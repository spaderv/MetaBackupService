using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MetaBackupService
{
    /// <summary>
    /// Core backup engine for local and remote backups
    /// Handles full and incremental backup logic
    /// .NET Framework 4.0 compatible
    /// </summary>
    public class BackupEngine
    {
        private readonly ManifestManager _manifestManager = new ManifestManager();
        private readonly PathManager _pathManager = new PathManager();

        public delegate void ProgressCallback(int processed, int total);

        /// <summary>
        /// Performs full backup (copies all files from source to new backup folder)
        /// </summary>
        public string FullBackupLocal(string sourceFolder, string destFolder, ProgressCallback progressCallback = null)
        {
            try
            {
                DateTime now = DateTime.Now;
                string backupFolderName = string.Format("Yedek_{0:yyyy-MM-dd_HH.mm.ss}", now);
                string backupFolderPath = Path.Combine(destFolder, backupFolderName);

                Directory.CreateDirectory(backupFolderPath);

                int totalFiles = Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories).Count();
                int backedUpCount = 0;

                LogManager.WriteLog("Counting files: " + totalFiles + " files to backup");

                foreach (string filePath in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        string relativePath = GetRelativePath(filePath, sourceFolder);
                        string destFilePath = Path.Combine(backupFolderPath, relativePath);

                        Directory.CreateDirectory(Path.GetDirectoryName(destFilePath));
                        File.Copy(filePath, destFilePath, true);

                        backedUpCount++;
                        progressCallback?.Invoke(backedUpCount, totalFiles);
                    }
                    catch (Exception ex)
                    {
                        LogManager.WriteLog("Error copying file " + filePath + ": " + ex.Message);
                    }
                }

                // Update manifest
                try
                {
                    var manifest = _manifestManager.BuildManifestFromSource(sourceFolder);
                    _manifestManager.SaveManifest(destFolder, manifest);
                }
                catch (Exception ex)
                {
                    LogManager.WriteLog("Could not save manifest after full backup: " + ex.Message);
                }

                LogManager.WriteLog("? Full backup completed: " + backedUpCount + " files backed up.");
                return backupFolderPath;
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("? Full backup error: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Performs incremental backup (copies only changed files)
        /// </summary>
        public string IncrementalBackupLocal(string sourceFolder, string destFolder, ProgressCallback progressCallback = null)
        {
            try
            {
                var manifest = _manifestManager.LoadManifest(destFolder);
                if (manifest == null)
                    manifest = _manifestManager.BuildManifestFromBackups(destFolder);

                if (manifest == null)
                    manifest = new Dictionary<string, Tuple<long, double>>();

                var filesToBackup = new List<Tuple<string, string>>(); // (sourcePath, relativePath)
                var seenFiles = new HashSet<string>();

                foreach (string filePath in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        FileInfo fi = new FileInfo(filePath);
                        string relativePath = GetRelativePath(filePath, sourceFolder);
                        seenFiles.Add(relativePath);

                        Tuple<long, double> manifestEntry = null;
                        if (manifest.ContainsKey(relativePath))
                            manifestEntry = manifest[relativePath];

                        if (_manifestManager.HasFileChanged(fi, manifestEntry))
                            filesToBackup.Add(new Tuple<string, string>(filePath, relativePath));
                    }
                    catch (Exception ex)
                    {
                        LogManager.WriteLog("Error checking file " + filePath + ": " + ex.Message);
                    }
                }

                // If no files changed, skip backup
                if (filesToBackup.Count == 0)
                {
                    LogManager.WriteLog("?? No changed files found; skipping incremental backup.");
                    progressCallback?.Invoke(0, 0);
                    return null;
                }

                LogManager.WriteLog("Found " + filesToBackup.Count + " changed files to backup");

                DateTime now = DateTime.Now;
                string backupFolderName = string.Format("Yedek_{0:yyyy-MM-dd_HH.mm.ss}", now);
                string backupFolderPath = Path.Combine(destFolder, backupFolderName);

                Directory.CreateDirectory(backupFolderPath);

                int totalFiles = filesToBackup.Count;
                int backedUpCount = 0;
                var copiedOk = new List<string>();

                foreach (var tuple in filesToBackup)
                {
                    try
                    {
                        string destFilePath = Path.Combine(backupFolderPath, tuple.Item2);
                        Directory.CreateDirectory(Path.GetDirectoryName(destFilePath));
                        File.Copy(tuple.Item1, destFilePath, true);
                        copiedOk.Add(tuple.Item2);
                    }
                    catch (Exception ex)
                    {
                        LogManager.WriteLog("Error copying file " + tuple.Item1 + ": " + ex.Message);
                    }
                    finally
                    {
                        backedUpCount++;
                        progressCallback?.Invoke(backedUpCount, totalFiles);
                    }
                }

                if (copiedOk.Count == 0)
                {
                    try { Directory.Delete(backupFolderPath); }
                    catch { }
                    return null;
                }

                // Update manifest with backed up files
                foreach (string relPath in copiedOk)
                {
                    try
                    {
                        string sourcePath = Path.Combine(sourceFolder, relPath);
                        FileInfo fi = new FileInfo(sourcePath);
                        double mtime = fi.LastWriteTimeUtc.ToUnixTimestamp();
                        manifest[relPath] = new Tuple<long, double>(fi.Length, mtime);
                    }
                    catch { }
                }

                // Remove deleted files from manifest
                var deletedFiles = manifest.Keys.Where(k => !seenFiles.Contains(k)).ToList();
                foreach (string relPath in deletedFiles)
                    manifest.Remove(relPath);

                _manifestManager.SaveManifest(destFolder, manifest);

                LogManager.WriteLog("? Incremental backup completed: " + copiedOk.Count + " files backed up.");
                return backupFolderPath;
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("? Incremental backup error: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Main backup orchestrator (combines source validation, structure creation, and backup execution)
        /// </summary>
        public string RunBackup(string sourceFolder, string destFolder, string username = null, 
            bool doFullBackup = false, int keepLast = 3, ProgressCallback progressCallback = null)
        {
            try
            {
                // Validate source
                if (!Directory.Exists(sourceFolder))
                {
                    LogManager.WriteLog("Source folder not found: " + sourceFolder);
                    return null;
                }

                LogManager.WriteLog("Starting backup: " + sourceFolder + " -> " + destFolder);
                LogManager.WriteLog("User: " + (username ?? "Unknown"));

                // Create folder structure
                var folderTuple = FolderStructureManager.CreateYearMonthStructure(destFolder);
                string monthFolder = folderTuple.Item2;

                // Get existing backups
                var existingBackups = FolderStructureManager.ListBackups(monthFolder);
                int backupCount = existingBackups.Count;

                LogManager.WriteLog("Existing backups: " + backupCount);

                string result = null;

                // Decide backup type
                if (doFullBackup || backupCount == 0)
                {
                    LogManager.WriteLog("Running FULL backup...");
                    result = FullBackupLocal(sourceFolder, monthFolder, progressCallback);
                }
                else
                {
                    LogManager.WriteLog("Running INCREMENTAL backup...");
                    result = IncrementalBackupLocal(sourceFolder, monthFolder, progressCallback);
                }

                // Cleanup old backups if full backup
                if (result != null && doFullBackup && keepLast > 0)
                {
                    int deleted = FolderStructureManager.DeleteOldBackupsKeepLast(monthFolder, keepLast);
                    LogManager.WriteLog("Deleted " + deleted + " old backups, keeping last " + keepLast);
                }

                return result;
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Backup error: " + ex.Message);
                return null;
            }
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
                return full.Substring(baseNorm.Length);

            return full;
        }
    }
}
