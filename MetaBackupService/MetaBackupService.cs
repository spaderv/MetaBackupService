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
                _taskRequestTimer = new Timer(CheckTaskRequests, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

                WriteLog("MetaBackup Service started successfully. Tasks scheduled: " + _scheduledTasks.Count);
                LogManager.WriteSeparator();

                // Send startup notification to Telegram
                try
                {
                    if (TelegramNotificationService.IsConfigured())
                    {
                        TelegramNotificationService.SendSuccess(
                            "MetaBackup Service Started",
                            "Service is running and ready",
                            "?? Tasks scheduled: " + _scheduledTasks.Count + "\n?? Next run in: 1 minute"
                        );
                    }
                }
                catch (Exception ex)
                {
                    LogManager.WriteLog("Error sending Telegram notification: " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                WriteLog("ERROR starting service: " + ex.Message);
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
                
                if (!Directory.Exists(requestDir))
                {
                    Directory.CreateDirectory(requestDir);
                    return;
                }

                string[] requestFiles = Directory.GetFiles(requestDir, "*.json");
                
                foreach (string requestFile in requestFiles)
                {
                    try
                    {
                        string json = File.ReadAllText(requestFile);
                        var request = SimpleJsonParser.Parse(json) as Dictionary<string, object>;

                        if (request == null)
                            continue;

                        string taskId = request.ContainsKey("task_id") ? request["task_id"].ToString() : null;
                        string taskName = request.ContainsKey("task_name") ? request["task_name"].ToString() : "Unknown";

                        if (string.IsNullOrEmpty(taskId))
                        {
                            LogManager.WriteLog("Task request missing task_id");
                            try { File.Delete(requestFile); } catch { }
                            continue;
                        }

                        LogManager.WriteLog("Processing task request: " + taskName + " (ID: " + taskId + ")");

                        // Try to find task in scheduled tasks first
                        ScheduledTask taskToRun = _scheduledTasks.FirstOrDefault(t => 
                            t.TaskDict.ContainsKey("id") && t.TaskDict["id"].ToString() == taskId);

                        if (taskToRun != null)
                        {
                            // Found in scheduled tasks - use it
                            LogManager.WriteLog("Task found in scheduled tasks, enqueueing: " + taskName);
                            _queueManager.EnqueueTask(taskToRun);
                        }
                        else
                        {
                            // Not in scheduled tasks - this is a manually triggered task
                            // Create a TaskQueueItem directly and enqueue it
                            LogManager.WriteLog("Task not in scheduled tasks (manual trigger), creating queue item: " + taskName);
                            
                            var queueItem = new TaskQueueItem
                            {
                                Id = taskId,
                                Name = taskName,
                                Type = request.ContainsKey("task_type") ? request["task_type"].ToString() : "backup",
                                Status = "pending",
                                Source = request.ContainsKey("source") ? request["source"].ToString() : "",
                                Dest = request.ContainsKey("dest") ? request["dest"].ToString() : "",
                                Username = request.ContainsKey("username") ? request["username"].ToString() : "",
                                FullBackup = request.ContainsKey("full_backup") && (bool)request["full_backup"],
                                KeepLast = request.ContainsKey("keep_last") ? Convert.ToInt32(request["keep_last"]) : 3,
                                DeleteOlderThanDays = request.ContainsKey("delete_older_than_days") ? Convert.ToInt32(request["delete_older_than_days"]) : 30,
                                CreatedAt = DateTime.Now,
                                RetryCount = 0,
                                MaxRetries = 3
                            };

                            TaskQueue.EnqueueTask(queueItem);
                            LogManager.WriteLog("Manually triggered task enqueued: " + taskName);
                        }

                        // Delete request file after processing
                        try { File.Delete(requestFile); } catch { }
                    }
                    catch (Exception ex)
                    {
                        LogManager.WriteLog("Error processing task request: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Error in CheckTaskRequests: " + ex.Message);
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
                
                foreach (ScheduledTask task in _scheduledTasks)
                {
                    if (ShouldTaskRun(task, now))
                    {
                        TimeSpan timeSinceLastRun = now - task.LastRun;
                        if (timeSinceLastRun.TotalSeconds >= 60)
                        {
                            task.LastRun = now;
                            string taskName = task.TaskDict.ContainsKey("name") ? task.TaskDict["name"].ToString() : "Unknown";
                            LogManager.WriteLog("Task triggered: " + taskName);

                            _queueManager.EnqueueTask(task);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Error in scheduler: " + ex.Message);
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
