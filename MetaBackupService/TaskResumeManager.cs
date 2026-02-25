using System;
using System.Collections.Generic;
using System.IO;

namespace MetaBackupService
{
    /// <summary>
    /// Manages resumable task state persistence
    /// Saves/loads backup progress to allow resuming interrupted backups
    /// </summary>
    public static class TaskResumeManager
    {
        private static readonly object _lockObject = new object();

        public static string GetResumeStateDir()
        {
            string configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MetaBackup", "resume");
            
            if (!Directory.Exists(configDir))
                Directory.CreateDirectory(configDir);

            return configDir;
        }

        public static string GetResumeStatePath(string taskId)
        {
            return Path.Combine(GetResumeStateDir(), taskId + ".json");
        }

        /// <summary>
        /// Save task resume state to disk
        /// Contains information about which files have been processed
        /// </summary>
        public static void SaveResumeState(string taskId, Dictionary<string, object> state)
        {
            lock (_lockObject)
            {
                try
                {
                    string resumePath = GetResumeStatePath(taskId);
                    
                    var stateData = new Dictionary<string, object>
                    {
                        { "task_id", taskId },
                        { "saved_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                        { "state", state }
                    };

                    string json = SimpleJsonParser.Stringify(stateData);
                    string tempPath = resumePath + ".tmp";

                    File.WriteAllText(tempPath, json);

                    if (File.Exists(resumePath))
                        File.Delete(resumePath);

                    File.Move(tempPath, resumePath);
                }
                catch (Exception ex)
                {
                    LogManager.WriteLog("Error saving resume state: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Load task resume state from disk
        /// </summary>
        public static Dictionary<string, object> LoadResumeState(string taskId)
        {
            lock (_lockObject)
            {
                try
                {
                    string resumePath = GetResumeStatePath(taskId);

                    if (!File.Exists(resumePath))
                        return null;

                    string json = File.ReadAllText(resumePath);
                    var stateData = SimpleJsonParser.Parse(json) as Dictionary<string, object>;

                    if (stateData != null && stateData.ContainsKey("state"))
                    {
                        var state = stateData["state"] as Dictionary<string, object>;
                        return state;
                    }

                    return null;
                }
                catch (Exception ex)
                {
                    LogManager.WriteLog("Error loading resume state: " + ex.Message);
                    return null;
                }
            }
        }

        /// <summary>
        /// Delete resume state (after task completion or max retries exceeded)
        /// </summary>
        public static void DeleteResumeState(string taskId)
        {
            lock (_lockObject)
            {
                try
                {
                    string resumePath = GetResumeStatePath(taskId);

                    if (File.Exists(resumePath))
                    {
                        File.Delete(resumePath);
                    }
                }
                catch (Exception ex)
                {
                    LogManager.WriteLog("Error deleting resume state: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Record which files have been successfully processed
        /// </summary>
        public static void RecordProcessedFiles(string taskId, List<string> processedFiles)
        {
            try
            {
                var state = LoadResumeState(taskId) ?? new Dictionary<string, object>();
                
                var fileList = new List<object>();
                foreach (string file in processedFiles)
                {
                    fileList.Add(file);
                }

                state["processed_files"] = fileList;
                state["files_count"] = processedFiles.Count;
                state["last_update"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                SaveResumeState(taskId, state);
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Error recording processed files: " + ex.Message);
            }
        }

        /// <summary>
        /// Get list of files that were already processed
        /// </summary>
        public static List<string> GetProcessedFiles(string taskId)
        {
            try
            {
                var state = LoadResumeState(taskId);
                if (state == null || !state.ContainsKey("processed_files"))
                    return new List<string>();

                var fileList = state["processed_files"] as List<object>;
                if (fileList == null)
                    return new List<string>();

                var result = new List<string>();
                foreach (var file in fileList)
                {
                    result.Add(file.ToString());
                }

                return result;
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Error getting processed files: " + ex.Message);
                return new List<string>();
            }
        }

        /// <summary>
        /// Save backup folder path for incremental resume
        /// </summary>
        public static void SaveBackupFolder(string taskId, string backupFolderPath)
        {
            try
            {
                var state = LoadResumeState(taskId) ?? new Dictionary<string, object>();
                state["backup_folder"] = backupFolderPath;
                state["backup_folder_created"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                SaveResumeState(taskId, state);
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Error saving backup folder: " + ex.Message);
            }
        }

        /// <summary>
        /// Get previously created backup folder for resuming
        /// </summary>
        public static string GetBackupFolder(string taskId)
        {
            try
            {
                var state = LoadResumeState(taskId);
                if (state != null && state.ContainsKey("backup_folder"))
                {
                    string folder = state["backup_folder"].ToString();
                    if (Directory.Exists(folder))
                        return folder;
                }
                return null;
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Error getting backup folder: " + ex.Message);
                return null;
            }
        }
    }
}

