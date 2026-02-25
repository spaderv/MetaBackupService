using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;

namespace MetaBackupService
{
    /// <summary>
    /// MetaBackup Windows Service - Main entry point
    /// Manages scheduled backup and cleaning tasks
    /// .NET Framework 4.0 compatible
    /// </summary>
    public partial class MetaBackupService : ServiceBase
    {
        private readonly BackupEngine _backupEngine = new BackupEngine();
        private Timer _schedulerTimer;
        private Timer _configReloadTimer;
        private Timer _taskRequestTimer;
        private Dictionary<string, object> _currentConfig;
        private List<ScheduledTask> _scheduledTasks = new List<ScheduledTask>();
        private string _lastConfigHash = "";
        private TaskQueueManager _queueManager;

        public MetaBackupService()
        {
            ServiceName = "MetaBackupService";
            // DisplayName is optional and may not be available in some .NET 4.0 configurations
            // DisplayName = "MetaBackup Scheduler Service";
            CanStop = true;
            CanPauseAndContinue = true;
        }

        /// <summary>
        /// Service start handler
        /// </summary>
        protected override void OnStart(string[] args)
        {
            try
            {
                string logDir = GetLogDir();
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                LogManager.WriteSeparator();
                LogManager.WriteLog("MetaBackup Service starting at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                LogManager.WriteLog("Service version: C# implementation");
                
                try
                {
                    LogManager.WriteLog("Process user: " + System.Security.Principal.WindowsIdentity.GetCurrent().Name);
                }
                catch { }
                
                LogManager.WriteLog("Log directory: " + logDir);
                LogManager.WriteSeparator();

                WriteLog("Loading configuration...");
                LoadConfiguration();

                WriteLog("Building scheduled tasks...");
                BuildScheduledTasks();

                WriteLog("Starting queue manager...");
                _queueManager = TaskQueueManager.Instance;
                _queueManager.Start();

                WriteLog("Starting scheduler timer...");
                _schedulerTimer = new Timer(CheckScheduledTasks, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));

                WriteLog("Starting config reload timer...");
                _configReloadTimer = new Timer(CheckConfigReload, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

                WriteLog("Starting task request monitor...");
                _taskRequestTimer = new Timer(CheckTaskRequests, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

                WriteLog("MetaBackup Service started successfully. Tasks scheduled: " + _scheduledTasks.Count);
                LogManager.WriteSeparator();
            }
            catch (Exception ex)
            {
                WriteLog("ERROR starting service: " + ex.Message);
                WriteLog("StackTrace: " + ex.StackTrace);
                throw;
            }
        }

        /// <summary>
        /// Monitor for task requests from GUI
        /// </summary>
        private void CheckTaskRequests(object state)
        {
            try
            {
                string requestDir = Path.Combine(GetConfigDir(), "task-requests");
                
                WriteLog("DEBUG: Checking for task requests in: " + requestDir);
                WriteLog("DEBUG: Directory exists: " + Directory.Exists(requestDir));
                
                if (!Directory.Exists(requestDir))
                {
                    WriteLog("DEBUG: Request directory doesn't exist, creating it");
                    Directory.CreateDirectory(requestDir);
                    return;
                }

                string[] requestFiles = Directory.GetFiles(requestDir, "*.json");
                WriteLog("DEBUG: Found " + requestFiles.Length + " request files");
                
                foreach (string requestFile in requestFiles)
                {
                    WriteLog("DEBUG: Processing request file: " + requestFile);
                    try
                    {
                        string json = File.ReadAllText(requestFile);
                        WriteLog("DEBUG: Request JSON: " + json);
                        
                        var request = SimpleJsonParser.Parse(json);

                        string taskId = null;
                        string taskName = null;
                        
                        if (request.ContainsKey("task_id"))
                            taskId = request["task_id"].ToString();
                        if (request.ContainsKey("task_name"))
                            taskName = request["task_name"].ToString();

                        WriteLog("DEBUG: Looking for task ID: " + (taskId ?? "null") + ", Name: " + (taskName ?? "null"));
                        WriteLog("DEBUG: Total scheduled tasks: " + _scheduledTasks.Count);
                        
                        foreach (var st in _scheduledTasks)
                        {
                            string stName = st.TaskDict.ContainsKey("name") ? st.TaskDict["name"].ToString() : "NoName";
                            string stId = st.TaskDict.ContainsKey("id") ? st.TaskDict["id"].ToString() : "NoID";
                            WriteLog("DEBUG: Available task - ID: " + stId + ", Name: " + stName);
                        }

                        ScheduledTask taskToRun = null;
                        
                        // Try to find by ID first
                        if (!string.IsNullOrEmpty(taskId))
                        {
                            taskToRun = _scheduledTasks.FirstOrDefault(t => 
                                t.TaskDict.ContainsKey("id") && t.TaskDict["id"].ToString() == taskId);
                            WriteLog("DEBUG: Search by ID result: " + (taskToRun != null ? "FOUND" : "NOT FOUND"));
                        }
                        
                        // Fallback: try to find by name
                        if (taskToRun == null && !string.IsNullOrEmpty(taskName))
                        {
                            taskToRun = _scheduledTasks.FirstOrDefault(t => 
                                t.TaskDict.ContainsKey("name") && t.TaskDict["name"].ToString() == taskName);
                            WriteLog("DEBUG: Search by name result: " + (taskToRun != null ? "FOUND" : "NOT FOUND"));
                        }

                        if (taskToRun != null)
                        {
                            string displayName = taskToRun.TaskDict.ContainsKey("name") ? 
                                taskToRun.TaskDict["name"].ToString() : (taskId ?? "Unknown");
                            WriteLog("?? Enqueueing manually requested task: " + displayName);
                            
                            _queueManager.EnqueueTask(taskToRun);
                        }
                        else
                        {
                            WriteLog("?? Task request received but task not found. ID: " + (taskId ?? "null") + ", Name: " + (taskName ?? "null"));
                        }

                        // Delete request file after processing
                        try { File.Delete(requestFile); } catch { }
                    }
                    catch (Exception ex)
                    {
                        WriteLog("Error processing task request: " + ex.Message);
                        WriteLog("StackTrace: " + ex.StackTrace);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog("Error in CheckTaskRequests: " + ex.Message);
                WriteLog("StackTrace: " + ex.StackTrace);
            }
        }

        private string GetConfigDir()
        {
            string programData = Environment.GetEnvironmentVariable("PROGRAMDATA");
            if (!string.IsNullOrEmpty(programData))
            {
                return Path.Combine(programData, "MetaBackup");
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MetaBackup");
        }

        /// <summary>
        /// Service stop handler
        /// </summary>
        protected override void OnStop()
        {
            try
            {
                WriteLog("MetaBackup Service stopping...");

                if (_queueManager != null)
                {
                    _queueManager.Stop();
                }

                if (_schedulerTimer != null)
                {
                    _schedulerTimer.Dispose();
                    _schedulerTimer = null;
                }

                if (_configReloadTimer != null)
                {
                    _configReloadTimer.Dispose();
                    _configReloadTimer = null;
                }

                if (_taskRequestTimer != null)
                {
                    _taskRequestTimer.Dispose();
                    _taskRequestTimer = null;
                }

                _scheduledTasks.Clear();

                WriteLog("MetaBackup Service stopped successfully.");
            }
            catch (Exception ex)
            {
                WriteLog("Error stopping service: " + ex.Message);
            }
        }

        /// <summary>
        /// Loads configuration from file
        /// </summary>
        private void LoadConfiguration()
        {
            try
            {
                string configPath = GetConfigPath();

                if (!File.Exists(configPath))
                {
                    WriteLog("Config file not found at: " + configPath);
                    _currentConfig = new Dictionary<string, object>
                    {
                        { "username", "" },
                        { "password_hash", "" },
                        { "tasks", new List<Dictionary<string, object>>() },
                        { "telegram", new Dictionary<string, object>
                        {
                            { "bot_token_enc", "" },
                            { "chat_ids", new List<string>() },
                            { "message_filter", "failure_only" }
                        }},
                        { "watchdog", new Dictionary<string, object>
                        {
                            { "services", new List<string>() },
                            { "manually_stopped", new List<string>() }
                        }}
                    };
                }
                else
                {
                    string json = File.ReadAllText(configPath);
                    _currentConfig = SimpleJsonParser.Parse(json);
                }

                WriteLog("Configuration loaded successfully.");
            }
            catch (Exception ex)
            {
                WriteLog("Error loading configuration: " + ex.Message);
                _currentConfig = new Dictionary<string, object>();
            }
        }

        /// <summary>
        /// Builds scheduled tasks from configuration
        /// </summary>
        private void BuildScheduledTasks()
        {
            try
            {
                _scheduledTasks.Clear();

                if (_currentConfig == null || !_currentConfig.ContainsKey("tasks"))
                    return;

                var tasks = _currentConfig["tasks"] as List<object>;
                if (tasks == null)
                    return;

                foreach (object taskObj in tasks)
                {
                    var task = taskObj as Dictionary<string, object>;
                    if (task == null)
                        continue;

                    // Skip disabled tasks
                    if (task.ContainsKey("enabled") && task["enabled"] is bool && !(bool)task["enabled"])
                        continue;

                    string taskType = task.ContainsKey("type") ? task["type"].ToString() : "backup";

                    try
                    {
                        var times = CronExpressionHelper.NormalizeTimes(task.ContainsKey("times") ? task["times"] : null);
                        if (times.Count == 0)
                        {
                            WriteLog("Skipping task (no times): " + (task.ContainsKey("name") ? task["name"] : "Unknown"));
                            continue;
                        }

                        var daysOfWeek = CronExpressionHelper.NormalizeDaysOfWeek(task.ContainsKey("days") ? task["days"] : null);

                        var scheduledTask = new ScheduledTask
                        {
                            TaskDict = task,
                            TaskType = taskType,
                            Times = times,
                            DaysOfWeek = daysOfWeek,
                            LastRun = DateTime.MinValue
                        };

                        _scheduledTasks.Add(scheduledTask);

                        WriteLog(string.Format("Added {0} task: {1}", taskType, 
                            task.ContainsKey("name") ? task["name"] : "Unknown"));
                    }
                    catch (Exception ex)
                    {
                        WriteLog("Error building task: " + ex.Message);
                    }
                }

                WriteLog("Total tasks scheduled: " + _scheduledTasks.Count);
            }
            catch (Exception ex)
            {
                WriteLog("Error building scheduled tasks: " + ex.Message);
            }
        }

        /// <summary>
        /// Timer callback - checks if any scheduled tasks should run
        /// </summary>
        private void CheckScheduledTasks(object state)
        {
            try
            {
                DateTime now = DateTime.Now;
                
                if (now.Second == 0)
                {
                    LogManager.WriteLog(string.Format("? Scheduler check - Time: {0:HH:mm} | Tasks: {1}", now, _scheduledTasks.Count));
                    
                    foreach (ScheduledTask task in _scheduledTasks)
                    {
                        string taskName = task.TaskDict.ContainsKey("name") ? task.TaskDict["name"].ToString() : "Unknown";
                        string times = string.Join(", ", task.Times.Select(t => string.Format("{0:D2}:{1:D2}", t.Item1, t.Item2)));
                        string days = task.DaysOfWeek.Count > 0 ? string.Join(", ", task.DaysOfWeek) : "Every day";
                        LogManager.WriteLog(string.Format("  ? {0} | Times: {1} | Days: {2}", taskName, times, days));
                    }
                }

                foreach (ScheduledTask task in _scheduledTasks)
                {
                    if (ShouldTaskRun(task, now))
                    {
                        TimeSpan timeSinceLastRun = now - task.LastRun;
                        if (timeSinceLastRun.TotalSeconds >= 60)
                        {
                            task.LastRun = now;
                            string taskName = task.TaskDict.ContainsKey("name") ? task.TaskDict["name"].ToString() : "Unknown";
                            LogManager.WriteSeparator();
                            LogManager.WriteLog("?? TASK TRIGGERED: " + taskName);
                            LogManager.WriteLog("Scheduled time: " + string.Format("{0:HH:mm}", now));
                            LogManager.WriteSeparator();

                            _queueManager.EnqueueTask(task);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("ERROR in scheduler: " + ex.Message);
                LogManager.WriteLog("StackTrace: " + ex.StackTrace);
            }
        }

        /// <summary>
        /// Determines if a task should run at the current time
        /// </summary>
        private static bool ShouldTaskRun(ScheduledTask task, DateTime now)
        {
            try
            {
                // Check if current time matches any scheduled time
                bool timeMatches = false;
                foreach (var time in task.Times)
                {
                    if (time.Item1 == now.Hour && time.Item2 == now.Minute)
                    {
                        timeMatches = true;
                        break;
                    }
                }

                if (!timeMatches)
                    return false;

                // Check if current day matches (or all days if empty)
                if (task.DaysOfWeek.Count == 0)
                    return true;

                return task.DaysOfWeek.Contains(now.DayOfWeek);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Executes a scheduled task (backup or cleaning)
        /// DEPRECATED - Tasks are now queued and executed by TaskQueueManager
        /// </summary>
        private void ExecuteTask(ScheduledTask task)
        {
            // This method is no longer called - tasks are queued instead
            LogManager.WriteLog("WARNING: ExecuteTask called directly (should use queue instead)");
        }

        /// <summary>
        /// Executes a backup task
        /// DEPRECATED - Use TaskQueueManager instead
        /// </summary>
        private void ExecuteBackupTask(ScheduledTask task)
        {
            // This method is no longer called - use TaskQueueManager
        }

        /// <summary>
        /// Executes a cleaning task
        /// DEPRECATED - Use TaskQueueManager instead
        /// </summary>
        private void ExecuteCleaningTask(ScheduledTask task)
        {
            // This method is no longer called - use TaskQueueManager
        }

        /// <summary>
        /// Saves configuration back to file
        /// </summary>
        private void SaveConfiguration()
        {
            try
            {
                if (_currentConfig == null)
                    return;

                string configPath = GetConfigPath();
                string json = SimpleJsonParser.Stringify(_currentConfig);
                string tempPath = configPath + ".tmp";

                File.WriteAllText(tempPath, json);

                if (File.Exists(configPath))
                    File.Delete(configPath);

                File.Move(tempPath, configPath);
            }
            catch (Exception ex)
            {
                WriteLog("Error saving configuration: " + ex.Message);
            }
        }

        /// <summary>
        /// Gets configuration file path
        /// </summary>
        private static string GetConfigPath()
        {
            string programData = Environment.GetEnvironmentVariable("PROGRAMDATA");
            if (!string.IsNullOrEmpty(programData))
            {
                return Path.Combine(programData, "MetaBackup", "config.json");
            }

            string appData = Environment.GetEnvironmentVariable("APPDATA");
            if (!string.IsNullOrEmpty(appData))
            {
                return Path.Combine(appData, "MetaBackup", "config.json");
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MetaBackup", "config.json");
        }

        /// <summary>
        /// Writes message to event log
        /// </summary>
        private void WriteLog(string message)
        {
            LogManager.WriteLog(message);
        }

        private static string GetLogDir()
        {
            try
            {
                string programData = Environment.GetEnvironmentVariable("PROGRAMDATA");
                if (!string.IsNullOrEmpty(programData))
                {
                    return Path.Combine(programData, "MetaBackup", "Logs");
                }
                return "UNKNOWN";
            }
            catch
            {
                return "ERROR";
            }
        }

        /// <summary>
        /// Check if config file has changed and reload if needed
        /// </summary>
        private void CheckConfigReload(object state)
        {
            try
            {
                string configPath = GetConfigPath();
                if (!File.Exists(configPath))
                    return;

                string json = File.ReadAllText(configPath);
                string currentHash = GetHash(json);

                if (currentHash != _lastConfigHash)
                {
                    WriteLog("?? Config file changed, reloading...");
                    _lastConfigHash = currentHash;
                    LoadConfiguration();
                    BuildScheduledTasks();
                    WriteLog("? Config reloaded. Tasks scheduled: " + _scheduledTasks.Count);
                }
            }
            catch (Exception ex)
            {
                WriteLog("Error checking config reload: " + ex.Message);
            }
        }

        /// <summary>
        /// Get simple hash of string for change detection
        /// </summary>
        private string GetHash(string text)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
                return Convert.ToBase64String(hash);
            }
        }
    }

    /// <summary>
    /// Helper class to track scheduled tasks
    /// </summary>
    public class ScheduledTask
    {
        public Dictionary<string, object> TaskDict { get; set; }
        public string TaskType { get; set; }
        public List<Tuple<int, int>> Times { get; set; }
        public List<DayOfWeek> DaysOfWeek { get; set; }
        public DateTime LastRun { get; set; }
    }

    /// <summary>
    /// Service entry point for Windows Service installation
    /// </summary>
    static class Program
    {
        static void Main()
        {
            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[]
            {
                new MetaBackupService()
            };
            ServiceBase.Run(ServicesToRun);
        }
    }
}
