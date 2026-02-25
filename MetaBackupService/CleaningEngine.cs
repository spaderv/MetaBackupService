using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MetaBackupService
{
    /// <summary>
    /// Cleaning engine for deleting old files based on age threshold
    /// .NET Framework 4.0 compatible
    /// </summary>
    public class CleaningEngine
    {
        public delegate void ProgressCallback(int processed, int total);

        /// <summary>
        /// Calculates file age in days
        /// </summary>
        public static double CalculateFileAgeDays(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    FileInfo fi = new FileInfo(filePath);
                    double ageDays = (DateTime.Now - fi.LastWriteTime).TotalDays;
                    return ageDays;
                }
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Error calculating file age " + filePath + ": " + ex.Message);
            }

            return 0;
        }

        /// <summary>
        /// Deletes old files from local folder based on age threshold
        /// Returns (deletedFilesCount, deletedFoldersCount, freedSpaceGB)
        /// </summary>
        public static Tuple<int, int, double> DeleteOldFilesLocal(string folderPath, int daysThreshold, 
            ProgressCallback progressCallback = null)
        {
            int deletedFiles = 0;
            int deletedFolders = 0;
            long freedSpaceBytes = 0;

            try
            {
                if (!Directory.Exists(folderPath))
                {
                    LogManager.WriteLog("Folder not found: " + folderPath);
                    return new Tuple<int, int, double>(0, 0, 0);
                }

                var allFiles = Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories).ToList();
                int totalFiles = allFiles.Count;
                int processedFiles = 0;

                foreach (string filePath in allFiles)
                {
                    try
                    {
                        double ageDays = CalculateFileAgeDays(filePath);

                        if (ageDays > daysThreshold)
                        {
                            FileInfo fi = new FileInfo(filePath);
                            long fileSize = fi.Length;

                            File.Delete(filePath);
                            deletedFiles++;
                            freedSpaceBytes += fileSize;

                            LogManager.WriteLog("Deleted old file: " + filePath + " (age: " + ageDays.ToString("F1") + " days)");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.WriteLog("Error deleting file " + filePath + ": " + ex.Message);
                    }

                    processedFiles++;
                    progressCallback?.Invoke(processedFiles, totalFiles);
                }

                // Delete empty directories
                foreach (string dirPath in Directory.EnumerateDirectories(folderPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (Directory.GetFileSystemEntries(dirPath).Length == 0)
                        {
                            Directory.Delete(dirPath);
                            deletedFolders++;
                            LogManager.WriteLog("Deleted empty directory: " + dirPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.WriteLog("Error deleting directory " + dirPath + ": " + ex.Message);
                    }
                }

                double freedSpaceGB = freedSpaceBytes / (1024.0 * 1024.0 * 1024.0);
                return new Tuple<int, int, double>(deletedFiles, deletedFolders, freedSpaceGB);
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Error during cleaning: " + ex.Message);
                return new Tuple<int, int, double>(0, 0, 0);
            }
        }

        /// <summary>
        /// Main cleaning orchestrator
        /// </summary>
        public static Tuple<bool, string> RunCleaning(string sourceFolder, int daysThreshold, 
            string username = null, ProgressCallback progressCallback = null)
        {
            try
            {
                LogManager.WriteLog("Starting cleaning task: " + sourceFolder);
                LogManager.WriteLog("User: " + (username ?? "Unknown"));
                LogManager.WriteLog("Delete files older than: " + daysThreshold + " days");

                var cleanResult = DeleteOldFilesLocal(sourceFolder, daysThreshold, progressCallback);
                int deletedFiles = cleanResult.Item1;
                int deletedFolders = cleanResult.Item2;
                double freedSpaceGB = cleanResult.Item3;

                LogManager.WriteLog("Cleaning completed:");
                LogManager.WriteLog(" - Deleted files: " + deletedFiles);
                LogManager.WriteLog(" - Deleted folders: " + deletedFolders);
                LogManager.WriteLog(" - Freed space: " + freedSpaceGB.ToString("F2") + " GB");

                if (deletedFiles > 0 || deletedFolders > 0)
                {
                    string message = string.Format(
                        "Cleaning completed. {0} files, {1} folders deleted. {2:F2} GB freed.",
                        deletedFiles, deletedFolders, freedSpaceGB);
                    return new Tuple<bool, string>(true, message);
                }
                else
                {
                    string message = string.Format("No files older than {0} days found.", daysThreshold);
                    return new Tuple<bool, string>(true, message);
                }
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Cleaning error: " + ex.Message);
                return new Tuple<bool, string>(false, string.Format("Cleaning failed: {0}", ex.Message));
            }
        }
    }
}
