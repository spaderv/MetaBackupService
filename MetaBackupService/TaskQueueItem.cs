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

        public TaskQueueItem()
        {
            Id = Guid.NewGuid().ToString();
            Status = "pending";
            CreatedAt = DateTime.Now;
            FilesProcessed = 0;
            TotalFiles = 0;
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
    }
}
