using System;
using System.Collections.Generic;
using System.Threading;

namespace MetaBackupService
{
    public class TaskQueueManager
    {
        private static TaskQueueManager _instance;
        private static readonly object _instanceLock = new object();

        private bool _isRunning = false;
        private Thread _queueWorkerThread;
        private bool _shouldStop = false;

        private TaskQueueManager()
        {
        }

        public static TaskQueueManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        if (_instance == null)
                        {
                            _instance = new TaskQueueManager();
                        }
                    }
                }
                return _instance;
            }
        }

        public void Start()
        {
            if (_isRunning)
                return;

            _shouldStop = false;
            _isRunning = true;

            _queueWorkerThread = new Thread(ProcessQueue)
            {
                IsBackground = true,
                Name = "TaskQueueWorker"
            };

            _queueWorkerThread.Start();
            LogManager.WriteLog("Task Queue Manager started");
        }

        public void Stop()
        {
            if (!_isRunning)
                return;

            _shouldStop = true;
            _isRunning = false;

            if (_queueWorkerThread != null)
            {
                _queueWorkerThread.Join(5000);
            }

            LogManager.WriteLog("Task Queue Manager stopped");
        }

        public string EnqueueTask(ScheduledTask scheduledTask)
        {
            try
            {
                var queueItem = new TaskQueueItem
                {
                    Name = scheduledTask.TaskDict.ContainsKey("name") ? scheduledTask.TaskDict["name"].ToString() : "Unknown",
                    Type = scheduledTask.TaskType,
                    Source = scheduledTask.TaskDict.ContainsKey("source") ? scheduledTask.TaskDict["source"].ToString() : "",
                    Dest = scheduledTask.TaskDict.ContainsKey("dest") ? scheduledTask.TaskDict["dest"].ToString() : "",
                    Username = scheduledTask.TaskDict.ContainsKey("username") ? scheduledTask.TaskDict["username"].ToString() : "Scheduler",
                    FullBackup = scheduledTask.TaskDict.ContainsKey("full") && (bool)scheduledTask.TaskDict["full"],
                    KeepLast = scheduledTask.TaskDict.ContainsKey("keep_last") ? Convert.ToInt32(scheduledTask.TaskDict["keep_last"]) : 3,
                    DeleteOlderThanDays = scheduledTask.TaskDict.ContainsKey("delete_older_than_days") ? Convert.ToInt32(scheduledTask.TaskDict["delete_older_than_days"]) : 30
                };

                string taskId = TaskQueue.EnqueueTask(queueItem);
                LogManager.WriteLog("Task enqueued: " + queueItem.Name + " (ID: " + taskId + ")");

                return taskId;
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Error enqueueing task: " + ex.Message);
                return null;
            }
        }

        private void ProcessQueue()
        {
            while (!_shouldStop)
            {
                try
                {
                    TaskQueueItem nextTask = TaskQueue.GetNextPendingTask();

                    if (nextTask != null)
                    {
                        LogManager.WriteLog("Processing queued task: " + nextTask.Name);

                        TaskQueue.UpdateTaskStatus(nextTask.Id, "running");
                        nextTask.Start();

                        try
                        {
                            ExecuteQueuedTask(nextTask);

                            nextTask.Complete();
                            TaskQueue.UpdateTaskStatus(nextTask.Id, "completed");

                            LogManager.WriteLog("Task completed: " + nextTask.Name);
                            
                            TaskQueue.RemoveTask(nextTask.Id);
                        }
                        catch (Exception ex)
                        {
                            nextTask.Fail(ex.Message);
                            TaskQueue.UpdateTaskStatus(nextTask.Id, "failed");

                            LogManager.WriteLog("Task failed: " + nextTask.Name + " - Error: " + ex.Message);
                            
                            TaskQueue.RemoveTask(nextTask.Id);
                        }
                    }
                    else
                    {
                        Thread.Sleep(5000);
                    }
                }
                catch (Exception ex)
                {
                    LogManager.WriteLog("Error in queue processing loop: " + ex.Message);
                    Thread.Sleep(5000);
                }
            }
        }

        private void ExecuteQueuedTask(TaskQueueItem queueItem)
        {
            if (queueItem.Type.Equals("backup", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteQueuedBackup(queueItem);
            }
            else if (queueItem.Type.Equals("cleaning", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteQueuedCleaning(queueItem);
            }
            else
            {
                throw new Exception("Unknown task type: " + queueItem.Type);
            }
        }

        private void ExecuteQueuedBackup(TaskQueueItem queueItem)
        {
            try
            {
                string progressLogFile = LogManager.CreateTaskLogFile("backup", queueItem.Name);
                queueItem.LogFilePath = progressLogFile;

                try { System.IO.File.Delete(progressLogFile.Replace(".txt", "_progress.txt")); } catch { }

                BackupEngine engine = new BackupEngine();

                int lastLogged = 0;
                BackupEngine.ProgressCallback progressCallback = (current, total) =>
                {
                    queueItem.FilesProcessed = current;
                    queueItem.TotalFiles = total;
                    
                    if (total > 0 && (current - lastLogged >= 5 || current == total))
                    {
                        string progressFile = progressLogFile.Replace(".txt", "_progress.txt");
                        int progressPercent = (current * 100) / total;
                        string progressLine = string.Format("Progress: {0}/{1} ({2}%)", current, total, progressPercent);
                        
                        try
                        {
                            System.IO.File.AppendAllText(progressFile, progressLine + Environment.NewLine);
                        }
                        catch { }

                        lastLogged = current;
                    }
                };

                string result = engine.RunBackup(queueItem.Source, queueItem.Dest, queueItem.Username, 
                    queueItem.FullBackup, queueItem.KeepLast, progressCallback);

                if (string.IsNullOrEmpty(result))
                {
                    LogManager.WriteLog("Backup completed with no changes");
                }
                else
                {
                    LogManager.WriteLog("Backup completed: " + result);
                }
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Backup error: " + ex.Message);
                throw;
            }
        }

        private void ExecuteQueuedCleaning(TaskQueueItem queueItem)
        {
            try
            {
                string progressLogFile = LogManager.CreateTaskLogFile("cleaning", queueItem.Name);
                queueItem.LogFilePath = progressLogFile;

                CleaningEngine.ProgressCallback progressCallback = (current, total) =>
                {
                    queueItem.FilesProcessed = current;
                    queueItem.TotalFiles = total;
                };

                var result = CleaningEngine.RunCleaning(queueItem.Source, queueItem.DeleteOlderThanDays, 
                    queueItem.Username, progressCallback);

                LogManager.WriteLog("Cleaning completed: " + result.Item2);
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Cleaning error: " + ex.Message);
                throw;
            }
        }

        public Dictionary<string, int> GetQueueStats()
        {
            return TaskQueue.GetQueueStats();
        }

        public List<TaskQueueItem> GetQueuedTasks()
        {
            return TaskQueue.LoadQueue();
        }
    }
}
