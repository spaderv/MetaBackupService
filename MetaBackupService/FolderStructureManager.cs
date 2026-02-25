using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MetaBackupService
{
    /// <summary>
    /// Manages backup folder structure and consolidation
    /// Creates year/month hierarchy, merges previous month backups
    /// .NET Framework 4.0 compatible
    /// </summary>
    public class FolderStructureManager
    {
        /// <summary>
        /// Creates year/month folder structure
        /// Returns (yearFolder, monthFolder) paths
        /// </summary>
        public static Tuple<string, string> CreateYearMonthStructure(string baseBackupPath)
        {
            DateTime now = DateTime.Now;
            int currentYear = now.Year;
            int currentMonth = now.Month;
            string monthName = GetMonthName(currentMonth);

            // Create local folder structure
            string yearFolder = Path.Combine(baseBackupPath, currentYear.ToString());
            string monthFolder = Path.Combine(yearFolder, string.Format("{0:D2}_{1}", currentMonth, monthName));

            if (!Directory.Exists(yearFolder))
                Directory.CreateDirectory(yearFolder);

            if (!Directory.Exists(monthFolder))
                Directory.CreateDirectory(monthFolder);

            MergePreviousMonth(yearFolder);

            return new Tuple<string, string>(yearFolder, monthFolder);
        }

        /// <summary>
        /// Lists all backup folders (Yedek_*) in a month folder, sorted by modification time
        /// </summary>
        public static List<string> ListBackups(string monthFolder)
        {
            var backups = new List<Tuple<string, DateTime>>();

            try
            {
                if (!Directory.Exists(monthFolder))
                    return new List<string>();

                foreach (string dir in Directory.GetDirectories(monthFolder))
                {
                    string dirName = Path.GetFileName(dir);
                    if (dirName.StartsWith("Yedek_"))
                    {
                        try
                        {
                            DateTime mtime = Directory.GetLastWriteTime(dir);
                            backups.Add(new Tuple<string, DateTime>(dir, mtime));
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error listing backups: {ex.Message}");
            }

            backups.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            return backups.Select(x => x.Item1).ToList();
        }

        /// <summary>
        /// Merges previous month's backups into single consolidated backup
        /// Keeps newest, deletes others
        /// </summary>
        public static void MergePreviousMonth(string yearFolder)
        {
            try
            {
                DateTime now = DateTime.Now;
                int currentYear = now.Year;
                int currentMonth = now.Month;

                DateTime prevDate = now.AddMonths(-1);
                int prevYear = prevDate.Year;
                int prevMonth = prevDate.Month;

                string prevYearFolder = prevYear == currentYear ? yearFolder : 
                    Path.Combine(Directory.GetParent(yearFolder).FullName, prevYear.ToString());
                string prevMonthName = GetMonthName(prevMonth);
                string prevMonthFolder = Path.Combine(prevYearFolder, string.Format("{0:D2}_{1}", prevMonth, prevMonthName));

                if (!Directory.Exists(prevMonthFolder))
                    return;

                var backups = ListBackups(prevMonthFolder);
                if (backups.Count <= 1)
                    return;

                // Sort by modification time, newest first
                backups = backups.OrderByDescending(b => Directory.GetLastWriteTime(b)).ToList();

                string newestPath = backups[0];
                string newestName = Path.GetFileName(newestPath);
                string mergedName = string.Format("Yedek_{0}", prevMonthName);
                string mergedPath = Path.Combine(prevMonthFolder, mergedName);

                // Rename newest to merged name if needed
                if (!newestName.Equals(mergedName, StringComparison.OrdinalIgnoreCase) && 
                    !Directory.Exists(mergedPath))
                {
                    try
                    {
                        Directory.Move(newestPath, mergedPath);
                        System.Diagnostics.Debug.WriteLine($"Renamed previous month newest backup to: {mergedName}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Could not rename backup: {ex.Message}");
                        return;
                    }
                }

                // Delete other backups
                foreach (string backupPath in backups.Skip(1))
                {
                    try
                    {
                        DeleteDirectory(backupPath);
                        System.Diagnostics.Debug.WriteLine($"Deleted old backup: {Path.GetFileName(backupPath)}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Could not delete backup {backupPath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error merging previous month: {ex.Message}");
            }
        }

        /// <summary>
        /// Deletes old backups, keeping only the last N
        /// </summary>
        public static int DeleteOldBackupsKeepLast(string monthFolder, int keep = 3)
        {
            int deleted = 0;

            try
            {
                if (!Directory.Exists(monthFolder))
                    return 0;

                var backups = ListBackups(monthFolder);
                backups = backups.OrderByDescending(b => Directory.GetLastWriteTime(b)).ToList();

                int toDeleteCount = Math.Max(0, backups.Count - keep);
                if (toDeleteCount <= 0)
                    return 0;

                foreach (string backupPath in backups.Skip(keep))
                {
                    try
                    {
                        DeleteDirectory(backupPath);
                        System.Diagnostics.Debug.WriteLine($"Deleted old backup: {Path.GetFileName(backupPath)}");
                        deleted++;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Could not delete backup {backupPath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting old backups: {ex.Message}");
            }

            return deleted;
        }

        /// <summary>
        /// Recursively deletes a directory with retry logic for locked files
        /// </summary>
        private static void DeleteDirectory(string path)
        {
            if (!Directory.Exists(path))
                return;

            try
            {
                // Remove read-only attributes
                foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        FileInfo fi = new FileInfo(file);
                        if ((fi.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                            fi.Attributes ^= FileAttributes.ReadOnly;
                    }
                    catch { }
                }

                Directory.Delete(path, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to delete {path}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Gets English month name
        /// </summary>
        private static string GetMonthName(int month)
        {
            string[] months = new[] 
            { 
                "January", "February", "March", "April", "May", "June",
                "July", "August", "September", "October", "November", "December"
            };
            return (month >= 1 && month <= 12) ? months[month - 1] : "Unknown";
        }
    }
}
