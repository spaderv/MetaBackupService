using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            // IMPORTANT: Load persisted queue from disk on startup
            // This recovers tasks that were interrupted before the crash
            try
            {
                LoadPersistedQueue();
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Error loading persisted queue on startup: " + ex.Message);
            }

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
            int checkCleanupCounter = 0;
            
            while (!_shouldStop)
            {
                try
                {
                    TaskQueueItem nextTask = TaskQueue.GetNextPendingTask();

                    if (nextTask != null)
                    {
                        TaskQueue.UpdateTaskStatus(nextTask.Id, "running");
                        nextTask.Start();

                        try
                        {
                            ExecuteQueuedTask(nextTask);

                            nextTask.Complete();
                            TaskQueue.UpdateTaskStatus(nextTask.Id, "completed");

                            TaskResumeManager.DeleteResumeState(nextTask.Id);
                            TaskQueue.RemoveTask(nextTask.Id);
                        }
                        catch (Exception ex)
                        {
                            nextTask.Fail(ex.Message);
                            TaskQueue.UpdateTaskStatus(nextTask.Id, "failed");

                            // Check if we can retry
                            if (nextTask.CanRetry())
                            {
                                nextTask.PrepareForRetry();
                                TaskQueue.UpdateTaskStatus(nextTask.Id, "pending");
                                LogManager.WriteLog("Task will retry - Attempt " + (nextTask.RetryCount) + " of " + nextTask.MaxRetries + ": " + nextTask.Name);
                            }
                            else
                            {
                                TaskResumeManager.DeleteResumeState(nextTask.Id);
                                TaskQueue.RemoveTask(nextTask.Id);
                                LogManager.WriteLog("Task failed (no more retries): " + nextTask.Name + " - Error: " + ex.Message);
                            }
                        }
                        
                        // Every 10 tasks, cleanup old completed/failed tasks
                        checkCleanupCounter++;
                        if (checkCleanupCounter >= 10)
                        {
                            TaskQueue.CleanupOldCompletedTasks(24);  // Remove tasks older than 24 hours
                            checkCleanupCounter = 0;
                        }
                    }
                    else
                    {
                        // No pending tasks - cleanup old tasks
                        TaskQueue.CleanupOldCompletedTasks(24);
                        Thread.Sleep(1000);  // Check more frequently when idle
                    }
                }
                catch (Exception ex)
                {
                    LogManager.WriteLog("Error in queue processing: " + ex.Message);
                    Thread.Sleep(1000);
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
            string progressFile = Path.Combine(
                LogManager.GetLogDirectory(),
                "progress.txt");
            
            DateTime startTime = DateTime.Now;
            
            BackupEngine engine = new BackupEngine();
            
            // Use a class to hold mutable state for closure
            var progressTracker = new ProgressTracker();

            // Progress callback - Write to progress.txt when percentage changes by 1%
            BackupEngine.ProgressCallback progressCallback = (current, total) =>
            {
                queueItem.FilesProcessed = current;
                queueItem.TotalFiles = total;
                
                // Save to progress.txt when percentage increases by 1%
                if (total > 0)
                {
                    int currentPercent = (current * 100) / total;
                    if (currentPercent > progressTracker.LastPercent)
                    {
                        try
                        {
                            string progressLine = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}: {2}/{3} ({4}%)",
                                DateTime.Now, queueItem.Name, current, total, currentPercent);
                            
                            File.AppendAllText(progressFile, progressLine + Environment.NewLine);
                        }
                        catch (Exception ex)
                        {
                            LogManager.WriteLog("ERROR writing progress: " + ex.Message);
                        }
                        
                        progressTracker.LastPercent = currentPercent;
                    }
                }
            };

            // Load resume state if available
            var resumeState = TaskResumeManager.LoadResumeState(queueItem.Id);
            
            if (resumeState != null && resumeState.ContainsKey("processed_files"))
            {
                var processedFiles = resumeState["processed_files"] as List<object>;
                int filesCount = processedFiles != null ? processedFiles.Count : 0;
                LogManager.WriteLog("Resuming backup task " + queueItem.Name + " (ID: " + queueItem.Id + ") from " + filesCount + " previously processed files");
            }
            else
            {
                LogManager.WriteLog("Starting new backup task " + queueItem.Name + " (ID: " + queueItem.Id + ")");
            }

            string result = null;
            try
            {
                result = engine.RunBackup(queueItem.Source, queueItem.Dest, queueItem.Username, 
                    queueItem.FullBackup, queueItem.KeepLast, progressCallback, resumeState, queueItem.Id);
            }
            catch (Exception backupEx)
            {
                LogManager.WriteLog("Backup execution failed: " + backupEx.Message);
                throw;
            }

            // Log completion to yedek tasks log
            string yedekTasksLog = Path.Combine(
                LogManager.GetTaskLogDirectory("backup"),
                "tasks.txt");
            
            TimeSpan duration = DateTime.Now - startTime;
            
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(yedekTasksLog));
                
                if (string.IsNullOrEmpty(result))
                {
                    string taskEntry = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1} - NO CHANGES - Duration: {2:hh\\:mm\\:ss}",
                        DateTime.Now, queueItem.Name, duration);
                    System.IO.File.AppendAllText(yedekTasksLog, taskEntry + Environment.NewLine);
                    LogManager.WriteLog("Backup completed with no changes: " + queueItem.Name);
                }
                else
                {
                    string taskEntry = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1} - SUCCESS - {2} files - {3} - Duration: {4:hh\\:mm\\:ss}",
                        DateTime.Now, queueItem.Name, queueItem.FilesProcessed, result, duration);
                    System.IO.File.AppendAllText(yedekTasksLog, taskEntry + Environment.NewLine);
                    LogManager.WriteLog("Backup completed successfully: " + queueItem.Name + " - " + queueItem.FilesProcessed + " files");
                }
            }
            catch (Exception logEx)
            {
                LogManager.WriteLog("Error logging backup completion: " + logEx.Message);
            }
        }

        private void ExecuteQueuedCleaning(TaskQueueItem queueItem)
        {
            try
            {
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

        /// <summary>
        /// Load tasks from disk that were persisted before crash/shutdown
        /// This ensures resumed tasks aren't lost on service restart
        /// </summary>
        private void LoadPersistedQueue()
        {
            try
            {
                var queue = TaskQueue.LoadQueue();
                
                if (queue == null || queue.Count == 0)
                {
                    LogManager.WriteLog("No persisted queue found on startup");
                    return;
                }

                // Count tasks by status
                int pendingCount = queue.Count(t => t.Status == "pending");
                int runningCount = queue.Count(t => t.Status == "running");
                int failedCount = queue.Count(t => t.Status == "failed");

                if (pendingCount > 0 || runningCount > 0 || failedCount > 0)
                {
                    LogManager.WriteLog("Loaded persisted queue on startup:");
                    LogManager.WriteLog("  - Pending tasks: " + pendingCount);
                    LogManager.WriteLog("  - Running tasks: " + runningCount + " (will be retried)");
                    LogManager.WriteLog("  - Failed tasks: " + failedCount);

                    bool needsSave = false;

                    // Convert any "running" tasks back to "pending" so they can be retried
                    // This handles cases where service crashed mid-backup
                    foreach (var task in queue.Where(t => t.Status == "running"))
                    {
                        LogManager.WriteLog("Recovering crashed task: " + task.Name + " (ID: " + task.Id + ")");
                        
                        // Check if resume state exists for this task
                        var resumeState = TaskResumeManager.LoadResumeState(task.Id);
                        if (resumeState != null && resumeState.ContainsKey("processed_files"))
                        {
                            LogManager.WriteLog("Resume state found for task " + task.Id + " - will resume from saved checkpoint");
                        }
                        else
                        {
                            LogManager.WriteLog("No resume state found for task " + task.Id + " - will restart from beginning");
                        }
                        
                        task.Status = "pending";
                        task.StartedAt = null;
                        needsSave = true;
                    }

                    // Save the updated queue back to disk if any changes were made
                    if (needsSave)
                    {
                        TaskQueue.SaveQueue(queue);
                        LogManager.WriteLog("Queue recovered and persisted with " + queue.Count + " tasks");
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Error in LoadPersistedQueue: " + ex.Message);
            }
        }

        /// <summary>
        /// Helper class to track progress state across callback invocations
        /// </summary>
        private class ProgressTracker
        {
            public int LastPercent { get; set; } = 0;
        }
    }
}
