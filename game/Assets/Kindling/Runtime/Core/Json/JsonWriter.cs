using System.Globalization;
using System.Text;

namespace Kindling.Core.Json
{
    /// <summary>Writes a <see cref="JsonValue"/> as JSON (2-space indented, "\n" newlines, stable member order).</summary>
    public static class JsonWriter
    {
        public static string Write(JsonValue value, bool indented = true)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value ?? JsonValue.Null, indented, 0);
            if (indented)
            {
                sb.Append('\n');
            }

            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, JsonValue v, bool indented, int depth)
        {
            switch (v.Kind)
            {
                case JsonKind.Null:
                    sb.Append("null");
                    break;
                case JsonKind.Bool:
                    sb.Append(v.AsBool() ? "true" : "false");
                    break;
                case JsonKind.Number:
                    double d = v.AsDouble();
                    sb.Append(d == System.Math.Floor(d) && System.Math.Abs(d) < 1e15
                        ? ((long)d).ToString(CultureInfo.InvariantCulture)
                        : d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case JsonKind.String:
                    WriteString(sb, v.AsString());
                    break;
                case JsonKind.Array:
                    if (v.Count == 0)
                    {
                        sb.Append("[]");
                        break;
                    }

                    sb.Append('[');
                    for (int i = 0; i < v.Count; i++)
                    {
                        if (i > 0)
                        {
                            sb.Append(',');
                        }

                        NewLine(sb, indented, depth + 1);
                        WriteValue(sb, v.Items[i], indented, depth + 1);
                    }

                    NewLine(sb, indented, depth);
                    sb.Append(']');
                    break;
                case JsonKind.Object:
                    if (v.Count == 0)
                    {
                        sb.Append("{}");
                        break;
                    }

                    sb.Append('{');
                    for (int i = 0; i < v.Count; i++)
                    {
                        if (i > 0)
                        {
                            sb.Append(',');
                        }

                        NewLine(sb, indented, depth + 1);
                        WriteString(sb, v.Members[i].Key);
                        sb.Append(indented ? ": " : ":");
                        WriteValue(sb, v.Members[i].Value, indented, depth + 1);
                    }

                    NewLine(sb, indented, depth);
                    sb.Append('}');
                    break;
            }
        }

        static void NewLine(StringBuilder sb, bool indented, int depth)
        {
            if (indented)
            {
                sb.Append('\n').Append(' ', depth * 2);
            }
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');
        }
    }
}
