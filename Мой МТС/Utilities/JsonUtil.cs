using System;
using System.Globalization;
using System.Json;

namespace Мой_МТС.Utilities
{
    public static class JsonUtil
    {
        public static JsonValue ParseOrNull(string text)
        {
            if (System.String.IsNullOrWhiteSpace(text))
                return null;
            try
            {
                return JsonValue.Parse(text);
            }
            catch
            {
                return null;
            }
        }

        public static JsonValue Get(JsonValue root, params string[] path)
        {
            JsonValue current = root;
            for (int i = 0; i < path.Length; i++)
            {
                if (current == null || current.JsonType != JsonType.Object)
                    return null;

                JsonObject obj = (JsonObject)current;
                if (!obj.ContainsKey(path[i]))
                    return null;

                current = obj[path[i]];
            }
            return current;
        }

        public static JsonArray Array(JsonValue value)
        {
            if (value != null && value.JsonType == JsonType.Array)
                return (JsonArray)value;
            return new JsonArray();
        }

        public static string String(JsonValue value)
        {
            if (value == null)
                return null;
            try
            {
                if (value.JsonType == JsonType.String)
                    return value.ReadAs<string>();
                if (value.JsonType == JsonType.Boolean)
                    return value.ReadAs<bool>() ? "true" : "false";
                if (value.JsonType == JsonType.Number)
                    return value.ReadAs<double>().ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
            }
            string text = value.ToString();
            if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
                return text.Substring(1, text.Length - 2).Replace("\\\"", "\"");
            return text;
        }

        public static double? Double(JsonValue value)
        {
            if (value == null)
                return null;
            try
            {
                if (value.JsonType == JsonType.Number)
                    return value.ReadAs<double>();
                double result;
                if (System.Double.TryParse(String(value), NumberStyles.Any, CultureInfo.InvariantCulture, out result))
                    return result;
                if (System.Double.TryParse(String(value), NumberStyles.Any, CultureInfo.CurrentCulture, out result))
                    return result;
            }
            catch
            {
            }
            return null;
        }

        public static bool Bool(JsonValue value)
        {
            if (value == null)
                return false;
            try
            {
                if (value.JsonType == JsonType.Boolean)
                    return value.ReadAs<bool>();
                return String(value) == "true";
            }
            catch
            {
                return false;
            }
        }

        public static JsonPrimitive Primitive(string value)
        {
            return new JsonPrimitive(value == null ? System.String.Empty : value);
        }

        public static JsonPrimitive Primitive(int value)
        {
            return new JsonPrimitive(value);
        }

        public static JsonPrimitive Primitive(bool value)
        {
            return new JsonPrimitive(value);
        }
    }
}
