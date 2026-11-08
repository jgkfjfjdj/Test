using System;
using System.Collections.Generic;
using System.Globalization;

namespace Kindling.Core.Json
{
    public enum JsonKind : byte
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object,
    }

    /// <summary>A parsed JSON value (DOM). Objects keep member order. Accessors throw <see cref="JsonException"/> with the source position on type errors.</summary>
    public sealed class JsonValue
    {
        public static readonly JsonValue Null = new JsonValue(JsonKind.Null);

        readonly List<JsonValue> m_Items;
        readonly List<KeyValuePair<string, JsonValue>> m_Members;
        readonly Dictionary<string, JsonValue> m_Lookup;
        readonly string m_String;
        readonly double m_Number;
        readonly bool m_Bool;

        JsonValue(JsonKind kind)
        {
            Kind = kind;
            if (kind == JsonKind.Array)
            {
                m_Items = new List<JsonValue>();
            }
            else if (kind == JsonKind.Object)
            {
                m_Members = new List<KeyValuePair<string, JsonValue>>();
                m_Lookup = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            }
        }

        JsonValue(string value) : this(JsonKind.String) => m_String = value;

        JsonValue(double value) : this(JsonKind.Number) => m_Number = value;

        JsonValue(bool value) : this(JsonKind.Bool) => m_Bool = value;

        public JsonKind Kind { get; }

        /// <summary>1-based line of the value in the source text (0 when built in code).</summary>
        public int Line { get; internal set; }

        public int Column { get; internal set; }

        public bool IsNull => Kind == JsonKind.Null;

        public static JsonValue String(string value) => new JsonValue(value ?? string.Empty);

        public static JsonValue Number(double value) => new JsonValue(value);

        public static JsonValue Bool(bool value) => new JsonValue(value);

        public static JsonValue NewArray() => new JsonValue(JsonKind.Array);

        public static JsonValue NewObject() => new JsonValue(JsonKind.Object);

        public IReadOnlyList<JsonValue> Items => m_Items ?? throw Error("an array");

        public IReadOnlyList<KeyValuePair<string, JsonValue>> Members => m_Members ?? throw Error("an object");

        public int Count => Kind == JsonKind.Array ? m_Items.Count : Kind == JsonKind.Object ? m_Members.Count : 0;

        public string AsString() => Kind == JsonKind.String ? m_String : throw Error("a string");

        public double AsDouble() => Kind == JsonKind.Number ? m_Number : throw Error("a number");

        public float AsFloat() => (float)AsDouble();

        public int AsInt()
        {
            double d = AsDouble();
            if (d != Math.Floor(d) || d < int.MinValue || d > int.MaxValue)
            {
                throw Error("an integer");
            }

            return (int)d;
        }

        public bool AsBool() => Kind == JsonKind.Bool ? m_Bool : throw Error("true or false");

        public bool Has(string key) => m_Lookup != null && m_Lookup.ContainsKey(key);

        /// <summary>The member value, or null if this isn't an object or has no such member.</summary>
        public JsonValue Get(string key) => m_Lookup != null && m_Lookup.TryGetValue(key, out JsonValue v) ? v : null;

        public JsonValue Required(string key) => Get(key) ?? throw new JsonException($"Missing \"{key}\"", Line, Column);

        public string GetString(string key, string fallback = null) => Get(key) is JsonValue v && !v.IsNull ? v.AsString() : fallback;

        public double GetDouble(string key, double fallback = 0) => Get(key) is JsonValue v && !v.IsNull ? v.AsDouble() : fallback;

        public float GetFloat(string key, float fallback = 0) => (float)GetDouble(key, fallback);

        public int GetInt(string key, int fallback = 0) => Get(key) is JsonValue v && !v.IsNull ? v.AsInt() : fallback;

        public bool GetBool(string key, bool fallback = false) => Get(key) is JsonValue v && !v.IsNull ? v.AsBool() : fallback;

        public JsonValue Add(JsonValue item)
        {
            (m_Items ?? throw Error("an array")).Add(item ?? Null);
            return this;
        }

        public JsonValue Set(string key, JsonValue value)
        {
            if (m_Members == null)
            {
                throw Error("an object");
            }

            value ??= Null;
            if (m_Lookup.ContainsKey(key))
            {
                int i = m_Members.FindIndex(p => p.Key == key);
                m_Members[i] = new KeyValuePair<string, JsonValue>(key, value);
            }
            else
            {
                m_Members.Add(new KeyValuePair<string, JsonValue>(key, value));
            }

            m_Lookup[key] = value;
            return this;
        }

        public JsonValue Set(string key, string value) => Set(key, String(value));

        public JsonValue Set(string key, double value) => Set(key, Number(value));

        public JsonValue Set(string key, bool value) => Set(key, Bool(value));

        internal void AddMember(string key, JsonValue value, int line, int column)
        {
            if (m_Lookup.ContainsKey(key))
            {
                throw new JsonException($"Duplicate key \"{key}\"", line, column);
            }

            m_Members.Add(new KeyValuePair<string, JsonValue>(key, value));
            m_Lookup[key] = value;
        }

        public override string ToString() => Kind switch
        {
            JsonKind.String => m_String,
            JsonKind.Number => m_Number.ToString("R", CultureInfo.InvariantCulture),
            JsonKind.Bool => m_Bool ? "true" : "false",
            JsonKind.Null => "null",
            _ => JsonWriter.Write(this, indented: false),
        };

        JsonException Error(string expected) => new JsonException($"Expected {expected}, found {Kind}", Line, Column);
    }

    public sealed class JsonException : Exception
    {
        public JsonException(string message, int line, int column)
            : base(line > 0 ? $"{message} (line {line}, column {column})" : message)
        {
            Line = line;
            Column = column;
        }

        public int Line { get; }

        public int Column { get; }
    }
}
