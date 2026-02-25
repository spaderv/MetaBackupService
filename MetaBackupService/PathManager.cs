using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MetaBackupService
{
    /// <summary>
    /// Path management service for sanitization, validation, and normalization
    /// Converts absolute paths to environment variables for portability
    /// .NET Framework 4.0 compatible
    /// </summary>
    public class PathManager
    {
        private readonly string _defaultPath;
        private static readonly string[] CommonEnvVars = new[] 
        { 
            "PROGRAMDATA", "APPDATA", "LOCALAPPDATA", "TEMP", "TMP", "USERPROFILE"
        };

        public PathManager(string defaultPath = null)
        {
            _defaultPath = defaultPath ?? Directory.GetCurrentDirectory();
        }

        /// <summary>
        /// Converts absolute Windows path to environment variable form
        /// Example: C:\Users\John\Documents -> %USERPROFILE%\Documents
        /// </summary>
        public string SanitizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !(path is string))
                return path;

            // Skip if already an environment variable
            if (path.StartsWith("%") && path.EndsWith("%"))
                return path;

            try
            {
                // Skip non-Windows paths
                if (path.StartsWith("\\\\") || path.StartsWith("ftp://") || path.StartsWith("/"))
                    return path;

                // Try USERPROFILE first
                string userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
                if (!string.IsNullOrEmpty(userProfile) && path.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase))
                {
                    string relative = GetRelativePath(path, userProfile);
                    if (relative == ".")
                        return "%USERPROFILE%";
                    return Path.Combine("%USERPROFILE%", relative);
                }

                // Try other common environment variables
                foreach (string envVar in CommonEnvVars)
                {
                    string envValue = Environment.GetEnvironmentVariable(envVar);
                    if (!string.IsNullOrEmpty(envValue) && path.StartsWith(envValue, StringComparison.OrdinalIgnoreCase))
                    {
                        string relative = GetRelativePath(path, envValue);
                        if (relative == ".")
                            return $"%{envVar}%";
                        return Path.Combine($"%{envVar}%", relative);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error sanitizing path {path}: {ex.Message}");
            }

            return path;
        }

        /// <summary>
        /// Validates path existence and expands environment variables
        /// Creates directory if missing (for directory type)
        /// </summary>
        public string ValidatePath(string path, string pathType = "directory")
        {
            if (string.IsNullOrWhiteSpace(path))
                return _defaultPath;

            // Expand environment variables
            string expanded = Environment.ExpandEnvironmentVariables(path);
            expanded = expanded.Replace("%USERPROFILE%", Environment.GetEnvironmentVariable("USERPROFILE"));

            // Check if path exists
            if (File.Exists(expanded) || Directory.Exists(expanded))
                return expanded;

            // Try to create if it's a directory type
            if (pathType.Equals("directory", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Directory.CreateDirectory(expanded);
                    if (Directory.Exists(expanded))
                        return expanded;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Could not create directory {expanded}: {ex.Message}");
                }
            }

            // Fallback to default
            System.Diagnostics.Debug.WriteLine($"Path {path} (expanded: {expanded}) not found. Using default: {_defaultPath}");
            return _defaultPath;
        }

        /// <summary>
        /// Sanitizes all paths in a task dictionary
        /// </summary>
        public Dictionary<string, object> SanitizeTaskPaths(Dictionary<string, object> task)
        {
            if (task == null)
                return task;

            var sanitized = new Dictionary<string, object>(task);

            if (sanitized.ContainsKey("source") && sanitized["source"] is string sourceStr)
            {
                if (!sourceStr.StartsWith("\\") && !sourceStr.StartsWith("ftp://") && !sourceStr.StartsWith("/") && !sourceStr.StartsWith("%"))
                {
                    if (sourceStr.Contains(":") || (sourceStr.StartsWith("\\") && !sourceStr.StartsWith("\\\\")))
                        sanitized["source"] = SanitizePath(sourceStr);
                }
            }

            if (sanitized.ContainsKey("dest") && sanitized["dest"] is string destStr)
            {
                if (!destStr.StartsWith("\\") && !destStr.StartsWith("ftp://") && !destStr.StartsWith("%"))
                {
                    if (destStr.Contains(":") || (destStr.StartsWith("\\") && !destStr.StartsWith("\\\\")))
                        sanitized["dest"] = SanitizePath(destStr);
                }
            }

            return sanitized;
        }

        /// <summary>
        /// Validates all paths in a task dictionary
        /// </summary>
        public Dictionary<string, object> ValidateTaskPaths(Dictionary<string, object> task)
        {
            if (task == null)
                return task;

            var validated = new Dictionary<string, object>(task);

            if (validated.ContainsKey("source") && validated["source"] is string sourceStr)
            {
                if (!sourceStr.StartsWith("\\") && !sourceStr.StartsWith("ftp://") && !sourceStr.StartsWith("/") && !sourceStr.StartsWith("%"))
                {
                    if (sourceStr.Contains(":") || (sourceStr.StartsWith("\\") && !sourceStr.StartsWith("\\\\")))
                        validated["source"] = ValidatePath(sourceStr, "directory");
                }
            }

            if (validated.ContainsKey("dest") && validated["dest"] is string destStr)
            {
                if (!destStr.StartsWith("\\") && !destStr.StartsWith("ftp://") && !destStr.StartsWith("%"))
                {
                    if (destStr.Contains(":") || (destStr.StartsWith("\\") && !destStr.StartsWith("\\\\")))
                        validated["dest"] = ValidatePath(destStr, "directory");
                }
            }

            return validated;
        }

        /// <summary>
        /// Gets the relative path between two paths
        /// </summary>
        private static string GetRelativePath(string fullPath, string basePath)
        {
            try
            {
                if (fullPath.Equals(basePath, StringComparison.OrdinalIgnoreCase))
                    return ".";

                // Normalize paths
                fullPath = Path.GetFullPath(fullPath);
                basePath = Path.GetFullPath(basePath);

                if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                {
                    string relative = fullPath.Substring(basePath.Length);
                    if (relative.StartsWith(Path.DirectorySeparatorChar.ToString()))
                        relative = relative.Substring(1);
                    return relative;
                }
            }
            catch { }

            return fullPath;
        }
    }
}
