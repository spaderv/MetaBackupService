using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MetaBackupService
{
    /// <summary>
    /// Helper for parsing and converting cron expressions
    /// Converts task times and days to Windows Task Scheduler compatible format
    /// .NET Framework 4.0 compatible
    /// </summary>
    public static class CronExpressionHelper
    {
        private static readonly Dictionary<string, string> TurkishToDayOfWeek = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "pzt", "Monday" },
            { "sal", "Tuesday" },
            { "çar", "Wednesday" },
            { "per", "Thursday" },
            { "cum", "Friday" },
            { "cts", "Saturday" },
            { "paz", "Sunday" }
        };

        private static readonly Dictionary<string, int> DayOfWeekToNumber = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Sunday", 0 },
            { "Monday", 1 },
            { "Tuesday", 2 },
            { "Wednesday", 3 },
            { "Thursday", 4 },
            { "Friday", 5 },
            { "Saturday", 6 }
        };

        /// <summary>
        /// Parses "HH:MM" format to (hour, minute) tuple
        /// </summary>
        public static Tuple<int, int> ParseHHMM(string timeStr)
        {
            if (string.IsNullOrWhiteSpace(timeStr))
                throw new ArgumentException("Time string cannot be empty");

            string[] parts = timeStr.Trim().Split(':');
            if (parts.Length != 2)
                throw new ArgumentException($"Invalid time format: {timeStr}");

            int hh = int.Parse(parts[0]);
            int mm = int.Parse(parts[1]);

            if (!(0 <= hh && hh <= 23 && 0 <= mm && mm <= 59))
                throw new ArgumentException($"Invalid time: {timeStr}");

            return new Tuple<int, int>(hh, mm);
        }

        /// <summary>
        /// Converts times field (string or list) to list of (hh, mm) tuples
        /// </summary>
        public static List<Tuple<int, int>> NormalizeTimes(object timesAny)
        {
            var result = new List<Tuple<int, int>>();

            if (timesAny == null)
                return result;

            if (timesAny is string)
            {
                string[] parts = ((string)timesAny).Split(',');
                foreach (string part in parts)
                {
                    string trimmed = part.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        result.Add(ParseHHMM(trimmed));
                }
            }
            else if (timesAny is List<object>)
            {
                foreach (object item in (List<object>)timesAny)
                {
                    if (item != null)
                        result.Add(ParseHHMM(item.ToString()));
                }
            }
            else if (timesAny is List<string>)
            {
                foreach (string item in (List<string>)timesAny)
                {
                    if (!string.IsNullOrEmpty(item))
                        result.Add(ParseHHMM(item));
                }
            }

            return result;
        }

        /// <summary>
        /// Converts days field (Turkish/English/numeric) to list of DayOfWeek enums
        /// Returns empty list if "*" or null (meaning every day)
        /// </summary>
        public static List<DayOfWeek> NormalizeDaysOfWeek(object daysAny)
        {
            var result = new List<DayOfWeek>();

            if (daysAny == null)
                return result; // Empty = every day

            var items = new List<string>();

            if (daysAny is string)
            {
                string daysStr = (string)daysAny;
                if (daysStr == "*" || string.IsNullOrWhiteSpace(daysStr))
                    return result; // Empty = every day

                items.AddRange(daysStr.Split(',').Select(s => s.Trim().ToLower()));
            }
            else if (daysAny is List<object>)
            {
                items.AddRange(((List<object>)daysAny).Where(x => x != null).Select(x => x.ToString().ToLower()));
            }
            else if (daysAny is List<string>)
            {
                items.AddRange(((List<string>)daysAny).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.ToLower()));
            }

            foreach (string day in items)
            {
                if (day == "*")
                    continue; // Skip wildcards

                // Try Turkish abbreviation
                if (TurkishToDayOfWeek.ContainsKey(day))
                {
                    string englishDay = TurkishToDayOfWeek[day];
                    if (Enum.TryParse(englishDay, true, out DayOfWeek dow))
                        result.Add(dow);
                }
                // Try English day name
                else if (Enum.TryParse(day, true, out DayOfWeek dow))
                {
                    result.Add(dow);
                }
            }

            // Remove duplicates
            return result.Distinct().ToList();
        }

        /// <summary>
        /// Builds a Windows Task Scheduler trigger format
        /// Example: "WEEKLY", days: [Monday, Wednesday], time: 09:00
        /// </summary>
        public static Tuple<string, List<DayOfWeek>, string> BuildScheduleTrigger(
            List<Tuple<int, int>> times, object daysAny)
        {
            if (times == null || times.Count == 0)
                throw new ArgumentException("At least one time must be specified");

            var daysOfWeek = NormalizeDaysOfWeek(daysAny);

            // If no days specified, run daily
            string triggerType = daysOfWeek.Count == 0 ? "DAILY" : "WEEKLY";

            // Format first time for display (scheduler runs all times daily/weekly)
            string timeStr = string.Format("{0:D2}:{1:D2}", times[0].Item1, times[0].Item2);

            return new Tuple<string, List<DayOfWeek>, string>(triggerType, daysOfWeek, timeStr);
        }
    }
}
