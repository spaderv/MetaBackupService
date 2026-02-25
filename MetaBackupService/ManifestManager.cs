using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MetaBackupService
{
    /// <summary>
    /// Manages backup manifests for incremental backup detection
    /// Manifest tracks file sizes and modification times
    /// .NET Framework 4.0 compatible
    /// </summary>
    public class ManifestManager
    {
        private const string MANIFEST_FILENAME = "_manifest.json";
        private const double MTIME_TOLERANCE = 2.0; // seconds

        /// <summary>
        /// Loads manifest from disk as {relative_path: [size, mtime]}
        /// </summary>
        public Dictionary<string, Tuple<long, double>> LoadManifest(string monthFolder)
        {
            try
            {
                string manifestPath = Path.Combine(monthFolder, MANIFEST_FILENAME);
                
                if (!File.Exists(manifestPath))
                    return null;

                string json = File.ReadAllText(manifestPath);
                var parsed = SimpleJsonParser.Parse(json);

                if (parsed == null)
                    return null;

                var manifest = new Dictionary<string, Tuple<long, double>>();

                foreach (var kvp in parsed)
                {
                    try
                    {
                        var valArray = kvp.Value as List<object>;
                        if (valArray != null && valArray.Count >= 2)
                        {
                            long size = Convert.ToInt64(valArray[0]);
                            double mtime = Convert.ToDouble(valArray[1]);
                            manifest[kvp.Key] = new Tuple<long, double>(size, mtime);
                        }
                    }
                    catch { }
                }

                return manifest;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading manifest: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Saves manifest to disk as JSON
        /// </summary>
        public void SaveManifest(string monthFolder, Dictionary<string, Tuple<long, double>> manifest)
        {
            try
            {
                Directory.CreateDirectory(monthFolder);

                string manifestPath = Path.Combine(monthFolder, MANIFEST_FILENAME);
                string tempPath = manifestPath + ".tmp";

                var toSave = new Dictionary<string, object>();
                foreach (var kvp in manifest)
                {
                    toSave[kvp.Key] = new List<object> { kvp.Value.Item1, kvp.Value.Item2 };
                }

                string json = SimpleJsonParser.Stringify(toSave);
                File.WriteAllText(tempPath, json);

                if (File.Exists(manifestPath))
                    File.Delete(manifestPath);

                File.Move(tempPath, manifestPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving manifest: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds manifest from source folder (all files and their metadata)
        /// </summary>
        public Dictionary<string, Tuple<long, double>> BuildManifestFromSource(string sourceFolder)
        {
            var manifest = new Dictionary<string, Tuple<long, double>>();

            try
            {
                foreach (string filePath in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        FileInfo fi = new FileInfo(filePath);
                        string relativePath = GetRelativePath(filePath, sourceFolder);
                        
                        double mtime = fi.LastWriteTimeUtc.ToUnixTimestamp();
                        manifest[relativePath] = new Tuple<long, double>(fi.Length, mtime);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error building manifest from source: {ex.Message}");
            }

            return manifest;
        }

        /// <summary>
        /// Builds manifest from all backup folders in month folder
        /// </summary>
        public Dictionary<string, Tuple<long, double>> BuildManifestFromBackups(string monthFolder)
        {
            var manifest = new Dictionary<string, Tuple<long, double>>();

            try
            {
                var backupDirs = Directory.GetDirectories(monthFolder)
                    .Where(d => Path.GetFileName(d).StartsWith("Yedek_"))
                    .ToList();

                foreach (string backupDir in backupDirs)
                {
                    foreach (string filePath in Directory.EnumerateFiles(backupDir, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            FileInfo fi = new FileInfo(filePath);
                            string relativePath = GetRelativePath(filePath, backupDir);
                            
                            double mtime = fi.LastWriteTimeUtc.ToUnixTimestamp();
                            manifest[relativePath] = new Tuple<long, double>(fi.Length, mtime);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error building manifest from backups: {ex.Message}");
            }

            return manifest;
        }

        /// <summary>
        /// Checks if file has changed compared to manifest entry
        /// </summary>
        public bool HasFileChanged(FileInfo file, Tuple<long, double> manifestEntry)
        {
            if (manifestEntry == null)
                return true; // New file

            double currentMtime = file.LastWriteTimeUtc.ToUnixTimestamp();
            
            bool sameSizeNoNewerTime = (file.Length == manifestEntry.Item1) && 
                                      (currentMtime <= manifestEntry.Item2 + MTIME_TOLERANCE);
            
            return !sameSizeNoNewerTime;
        }

        /// <summary>
        /// Gets relative path between two paths
        /// </summary>
        private static string GetRelativePath(string fullPath, string basePath)
        {
            string full = Path.GetFullPath(fullPath);
            string baseNorm = Path.GetFullPath(basePath);

            if (!baseNorm.EndsWith(Path.DirectorySeparatorChar.ToString()))
                baseNorm += Path.DirectorySeparatorChar;

            if (full.StartsWith(baseNorm, StringComparison.OrdinalIgnoreCase))
            {
                return full.Substring(baseNorm.Length);
            }

            return full;
        }
    }

    /// <summary>
    /// Extension methods for DateTime to Unix timestamp conversion
    /// </summary>
    public static class DateTimeExtensions
    {
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static double ToUnixTimestamp(this DateTime dt)
        {
            return (dt - UnixEpoch).TotalSeconds;
        }

        public static DateTime FromUnixTimestamp(double timestamp)
        {
            return UnixEpoch.AddSeconds(timestamp);
        }
    }
}
