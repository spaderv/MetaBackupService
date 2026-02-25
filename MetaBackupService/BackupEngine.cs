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
        /// Supports resuming from interrupted backups
        /// </summary>
        public string FullBackupLocal(string sourceFolder, string destFolder, ProgressCallback progressCallback = null,
            Dictionary<string, object> resumeState = null, string taskId = null)
        {
            try
            {
                string backupFolderPath = null;
                int backedUpCount = 0;
                var backedUpFiles = new HashSet<string>();

                // Check if resuming from previous attempt
                if (resumeState != null && taskId != null && resumeState.ContainsKey("backup_folder"))
                {
                    backupFolderPath = resumeState["backup_folder"].ToString();
                    if (!Directory.Exists(backupFolderPath))
                    {
                        backupFolderPath = null;
                    }
                    else
                    {
                        // Get previously processed files
                        backedUpFiles = new HashSet<string>(TaskResumeManager.GetProcessedFiles(taskId));
                        backedUpCount = backedUpFiles.Count;
                    }
                }

                // Create new backup folder if not resuming
                if (string.IsNullOrEmpty(backupFolderPath))
                {
                    DateTime now = DateTime.Now;
                    string backupFolderName = string.Format("Yedek_{0:yyyy-MM-dd_HH.mm.ss}", now);
                    backupFolderPath = Path.Combine(destFolder, backupFolderName);
                    Directory.CreateDirectory(backupFolderPath);

                    if (taskId != null)
                        TaskResumeManager.SaveBackupFolder(taskId, backupFolderPath);
                }

                // Get all files once instead of enumerating twice
                string[] allFiles = Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories);
                int totalFiles = allFiles.Length;

                foreach (string filePath in allFiles)
                {
                    try
                    {
                        string relativePath = GetRelativePath(filePath, sourceFolder);
                        
                        // Skip if already processed
                        if (backedUpFiles.Contains(relativePath))
                            continue;

                        string destFilePath = Path.Combine(backupFolderPath, relativePath);

                        Directory.CreateDirectory(Path.GetDirectoryName(destFilePath));
                        File.Copy(filePath, destFilePath, true);

                        backedUpFiles.Add(relativePath);
                        backedUpCount++;
                        progressCallback?.Invoke(backedUpCount, totalFiles);

                        // Record progress periodically (every 20 files)
                        if (taskId != null && backedUpCount % 20 == 0)
                        {
                            TaskResumeManager.RecordProcessedFiles(taskId, new List<string>(backedUpFiles));
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.WriteLog("Error copying file " + filePath + ": " + ex.Message);
                    }
                }

                // Final record of all processed files
                if (taskId != null && backedUpFiles.Count > 0)
                    TaskResumeManager.RecordProcessedFiles(taskId, new List<string>(backedUpFiles));

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

                return backupFolderPath;
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Full backup error: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Performs incremental backup (copies only changed files)
        /// Supports resuming from interrupted backups
        /// </summary>
        public string IncrementalBackupLocal(string sourceFolder, string destFolder, ProgressCallback progressCallback = null,
            Dictionary<string, object> resumeState = null, string taskId = null)
        {
            try
            {
                var manifest = _manifestManager.LoadManifest(destFolder);
                if (manifest == null)
                    manifest = _manifestManager.BuildManifestFromBackups(destFolder);

                if (manifest == null)
                    manifest = new Dictionary<string, Tuple<long, double>>();

                var filesToBackup = new List<Tuple<string, string>>();
                var seenFiles = new HashSet<string>();
                var backedUpFiles = new HashSet<string>();

                // Check if resuming
                if (resumeState != null && taskId != null)
                {
                    backedUpFiles = new HashSet<string>(TaskResumeManager.GetProcessedFiles(taskId));
                }

                // Get all files once instead of enumerating directory twice
                string[] allFiles = Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories);
                
                foreach (string filePath in allFiles)
                {
                    try
                    {
                        FileInfo fi = new FileInfo(filePath);
                        string relativePath = GetRelativePath(filePath, sourceFolder);
                        seenFiles.Add(relativePath);

                        // Skip if already processed in this attempt
                        if (backedUpFiles.Contains(relativePath))
                            continue;

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

                // If no files to backup and no resume, skip
                if (filesToBackup.Count == 0 && backedUpFiles.Count == 0)
                {
                    progressCallback?.Invoke(0, 0);
                    return null;
                }

                string backupFolderPath;

                // Resume from previous folder or create new
                if (resumeState != null && taskId != null && resumeState.ContainsKey("backup_folder"))
                {
                    backupFolderPath = resumeState["backup_folder"].ToString();
                    if (!Directory.Exists(backupFolderPath))
                    {
                        DateTime now = DateTime.Now;
                        string backupFolderName = string.Format("Yedek_{0:yyyy-MM-dd_HH.mm.ss}", now);
                        backupFolderPath = Path.Combine(destFolder, backupFolderName);
                        Directory.CreateDirectory(backupFolderPath);
                    }
                }
                else
                {
                    DateTime now = DateTime.Now;
                    string backupFolderName = string.Format("Yedek_{0:yyyy-MM-dd_HH.mm.ss}", now);
                    backupFolderPath = Path.Combine(destFolder, backupFolderName);
                    Directory.CreateDirectory(backupFolderPath);
                }

                if (taskId != null)
                    TaskResumeManager.SaveBackupFolder(taskId, backupFolderPath);

                int totalFiles = filesToBackup.Count;
                int processedCount = backedUpFiles.Count;
                var copiedOk = new HashSet<string>(backedUpFiles);

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
                        processedCount++;
                        progressCallback?.Invoke(processedCount, totalFiles);

                        // Record progress periodically (every 20 files)
                        if (taskId != null && processedCount % 20 == 0)
                        {
                            TaskResumeManager.RecordProcessedFiles(taskId, new List<string>(copiedOk));
                        }
                    }
                }

                // Final record of all processed files
                if (taskId != null && copiedOk.Count > 0)
                    TaskResumeManager.RecordProcessedFiles(taskId, new List<string>(copiedOk));

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

                return backupFolderPath;
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Incremental backup error: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Main backup orchestrator (combines source validation, structure creation, and backup execution)
        /// Supports resuming interrupted backups
        /// </summary>
        public string RunBackup(string sourceFolder, string destFolder, string username = null, 
            bool doFullBackup = false, int keepLast = 3, ProgressCallback progressCallback = null,
            Dictionary<string, object> resumeState = null, string taskId = null)
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
                
                if (resumeState != null && taskId != null)
                {
                    LogManager.WriteLog("Resuming backup - Task ID: " + taskId);
                }

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
                    result = FullBackupLocal(sourceFolder, monthFolder, progressCallback, resumeState, taskId);
                }
                else
                {
                    LogManager.WriteLog("Running INCREMENTAL backup...");
                    result = IncrementalBackupLocal(sourceFolder, monthFolder, progressCallback, resumeState, taskId);
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
