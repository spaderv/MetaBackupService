using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MetaBackupService
{
    public static class TaskQueue
    {
        private static readonly object _lockObject = new object();

        public static string GetQueueFilePath()
        {
            string configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MetaBackup");
            
            if (!Directory.Exists(configDir))
                Directory.CreateDirectory(configDir);

            return Path.Combine(configDir, "task-queue.json");
        }

        public static List<TaskQueueItem> LoadQueue()
        {
            lock (_lockObject)
            {
                try
                {
                    string queuePath = GetQueueFilePath();

                    if (!File.Exists(queuePath))
                        return new List<TaskQueueItem>();

                    string json = File.ReadAllText(queuePath);
                    var queueData = SimpleJsonParser.Parse(json) as Dictionary<string, object>;

                    if (queueData == null || !queueData.ContainsKey("queue"))
                        return new List<TaskQueueItem>();

                    var queueList = queueData["queue"] as List<object>;
                    if (queueList == null)
                        return new List<TaskQueueItem>();

                    var items = new List<TaskQueueItem>();
                    foreach (var item in queueList)
                    {
                        var itemDict = item as Dictionary<string, object>;
                        if (itemDict != null)
                        {
                            items.Add(DictToQueueItem(itemDict));
                        }
                    }

                    return items;
                }
                catch (Exception ex)
                {
                    LogManager.WriteLog("Error loading queue: " + ex.Message);
                    return new List<TaskQueueItem>();
                }
            }
        }

        public static void SaveQueue(List<TaskQueueItem> queue)
        {
            lock (_lockObject)
            {
                try
                {
                    string queuePath = GetQueueFilePath();

                    var queueData = new Dictionary<string, object>
                    {
                        { "queue", queue.Select(QueueItemToDict).Cast<object>().ToList() },
                        { "saved_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
                    };

                    string json = SimpleJsonParser.Stringify(queueData);
                    string tempPath = queuePath + ".tmp";

                    File.WriteAllText(tempPath, json);

                    if (File.Exists(queuePath))
                        File.Delete(queuePath);

                    File.Move(tempPath, queuePath);
                }
                catch (Exception ex)
                {
                    LogManager.WriteLog("Error saving queue: " + ex.Message);
                }
            }
        }

        public static string EnqueueTask(TaskQueueItem item)
        {
            lock (_lockObject)
            {
                var queue = LoadQueue();
                queue.Add(item);
                SaveQueue(queue);
                LogManager.WriteLog("Task added to queue: " + item.Name + " (ID: " + item.Id + "), Total in queue: " + queue.Count);
                return item.Id;
            }
        }

        public static TaskQueueItem GetNextPendingTask()
        {
            lock (_lockObject)
            {
                var queue = LoadQueue();
                var pendingTask = queue.FirstOrDefault(t => t.Status == "pending");
                if (pendingTask != null)
                {
                    LogManager.WriteLog("Next pending task found: " + pendingTask.Name + " (ID: " + pendingTask.Id + ")");
                }
                return pendingTask;
            }
        }

        public static void UpdateTaskStatus(string taskId, string status)
        {
            lock (_lockObject)
            {
                var queue = LoadQueue();
                var task = queue.FirstOrDefault(t => t.Id == taskId);

                if (task != null)
                {
                    LogManager.WriteLog("Task status updated: " + task.Name + " -> " + status);
                    task.Status = status;
                    SaveQueue(queue);
                }
            }
        }

        public static void RemoveTask(string taskId)
        {
            lock (_lockObject)
            {
                var queue = LoadQueue();
                var taskToRemove = queue.FirstOrDefault(t => t.Id == taskId);
                if (taskToRemove != null)
                {
                    LogManager.WriteLog("Removing task from queue: " + taskToRemove.Name + " (ID: " + taskId + ")");
                    queue.RemoveAll(t => t.Id == taskId);
                    SaveQueue(queue);
                    LogManager.WriteLog("Task removed, remaining in queue: " + queue.Count);
                }
            }
        }

        public static Dictionary<string, int> GetQueueStats()
        {
            lock (_lockObject)
            {
                var queue = LoadQueue();
                return new Dictionary<string, int>
                {
                    { "total", queue.Count },
                    { "pending", queue.Count(t => t.Status == "pending") },
                    { "running", queue.Count(t => t.Status == "running") },
                    { "completed", queue.Count(t => t.Status == "completed") },
                    { "failed", queue.Count(t => t.Status == "failed") }
                };
            }
        }

        public static void CleanupOldCompletedTasks(int maxAgeHours = 24)
        {
            lock (_lockObject)
            {
                try
                {
                    var queue = LoadQueue();
                    var now = DateTime.Now;
                    int removed = 0;

                    // Remove tasks that completed or failed more than maxAgeHours ago
                    var tasksToRemove = queue.Where(t => 
                    {
                        if (t.Status == "completed" || t.Status == "failed")
                        {
                            DateTime relevantTime = t.CompletedAt ?? t.CreatedAt;
                            TimeSpan age = now - relevantTime;
                            return age.TotalHours >= maxAgeHours;
                        }
                        return false;
                    }).ToList();

                    foreach (var task in tasksToRemove)
                    {
                        queue.Remove(task);
                        removed++;
                        LogManager.WriteLog("Cleanup: Removed old " + task.Status + " task: " + task.Name + " (ID: " + task.Id + ")");
                    }

                    if (removed > 0)
                    {
                        SaveQueue(queue);
                        LogManager.WriteLog("Cleanup completed: Removed " + removed + " old tasks, " + queue.Count + " remaining");
                    }
                }
                catch (Exception ex)
                {
                    LogManager.WriteLog("Error in CleanupOldCompletedTasks: " + ex.Message);
                }
            }
        }

        private static TaskQueueItem DictToQueueItem(Dictionary<string, object> dict)
        {
            var item = new TaskQueueItem
            {
                Id = dict.ContainsKey("id") ? dict["id"].ToString() : Guid.NewGuid().ToString(),
                Name = dict.ContainsKey("name") ? dict["name"].ToString() : "",
                Type = dict.ContainsKey("type") ? dict["type"].ToString() : "backup",
                Status = dict.ContainsKey("status") ? dict["status"].ToString() : "pending",
                Source = dict.ContainsKey("source") ? dict["source"].ToString() : "",
                Dest = dict.ContainsKey("dest") ? dict["dest"].ToString() : "",
                Username = dict.ContainsKey("username") ? dict["username"].ToString() : "",
                FullBackup = dict.ContainsKey("full_backup") && (bool)dict["full_backup"],
                KeepLast = dict.ContainsKey("keep_last") ? Convert.ToInt32(dict["keep_last"]) : 3,
                DeleteOlderThanDays = dict.ContainsKey("delete_older_than_days") ? Convert.ToInt32(dict["delete_older_than_days"]) : 30,
                FilesProcessed = dict.ContainsKey("files_processed") ? Convert.ToInt32(dict["files_processed"]) : 0,
                TotalFiles = dict.ContainsKey("total_files") ? Convert.ToInt32(dict["total_files"]) : 0,
                ErrorMessage = dict.ContainsKey("error_message") ? dict["error_message"].ToString() : "",
                LogFilePath = dict.ContainsKey("log_file_path") ? dict["log_file_path"].ToString() : "",
                RetryCount = dict.ContainsKey("retry_count") ? Convert.ToInt32(dict["retry_count"]) : 0,
                MaxRetries = dict.ContainsKey("max_retries") ? Convert.ToInt32(dict["max_retries"]) : 3
            };

            if (dict.ContainsKey("created_at") && DateTime.TryParse(dict["created_at"].ToString(), out DateTime created))
                item.CreatedAt = created;

            if (dict.ContainsKey("started_at") && DateTime.TryParse(dict["started_at"].ToString(), out DateTime started))
                item.StartedAt = started;

            if (dict.ContainsKey("completed_at") && DateTime.TryParse(dict["completed_at"].ToString(), out DateTime completed))
                item.CompletedAt = completed;

            if (dict.ContainsKey("last_retry_at") && DateTime.TryParse(dict["last_retry_at"].ToString(), out DateTime lastRetry))
                item.LastRetryAt = lastRetry;

            return item;
        }

        private static Dictionary<string, object> QueueItemToDict(TaskQueueItem item)
        {
            return new Dictionary<string, object>
            {
                { "id", item.Id },
                { "name", item.Name },
                { "type", item.Type },
                { "status", item.Status },
                { "created_at", item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss") },
                { "started_at", item.StartedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "" },
                { "completed_at", item.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "" },
                { "source", item.Source },
                { "dest", item.Dest },
                { "username", item.Username },
                { "full_backup", item.FullBackup },
                { "keep_last", item.KeepLast },
                { "delete_older_than_days", item.DeleteOlderThanDays },
                { "files_processed", item.FilesProcessed },
                { "total_files", item.TotalFiles },
                { "error_message", item.ErrorMessage ?? "" },
                { "log_file_path", item.LogFilePath ?? "" },
                { "retry_count", item.RetryCount },
                { "max_retries", item.MaxRetries },
                { "last_retry_at", item.LastRetryAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "" }
            };
        }
    }
}
