using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace System.Json
{
    public enum JsonType
    {
        Default,
        Object,
        Array,
        String,
        Number,
        Boolean
    }

    public abstract class JsonValue
    {
        public abstract JsonType JsonType { get; }

        public static JsonValue Parse(string text)
        {
            Parser parser = new Parser(text);
            return parser.ParseRoot();
        }

        public virtual T ReadAs<T>()
        {
            object value = GetRawValue();
            if (value == null)
                return default(T);

            Type targetType = typeof(T);
            if (targetType == typeof(string))
                return (T)(object)Convert.ToString(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(bool))
                return (T)(object)Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(double))
                return (T)(object)Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(int))
                return (T)(object)Convert.ToInt32(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(long))
                return (T)(object)Convert.ToInt64(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(decimal))
                return (T)(object)Convert.ToDecimal(value, CultureInfo.InvariantCulture);

            return (T)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        internal virtual object GetRawValue()
        {
            return null;
        }

        internal abstract void WriteTo(StringBuilder builder);

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();
            WriteTo(builder);
            return builder.ToString();
        }

        internal static void WriteJsonString(StringBuilder builder, string value)
        {
            if (value == null)
            {
                builder.Append("null");
                return;
            }

            builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                switch (ch)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (ch < 32)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(ch);
                        }
                        break;
                }
            }
            builder.Append('"');
        }

        private sealed class Parser
        {
            private readonly string _text;
            private int _index;

            public Parser(string text)
            {
                _text = text == null ? String.Empty : text;
                _index = 0;
            }

            public JsonValue ParseRoot()
            {
                SkipWhiteSpace();
                JsonValue value = ParseValue();
                SkipWhiteSpace();
                return value;
            }

            private JsonValue ParseValue()
            {
                SkipWhiteSpace();
                if (_index >= _text.Length)
                    throw new FormatException("Unexpected end of JSON text.");

                char ch = _text[_index];
                if (ch == '{')
                    return ParseObject();
                if (ch == '[')
                    return ParseArray();
                if (ch == '"')
                    return new JsonPrimitive(ParseString());
                if (ch == '-' || (ch >= '0' && ch <= '9'))
                    return new JsonPrimitive(ParseNumber());
                if (MatchLiteral("true"))
                    return new JsonPrimitive(true);
                if (MatchLiteral("false"))
                    return new JsonPrimitive(false);
                if (MatchLiteral("null"))
                    return null;

                throw new FormatException("Invalid JSON value at position " + _index.ToString(CultureInfo.InvariantCulture) + ".");
            }

            private JsonObject ParseObject()
            {
                JsonObject obj = new JsonObject();
                Expect('{');
                SkipWhiteSpace();
                if (TryConsume('}'))
                    return obj;

                while (true)
                {
                    SkipWhiteSpace();
                    if (_index >= _text.Length || _text[_index] != '"')
                        throw new FormatException("Expected object key at position " + _index.ToString(CultureInfo.InvariantCulture) + ".");

                    string key = ParseString();
                    SkipWhiteSpace();
                    Expect(':');
                    obj[key] = ParseValue();
                    SkipWhiteSpace();

                    if (TryConsume('}'))
                        break;
                    Expect(',');
                }
                return obj;
            }

            private JsonArray ParseArray()
            {
                JsonArray array = new JsonArray();
                Expect('[');
                SkipWhiteSpace();
                if (TryConsume(']'))
                    return array;

                while (true)
                {
                    array.Add(ParseValue());
                    SkipWhiteSpace();
                    if (TryConsume(']'))
                        break;
                    Expect(',');
                }
                return array;
            }

            private string ParseString()
            {
                Expect('"');
                StringBuilder builder = new StringBuilder();
                while (_index < _text.Length)
                {
                    char ch = _text[_index++];
                    if (ch == '"')
                        return builder.ToString();

                    if (ch == '\\')
                    {
                        if (_index >= _text.Length)
                            throw new FormatException("Invalid escape sequence.");

                        char esc = _text[_index++];
                        switch (esc)
                        {
                            case '"': builder.Append('"'); break;
                            case '\\': builder.Append('\\'); break;
                            case '/': builder.Append('/'); break;
                            case 'b': builder.Append('\b'); break;
                            case 'f': builder.Append('\f'); break;
                            case 'n': builder.Append('\n'); break;
                            case 'r': builder.Append('\r'); break;
                            case 't': builder.Append('\t'); break;
                            case 'u':
                                builder.Append(ParseUnicodeEscape());
                                break;
                            default:
                                throw new FormatException("Invalid escape sequence \\" + esc + ".");
                        }
                    }
                    else
                    {
                        builder.Append(ch);
                    }
                }
                throw new FormatException("Unterminated JSON string.");
            }

            private char ParseUnicodeEscape()
            {
                if (_index + 4 > _text.Length)
                    throw new FormatException("Invalid unicode escape sequence.");

                string hex = _text.Substring(_index, 4);
                _index += 4;
                int code = Int32.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return (char)code;
            }

            private double ParseNumber()
            {
                int start = _index;
                if (_text[_index] == '-')
                    _index++;

                while (_index < _text.Length && Char.IsDigit(_text[_index]))
                    _index++;

                if (_index < _text.Length && _text[_index] == '.')
                {
                    _index++;
                    while (_index < _text.Length && Char.IsDigit(_text[_index]))
                        _index++;
                }

                if (_index < _text.Length && (_text[_index] == 'e' || _text[_index] == 'E'))
                {
                    _index++;
                    if (_index < _text.Length && (_text[_index] == '+' || _text[_index] == '-'))
                        _index++;
                    while (_index < _text.Length && Char.IsDigit(_text[_index]))
                        _index++;
                }

                string number = _text.Substring(start, _index - start);
                return Double.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            private bool MatchLiteral(string literal)
            {
                if (_index + literal.Length > _text.Length)
                    return false;

                for (int i = 0; i < literal.Length; i++)
                {
                    if (_text[_index + i] != literal[i])
                        return false;
                }

                _index += literal.Length;
                return true;
            }

            private void SkipWhiteSpace()
            {
                while (_index < _text.Length && Char.IsWhiteSpace(_text[_index]))
                    _index++;
            }

            private void Expect(char expected)
            {
                SkipWhiteSpace();
                if (_index >= _text.Length || _text[_index] != expected)
                    throw new FormatException("Expected '" + expected + "' at position " + _index.ToString(CultureInfo.InvariantCulture) + ".");
                _index++;
            }

            private bool TryConsume(char expected)
            {
                SkipWhiteSpace();
                if (_index < _text.Length && _text[_index] == expected)
                {
                    _index++;
                    return true;
                }
                return false;
            }
        }
    }

    public sealed class JsonPrimitive : JsonValue
    {
        private readonly object _value;
        private readonly JsonType _type;

        public JsonPrimitive(string value)
        {
            _value = value;
            _type = JsonType.String;
        }

        public JsonPrimitive(int value)
        {
            _value = value;
            _type = JsonType.Number;
        }

        public JsonPrimitive(long value)
        {
            _value = value;
            _type = JsonType.Number;
        }

        public JsonPrimitive(double value)
        {
            _value = value;
            _type = JsonType.Number;
        }

        public JsonPrimitive(bool value)
        {
            _value = value;
            _type = JsonType.Boolean;
        }

        public override JsonType JsonType
        {
            get { return _type; }
        }

        internal override object GetRawValue()
        {
            return _value;
        }

        internal override void WriteTo(StringBuilder builder)
        {
            if (_type == JsonType.String)
            {
                WriteJsonString(builder, _value as string);
                return;
            }
            if (_type == JsonType.Boolean)
            {
                builder.Append(((bool)_value) ? "true" : "false");
                return;
            }
            if (_value == null)
            {
                builder.Append("null");
                return;
            }
            builder.Append(Convert.ToString(_value, CultureInfo.InvariantCulture));
        }
    }

    public sealed class JsonObject : JsonValue
    {
        private readonly Dictionary<string, JsonValue> _values = new Dictionary<string, JsonValue>();

        public override JsonType JsonType
        {
            get { return JsonType.Object; }
        }

        public JsonValue this[string key]
        {
            get
            {
                JsonValue value;
                if (key != null && _values.TryGetValue(key, out value))
                    return value;
                return null;
            }
            set
            {
                if (key == null)
                    return;
                _values[key] = value;
            }
        }

        public int Count
        {
            get { return _values.Count; }
        }

        public bool ContainsKey(string key)
        {
            return key != null && _values.ContainsKey(key);
        }

        internal override void WriteTo(StringBuilder builder)
        {
            builder.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, JsonValue> item in _values)
            {
                if (!first)
                    builder.Append(',');
                first = false;
                WriteJsonString(builder, item.Key);
                builder.Append(':');
                if (item.Value == null)
                    builder.Append("null");
                else
                    item.Value.WriteTo(builder);
            }
            builder.Append('}');
        }
    }

    public sealed class JsonArray : JsonValue
    {
        private readonly List<JsonValue> _values = new List<JsonValue>();

        public override JsonType JsonType
        {
            get { return JsonType.Array; }
        }

        public JsonValue this[int index]
        {
            get { return _values[index]; }
            set { _values[index] = value; }
        }

        public int Count
        {
            get { return _values.Count; }
        }

        public void Add(JsonValue value)
        {
            _values.Add(value);
        }

        internal override void WriteTo(StringBuilder builder)
        {
            builder.Append('[');
            for (int i = 0; i < _values.Count; i++)
            {
                if (i > 0)
                    builder.Append(',');
                if (_values[i] == null)
                    builder.Append("null");
                else
                    _values[i].WriteTo(builder);
            }
            builder.Append(']');
        }
    }
}
