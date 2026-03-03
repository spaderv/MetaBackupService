using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace MetaBackupService
{
    /// <summary>
    /// Telegram notification service for MetaBackup
    /// Sends messages to Telegram bot with configurable filtering
    /// .NET Framework 4.0 compatible
    /// </summary>
    public static class TelegramNotificationService
    {
        public enum MessageFilter
        {
            All,           // Send all notifications
            SuccessOnly,   // Send only success messages
            FailureOnly    // Send only failure messages (ERROR, WARNING)
        }

        private static readonly object _lockObject = new object();
        private static Dictionary<string, object> _cachedConfig = null;
        private static DateTime _lastConfigCheck = DateTime.MinValue;

        /// <summary>
        /// Load Telegram configuration from service config file
        /// </summary>
        private static Dictionary<string, object> LoadTelegramConfig()
        {
            lock (_lockObject)
            {
                // Cache config for 5 seconds
                if (_cachedConfig != null && (DateTime.Now - _lastConfigCheck).TotalSeconds < 5)
                {
                    return _cachedConfig;
                }

                try
                {
                    string configPath = GetConfigPath();
                    if (!File.Exists(configPath))
                    {
                        return null;
                    }

                    string json = File.ReadAllText(configPath);
                    var config = SimpleJsonParser.Parse(json) as Dictionary<string, object>;
                    
                    if (config == null || !config.ContainsKey("telegram"))
                    {
                        return null;
                    }

                    _cachedConfig = config["telegram"] as Dictionary<string, object>;
                    _lastConfigCheck = DateTime.Now;
                    return _cachedConfig;
                }
                catch (Exception ex)
                {
                    LogManager.WriteLog("Error loading Telegram config: " + ex.Message);
                    return null;
                }
            }
        }

        /// <summary>
        /// Get configuration file path
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
        /// Send a success message to Telegram
        /// </summary>
        public static void SendSuccess(string title, string message, string additionalInfo = null)
        {
            try
            {
                var config = LoadTelegramConfig();
                if (config == null || !IsConfigured(config))
                {
                    return;
                }

                MessageFilter filter = GetMessageFilter(config);
                
                // Skip if filter is set to FailureOnly
                if (filter == MessageFilter.FailureOnly)
                {
                    return;
                }

                var chatIds = GetChatIds(config);
                var botToken = GetBotToken(config);

                if (string.IsNullOrEmpty(botToken) || chatIds == null || chatIds.Count == 0)
                {
                    return;
                }

                string fullMessage = FormatSuccessMessage(title, message, additionalInfo);
                SendToTelegram(botToken, chatIds, fullMessage);
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Error sending Telegram success notification: " + ex.Message);
            }
        }

        /// <summary>
        /// Send a failure message to Telegram
        /// </summary>
        public static void SendFailure(string title, string message, string errorDetails = null)
        {
            try
            {
                var config = LoadTelegramConfig();
                if (config == null || !IsConfigured(config))
                {
                    return;
                }

                MessageFilter filter = GetMessageFilter(config);
                
                // Skip if filter is set to SuccessOnly
                if (filter == MessageFilter.SuccessOnly)
                {
                    return;
                }

                var chatIds = GetChatIds(config);
                var botToken = GetBotToken(config);

                if (string.IsNullOrEmpty(botToken) || chatIds == null || chatIds.Count == 0)
                {
                    return;
                }

                string fullMessage = FormatFailureMessage(title, message, errorDetails);
                SendToTelegram(botToken, chatIds, fullMessage);
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Error sending Telegram failure notification: " + ex.Message);
            }
        }

        /// <summary>
        /// Send a warning message to Telegram
        /// </summary>
        public static void SendWarning(string title, string message)
        {
            try
            {
                var config = LoadTelegramConfig();
                if (config == null || !IsConfigured(config))
                {
                    return;
                }

                MessageFilter filter = GetMessageFilter(config);
                
                // Skip if filter is set to SuccessOnly
                if (filter == MessageFilter.SuccessOnly)
                {
                    return;
                }

                var chatIds = GetChatIds(config);
                var botToken = GetBotToken(config);

                if (string.IsNullOrEmpty(botToken) || chatIds == null || chatIds.Count == 0)
                {
                    return;
                }

                string fullMessage = FormatWarningMessage(title, message);
                SendToTelegram(botToken, chatIds, fullMessage);
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Error sending Telegram warning notification: " + ex.Message);
            }
        }

        /// <summary>
        /// Send custom message to Telegram
        /// </summary>
        public static void SendMessage(string message)
        {
            try
            {
                var config = LoadTelegramConfig();
                if (config == null || !IsConfigured(config))
                {
                    return;
                }

                var chatIds = GetChatIds(config);
                var botToken = GetBotToken(config);

                if (string.IsNullOrEmpty(botToken) || chatIds == null || chatIds.Count == 0)
                {
                    return;
                }

                SendToTelegram(botToken, chatIds, message);
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Error sending Telegram message: " + ex.Message);
            }
        }

        /// <summary>
        /// Check if Telegram is configured
        /// </summary>
        public static bool IsConfigured()
        {
            try
            {
                var config = LoadTelegramConfig();
                return IsConfigured(config);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Internal method to check if config has required values
        /// </summary>
        private static bool IsConfigured(Dictionary<string, object> config)
        {
            if (config == null)
                return false;

            var botToken = GetBotToken(config);
            var chatIds = GetChatIds(config);

            return !string.IsNullOrEmpty(botToken) && (chatIds != null && chatIds.Count > 0);
        }

        /// <summary>
        /// Get bot token from config (decrypt if needed)
        /// </summary>
        private static string GetBotToken(Dictionary<string, object> config)
        {
            if (config == null)
                return null;

            // Try encrypted token first
            if (config.ContainsKey("bot_token_enc"))
            {
                string encrypted = config["bot_token_enc"].ToString();
                if (!string.IsNullOrEmpty(encrypted))
                {
                    try
                    {
                        string decrypted = CryptographyService.UnprotectString(encrypted);
                        if (!string.IsNullOrEmpty(decrypted))
                            return decrypted;
                    }
                    catch { }
                }
            }

            // Fallback to plain token if encrypted not available
            if (config.ContainsKey("bot_token"))
            {
                return config["bot_token"].ToString();
            }

            return null;
        }

        /// <summary>
        /// Get chat IDs from config
        /// </summary>
        private static List<string> GetChatIds(Dictionary<string, object> config)
        {
            if (config == null || !config.ContainsKey("chat_ids"))
                return null;

            var chatIds = new List<string>();
            var chatIdObj = config["chat_ids"];

            // Handle if it's a list
            if (chatIdObj is List<object>)
            {
                foreach (var id in (List<object>)chatIdObj)
                {
                    if (id != null)
                        chatIds.Add(id.ToString());
                }
            }
            // Handle if it's a string (comma-separated)
            else if (chatIdObj is string)
            {
                string chatIdStr = chatIdObj.ToString();
                if (!string.IsNullOrEmpty(chatIdStr))
                {
                    foreach (var id in chatIdStr.Split(','))
                    {
                        string trimmed = id.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                            chatIds.Add(trimmed);
                    }
                }
            }

            return chatIds.Count > 0 ? chatIds : null;
        }

        /// <summary>
        /// Get message filter setting from config
        /// </summary>
        private static MessageFilter GetMessageFilter(Dictionary<string, object> config)
        {
            if (config == null || !config.ContainsKey("message_filter"))
                return MessageFilter.FailureOnly;

            string filterStr = config["message_filter"].ToString().ToLower();

            switch (filterStr)
            {
                case "all":
                    return MessageFilter.All;
                case "success_only":
                    return MessageFilter.SuccessOnly;
                case "failure_only":
                default:
                    return MessageFilter.FailureOnly;
            }
        }

        /// <summary>
        /// Format success message
        /// </summary>
        private static string FormatSuccessMessage(string title, string message, string additionalInfo)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[SUCCESS]");
            sb.AppendLine(title);
            sb.AppendLine("================================");
            sb.AppendLine(message);
            
            if (!string.IsNullOrEmpty(additionalInfo))
            {
                sb.AppendLine();
                sb.AppendLine(additionalInfo);
            }
            
            sb.AppendLine("================================");
            sb.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            return sb.ToString();
        }

        /// <summary>
        /// Format failure message
        /// </summary>
        private static string FormatFailureMessage(string title, string message, string errorDetails)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[FAILURE]");
            sb.AppendLine(title);
            sb.AppendLine("================================");
            sb.AppendLine(message);
            
            if (!string.IsNullOrEmpty(errorDetails))
            {
                sb.AppendLine();
                sb.AppendLine("Error Details:");
                sb.AppendLine(errorDetails);
            }
            
            sb.AppendLine("================================");
            sb.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            return sb.ToString();
        }

        /// <summary>
        /// Format warning message
        /// </summary>
        private static string FormatWarningMessage(string title, string message)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[WARNING]");
            sb.AppendLine(title);
            sb.AppendLine("================================");
            sb.AppendLine(message);
            sb.AppendLine("================================");
            sb.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            return sb.ToString();
        }

        /// <summary>
        /// Send message to Telegram
        /// </summary>
        private static void SendToTelegram(string botToken, List<string> chatIds, string message)
        {
            LogManager.WriteLog("Starting SendToTelegram: " + chatIds.Count + " recipients");
            
            foreach (string chatId in chatIds)
            {
                try
                {
                    LogManager.WriteLog("Sending to chat ID: " + chatId);
                    SendToTelegramChat(botToken, chatId, message);
                    LogManager.WriteLog("Successfully sent to chat ID: " + chatId);
                }
                catch (Exception ex)
                {
                    LogManager.WriteLog("Error sending to chat " + chatId + ": " + ex.Message);
                    LogManager.WriteLog("Exception Type: " + ex.GetType().Name);
                    if (ex.InnerException != null)
                    {
                        LogManager.WriteLog("Inner Exception: " + ex.InnerException.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Send message to specific Telegram chat
        /// </summary>
        private static void SendToTelegramChat(string botToken, string chatId, string message)
        {
            string url = "https://api.telegram.org/bot" + botToken + "/sendMessage";

            try
            {
                // Enable TLS 1.2 for .NET Framework 4.0 (numeric value: 3072)
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                
                LogManager.WriteLog("Attempting to send Telegram message to chat " + chatId);
                LogManager.WriteLog("URL: " + url);
                LogManager.WriteLog("Message length: " + message.Length);

                using (WebClient client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    
                    // Create JSON payload for Telegram API
                    string jsonPayload = string.Format(
                        "{{\"chat_id\": \"{0}\", \"text\": {1}, \"parse_mode\": \"HTML\"}}",
                        chatId,
                        EscapeJsonString(message)
                    );

                    LogManager.WriteLog("JSON Payload prepared, length: " + jsonPayload.Length);
                    
                    // Set content type to JSON
                    client.Headers.Add("Content-Type", "application/json");

                    byte[] data = Encoding.UTF8.GetBytes(jsonPayload);
                    byte[] response = client.UploadData(url, "POST", data);
                    string responseString = Encoding.UTF8.GetString(response);

                    LogManager.WriteLog("Telegram response: " + responseString);
                    LogManager.WriteLog("Telegram message sent successfully to chat " + chatId);
                }
            }
            catch (WebException ex)
            {
                LogManager.WriteLog("Telegram WebException: " + ex.Message);
                LogManager.WriteLog("WebException Status: " + ex.Status);
                
                if (ex.Response != null)
                {
                    try
                    {
                        using (StreamReader reader = new StreamReader(ex.Response.GetResponseStream()))
                        {
                            string responseText = reader.ReadToEnd();
                            LogManager.WriteLog("Telegram API Response: " + responseText);
                        }
                    }
                    catch (Exception readEx)
                    {
                        LogManager.WriteLog("Error reading response: " + readEx.Message);
                    }
                }
                throw;
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Unexpected error in SendToTelegramChat: " + ex.Message);
                LogManager.WriteLog("Exception Type: " + ex.GetType().Name);
                LogManager.WriteLog("Stack Trace: " + ex.StackTrace);
                throw;
            }
        }

        /// <summary>
        /// Escape string for JSON format
        /// </summary>
        private static string EscapeJsonString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "\"\"";

            // Use StringBuilder for efficiency
            var sb = new StringBuilder();
            sb.Append("\"");

            foreach (char c in input)
            {
                switch (c)
                {
                    case '\"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        // For other control characters
                        if (char.IsControl(c))
                        {
                            sb.Append(string.Format("\\u{0:X4}", (int)c));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }

            sb.Append("\"");
            return sb.ToString();
        }

        /// <summary>
        /// Test Telegram configuration
        /// </summary>
        public static Tuple<bool, string> TestConfiguration()
        {
            try
            {
                LogManager.WriteLog("=== TELEGRAM TEST CONFIGURATION START ===");
                
                var config = LoadTelegramConfig();
                LogManager.WriteLog("Config loaded: " + (config != null ? "YES" : "NO"));
                
                if (config == null)
                {
                    LogManager.WriteLog("Telegram configuration not found");
                    return new Tuple<bool, string>(false, "Telegram configuration not found");
                }

                var botToken = GetBotToken(config);
                var chatIds = GetChatIds(config);

                LogManager.WriteLog("Bot token present: " + (!string.IsNullOrEmpty(botToken) ? "YES" : "NO"));
                LogManager.WriteLog("Chat IDs count: " + (chatIds != null ? chatIds.Count : 0));

                if (string.IsNullOrEmpty(botToken))
                {
                    LogManager.WriteLog("Bot token is empty or invalid");
                    return new Tuple<bool, string>(false, "Bot token is empty or invalid");
                }

                if (chatIds == null || chatIds.Count == 0)
                {
                    LogManager.WriteLog("No chat IDs configured");
                    return new Tuple<bool, string>(false, "No chat IDs configured");
                }

                if (!botToken.Contains(":"))
                {
                    LogManager.WriteLog("Bot token format is invalid (should contain ':')");
                    return new Tuple<bool, string>(false, "Bot token format is invalid (should contain ':')");
                }

                // Try to send a test message
                try
                {
                    LogManager.WriteLog("Attempting to send test message...");
                    string testMessage = "[TEST] Telegram Test Message from MetaBackup\n\nConfiguration is working correctly!\n\nTime: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    SendToTelegram(botToken, chatIds, testMessage);
                    
                    LogManager.WriteLog("=== TELEGRAM TEST CONFIGURATION SUCCESS ===");
                    return new Tuple<bool, string>(true, "Test message sent successfully");
                }
                catch (Exception sendEx)
                {
                    LogManager.WriteLog("Failed to send test message: " + sendEx.Message);
                    LogManager.WriteLog("Exception Type: " + sendEx.GetType().Name);
                    if (sendEx.InnerException != null)
                    {
                        LogManager.WriteLog("Inner Exception: " + sendEx.InnerException.Message);
                    }
                    LogManager.WriteLog("=== TELEGRAM TEST CONFIGURATION FAILED ===");
                    return new Tuple<bool, string>(false, "Failed to send test message: " + sendEx.Message);
                }
            }
            catch (Exception ex)
            {
                LogManager.WriteLog("Error testing Telegram configuration: " + ex.Message);
                LogManager.WriteLog("=== TELEGRAM TEST CONFIGURATION ERROR ===");
                return new Tuple<bool, string>(false, "Error testing configuration: " + ex.Message);
            }
        }
    }
}
