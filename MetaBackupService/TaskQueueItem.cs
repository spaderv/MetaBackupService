using System;

namespace MetaBackupService
{
    public class TaskQueueItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        
        public string Source { get; set; }
        public string Dest { get; set; }
        public string Username { get; set; }
        public bool FullBackup { get; set; }
        public int KeepLast { get; set; }
        public int DeleteOlderThanDays { get; set; }
        
        public int FilesProcessed { get; set; }
        public int TotalFiles { get; set; }
        public string ErrorMessage { get; set; }
        public string LogFilePath { get; set; }

        // Resume system properties
        public int RetryCount { get; set; }
        public int MaxRetries { get; set; }
        public DateTime? LastRetryAt { get; set; }
        public string ResumeStatePath { get; set; }

        public TaskQueueItem()
        {
            Id = Guid.NewGuid().ToString();
            Status = "pending";
            CreatedAt = DateTime.Now;
            FilesProcessed = 0;
            TotalFiles = 0;
            RetryCount = 0;
            MaxRetries = 3;
            LastRetryAt = null;
        }

        public void Start()
        {
            Status = "running";
            StartedAt = DateTime.Now;
        }

        public void Complete()
        {
            Status = "completed";
            CompletedAt = DateTime.Now;
        }

        public void Fail(string error)
        {
            Status = "failed";
            ErrorMessage = error;
            CompletedAt = DateTime.Now;
        }

        public void Reset()
        {
            Status = "pending";
            StartedAt = null;
            CompletedAt = null;
            ErrorMessage = null;
            FilesProcessed = 0;
        }

        public bool CanRetry()
        {
            return RetryCount < MaxRetries && Status == "failed";
        }

        public void PrepareForRetry()
        {
            RetryCount++;
            LastRetryAt = DateTime.Now;
            Status = "pending";
            StartedAt = null;
            CompletedAt = null;
            ErrorMessage = null;
        }
    }
}
