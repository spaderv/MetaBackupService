using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MetaBackupService
{
    /// <summary>
    /// Simple JSON parser (imported reference for service)
    /// Provides basic JSON serialization/deserialization
    /// </summary>
    public static class SimpleJsonParser
    {
        public static Dictionary<string, object> Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, object>();

            json = json.Trim();
            if (!json.StartsWith("{") || !json.EndsWith("}"))
                return new Dictionary<string, object>();

            var result = new Dictionary<string, object>();
            string content = json.Substring(1, json.Length - 2).Trim();

            var pairs = SplitJsonPairs(content);
            foreach (var pair in pairs)
            {
                int colonIndex = pair.IndexOf(':');
                if (colonIndex < 0) continue;

                string key = ParseJsonString(pair.Substring(0, colonIndex).Trim());
                object value = ParseJsonValue(pair.Substring(colonIndex + 1).Trim());

                if (!string.IsNullOrEmpty(key))
                    result[key] = value;
            }

            return result;
        }

        public static string Stringify(Dictionary<string, object> obj)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{");

            bool first = true;
            foreach (var kvp in obj)
            {
                if (!first)
                    sb.Append(",");

                sb.Append("\"");
                sb.Append(EscapeJsonString(kvp.Key));
                sb.Append("\":");
                sb.Append(StringifyValue(kvp.Value));

                first = false;
            }

            sb.Append("}");
            return sb.ToString();
        }

        private static object ParseJsonValue(string value)
        {
            value = value.Trim();

            if (value.Equals("null", StringComparison.OrdinalIgnoreCase))
                return null;
            if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
                return false;

            if (value.StartsWith("\"") && value.EndsWith("\""))
                return ParseJsonString(value);

            double d;
            if (double.TryParse(value, System.Globalization.NumberStyles.Any, 
                System.Globalization.CultureInfo.InvariantCulture, out d))
                return d;

            if (value.StartsWith("[") && value.EndsWith("]"))
                return ParseJsonArray(value);

            if (value.StartsWith("{") && value.EndsWith("}"))
                return Parse(value);

            return null;
        }

        private static string ParseJsonString(string value)
        {
            value = value.Trim();
            if (!value.StartsWith("\"") || !value.EndsWith("\""))
                return value;

            value = value.Substring(1, value.Length - 2);

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length)
                {
                    switch (value[i + 1])
                    {
                        case '"': sb.Append('"'); i++; break;
                        case '\\': sb.Append('\\'); i++; break;
                        case '/': sb.Append('/'); i++; break;
                        case 'b': sb.Append('\b'); i++; break;
                        case 'f': sb.Append('\f'); i++; break;
                        case 'n': sb.Append('\n'); i++; break;
                        case 'r': sb.Append('\r'); i++; break;
                        case 't': sb.Append('\t'); i++; break;
                        default: sb.Append(value[i]); break;
                    }
                }
                else
                {
                    sb.Append(value[i]);
                }
            }

            return sb.ToString();
        }

        private static object ParseJsonArray(string value)
        {
            var result = new List<object>();
            value = value.Trim();

            if (!value.StartsWith("[") || !value.EndsWith("]"))
                return result;

            string content = value.Substring(1, value.Length - 2).Trim();
            if (string.IsNullOrEmpty(content))
                return result;

            var items = SplitJsonArray(content);
            foreach (var item in items)
            {
                string trimmedItem = item.Trim();
                result.Add(ParseJsonValue(trimmedItem));
            }

            return result;
        }

        private static List<string> SplitJsonPairs(string content)
        {
            var pairs = new List<string>();
            var current = new System.Text.StringBuilder();
            int depth = 0;
            bool inString = false;
            bool escapeNext = false;

            foreach (char c in content)
            {
                if (escapeNext)
                {
                    current.Append(c);
                    escapeNext = false;
                    continue;
                }

                if (c == '\\')
                {
                    current.Append(c);
                    escapeNext = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    current.Append(c);
                    continue;
                }

                if (inString)
                {
                    current.Append(c);
                    continue;
                }

                if (c == '{' || c == '[')
                {
                    depth++;
                    current.Append(c);
                }
                else if (c == '}' || c == ']')
                {
                    depth--;
                    current.Append(c);
                }
                else if (c == ',' && depth == 0)
                {
                    pairs.Add(current.ToString());
                    current = new System.Text.StringBuilder();
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
                pairs.Add(current.ToString());

            return pairs;
        }

        private static List<string> SplitJsonArray(string content)
        {
            return SplitJsonPairs(content);
        }

        private static string StringifyValue(object value)
        {
            if (value == null)
                return "null";

            if (value is bool)
                return ((bool)value) ? "true" : "false";

            if (value is string)
                return "\"" + EscapeJsonString((string)value) + "\"";

            if (value is double || value is int || value is float)
                return Convert.ToDouble(value).ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (value is List<string>)
            {
                var list = (List<string>)value;
                var sb = new System.Text.StringBuilder("[");
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append("\"" + EscapeJsonString(list[i]) + "\"");
                }
                sb.Append("]");
                return sb.ToString();
            }

            if (value is System.Collections.IList)
            {
                var list = value as System.Collections.IList;
                var sb = new System.Text.StringBuilder("[");
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(",");

                    var dict = list[i] as Dictionary<string, object>;
                    if (dict != null)
                        sb.Append(Stringify(dict));
                    else
                        sb.Append(StringifyValue(list[i]));
                }
                sb.Append("]");
                return sb.ToString();
            }

            if (value is Dictionary<string, object>)
                return Stringify((Dictionary<string, object>)value);

            return "\"" + EscapeJsonString(value.ToString()) + "\"";
        }

        private static string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var sb = new System.Text.StringBuilder();
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }

            return sb.ToString();
        }
    }
}
