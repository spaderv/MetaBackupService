using System;
using System.IO;
using System.Collections.Generic;

namespace MetaBackupService
{
    /// <summary>
    /// Centralized logging manager for MetaBackup
    /// Similar to Python logging module - structured, file-based logging
    /// </summary>
    public static class LogManager
    {
        private static readonly object _lockObject = new object();

        /// <summary>
        /// Gets the application's log directory
        /// </summary>
        public static string GetLogDirectory()
        {
            try
            {
                string programData = Environment.GetEnvironmentVariable("PROGRAMDATA");
                if (!string.IsNullOrEmpty(programData))
                {
                    string logDir = Path.Combine(programData, "MetaBackup", "Logs");
                    if (!Directory.Exists(logDir))
                    {
                        Directory.CreateDirectory(logDir);
                    }
                    return logDir;
                }

                string commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                string fallbackDir = Path.Combine(commonAppData, "MetaBackup", "Logs");
                if (!Directory.Exists(fallbackDir))
                {
                    Directory.CreateDirectory(fallbackDir);
                }
                return fallbackDir;
            }
            catch (Exception ex)
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "MetaBackup", "Logs");
                try
                {
                    if (!Directory.Exists(tempDir))
                    {
                        Directory.CreateDirectory(tempDir);
                    }
                }
                catch { }
                return tempDir;
            }
        }

        /// <summary>
        /// Gets task log directory based on task type
        /// "yedek" for backup, "temizleme" for cleaning
        /// </summary>
        public static string GetTaskLogDirectory(string taskType)
        {
            string taskTypeFolder = taskType.Equals("backup", StringComparison.OrdinalIgnoreCase) ? "yedek" : "temizleme";
            string logDir = Path.Combine(GetLogDirectory(), taskTypeFolder);
            
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }
            
            return logDir;
        }

        /// <summary>
        /// Create task-specific log file with timestamp
        /// Format: TaskName_YYYY-MM-DD_HH.MM.SS.txt
        /// </summary>
        public static string CreateTaskLogFile(string taskType, string taskName)
        {
            string taskLogDir = GetTaskLogDirectory(taskType);
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH.mm.ss");
            string sanitizedTaskName = SanitizeFileName(taskName);
            string fileName = string.Format("{0}_{1}.txt", sanitizedTaskName, timestamp);
            return Path.Combine(taskLogDir, fileName);
        }

        /// <summary>
        /// Writes a log message with timestamp to service.log
        /// Format: [YYYY-MM-DD HH:mm:ss] MESSAGE
        /// </summary>
        public static void WriteLog(string message, string logFileName = "service.log")
        {
            lock (_lockObject)
            {
                try
                {
                    string logDir = GetLogDirectory();
                    string logPath = Path.Combine(logDir, logFileName);

                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    string logMessage = string.Format("[{0}] {1}{2}", timestamp, message, Environment.NewLine);

                    if (!Directory.Exists(logDir))
                    {
                        Directory.CreateDirectory(logDir);
                    }

                    File.AppendAllText(logPath, logMessage);
                }
                catch (Exception ex)
                {
                    try
                    {
                        string fallbackPath = Path.Combine(Path.GetTempPath(), "MetaBackup_ServiceLog.txt");
                        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        string errorMsg = string.Format("[{0}] LOG ERROR: {1} | Message: {2}{3}",
                            timestamp, ex.Message, message, Environment.NewLine);
                        File.AppendAllText(fallbackPath, errorMsg);
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Writes task execution log to individual task file
        /// </summary>
        public static void WriteTaskLog(string taskLogFile, string message)
        {
            lock (_lockObject)
            {
                try
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    string logMessage = string.Format("[{0}] {1}{2}", timestamp, message, Environment.NewLine);

                    string taskLogDir = Path.GetDirectoryName(taskLogFile);
                    if (!Directory.Exists(taskLogDir))
                    {
                        Directory.CreateDirectory(taskLogDir);
                    }

                    File.AppendAllText(taskLogFile, logMessage);
                }
                catch { }
            }
        }

        /// <summary>
        /// Log multiple lines
        /// </summary>
        public static void WriteLogLines(params string[] messages)
        {
            foreach (string msg in messages)
            {
                if (!string.IsNullOrEmpty(msg))
                {
                    WriteLog(msg);
                }
            }
        }

        /// <summary>
        /// Log a separator line
        /// </summary>
        public static void WriteSeparator()
        {
            WriteLog("================================================================================");
        }

        /// <summary>
        /// Log section header
        /// </summary>
        public static void WriteSection(string title)
        {
            WriteSeparator();
            WriteLog("=== " + title + " ===");
            WriteSeparator();
        }

        /// <summary>
        /// Log indented message
        /// </summary>
        public static void WriteLogIndent(string message, int indentLevel = 1)
        {
            string indent = new string(' ', indentLevel * 2);
            WriteLog(indent + message);
        }

        /// <summary>
        /// Log a key-value pair
        /// </summary>
        public static void WriteLogKeyValue(string key, object value)
        {
            WriteLogIndent(string.Format("{0}: {1}", key, value));
        }

        /// <summary>
        /// Sanitizes a filename to be safe for file system
        /// </summary>
        private static string SanitizeFileName(string fileName)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }
            return fileName;
        }
    }
}
