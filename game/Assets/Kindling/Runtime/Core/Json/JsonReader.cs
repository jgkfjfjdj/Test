using System;
using System.Globalization;
using System.Text;

namespace Kindling.Core.Json
{
    /// <summary>
    /// Strict JSON parser (RFC 8259) that also accepts // and /* */ comments, for hand-written content
    /// files. Duplicate keys and trailing garbage are errors. Nesting is limited to 64 levels.
    /// </summary>
    public static class JsonReader
    {
        const int MaxDepth = 64;

        public static JsonValue Parse(string text)
        {
            var p = new Parser(text ?? throw new ArgumentNullException(nameof(text)));
            p.SkipTrivia();
            JsonValue value = p.ParseValue(0);
            p.SkipTrivia();
            if (!p.AtEnd)
            {
                throw p.Error("Unexpected text after the JSON value");
            }

            return value;
        }

        struct Parser
        {
            readonly string m_Text;
            int m_Pos;
            int m_Line;
            int m_LineStart;

            public Parser(string text)
            {
                m_Text = text;
                m_Pos = 0;
                m_Line = 1;
                m_LineStart = 0;
                if (text.Length > 0 && text[0] == '﻿')
                {
                    m_Pos = 1; // BOM
                }
            }

            public bool AtEnd => m_Pos >= m_Text.Length;

            int Column => m_Pos - m_LineStart + 1;

            public JsonException Error(string message) => new JsonException(message, m_Line, Column);

            public void SkipTrivia()
            {
                while (m_Pos < m_Text.Length)
                {
                    char c = m_Text[m_Pos];
                    if (c == '\n')
                    {
                        m_Pos++;
                        m_Line++;
                        m_LineStart = m_Pos;
                    }
                    else if (c == ' ' || c == '\t' || c == '\r')
                    {
                        m_Pos++;
                    }
                    else if (c == '/' && m_Pos + 1 < m_Text.Length && m_Text[m_Pos + 1] == '/')
                    {
                        while (m_Pos < m_Text.Length && m_Text[m_Pos] != '\n')
                        {
                            m_Pos++;
                        }
                    }
                    else if (c == '/' && m_Pos + 1 < m_Text.Length && m_Text[m_Pos + 1] == '*')
                    {
                        int end = m_Text.IndexOf("*/", m_Pos + 2, StringComparison.Ordinal);
                        if (end < 0)
                        {
                            throw Error("Unterminated comment");
                        }

                        for (int i = m_Pos; i < end; i++)
                        {
                            if (m_Text[i] == '\n')
                            {
                                m_Line++;
                                m_LineStart = i + 1;
                            }
                        }

                        m_Pos = end + 2;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            public JsonValue ParseValue(int depth)
            {
                if (depth > MaxDepth)
                {
                    throw Error("Nesting too deep");
                }

                if (AtEnd)
                {
                    throw Error("Unexpected end of text");
                }

                int line = m_Line;
                int column = Column;
                JsonValue value;
                char c = m_Text[m_Pos];
                switch (c)
                {
                    case '{':
                        value = ParseObject(depth);
                        break;
                    case '[':
                        value = ParseArray(depth);
                        break;
                    case '"':
                        value = JsonValue.String(ParseString());
                        break;
                    case 't':
                        Expect("true");
                        value = JsonValue.Bool(true);
                        break;
                    case 'f':
                        Expect("false");
                        value = JsonValue.Bool(false);
                        break;
                    case 'n':
                        Expect("null");
                        return JsonValue.Null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            value = JsonValue.Number(ParseNumber());
                            break;
                        }

                        throw Error($"Unexpected character '{c}'");
                }

                value.Line = line;
                value.Column = column;
                return value;
            }

            JsonValue ParseObject(int depth)
            {
                JsonValue obj = JsonValue.NewObject();
                m_Pos++; // {
                SkipTrivia();
                if (Peek() == '}')
                {
                    m_Pos++;
                    return obj;
                }

                while (true)
                {
                    SkipTrivia();
                    if (Peek() != '"')
                    {
                        throw Error("Expected a property name in double quotes");
                    }

                    int line = m_Line;
                    int column = Column;
                    string key = ParseString();
                    SkipTrivia();
                    if (Peek() != ':')
                    {
                        throw Error("Expected ':'");
                    }

                    m_Pos++;
                    SkipTrivia();
                    obj.AddMember(key, ParseValue(depth + 1), line, column);
                    SkipTrivia();
                    char c = Peek();
                    m_Pos++;
                    if (c == '}')
                    {
                        return obj;
                    }

                    if (c != ',')
                    {
                        m_Pos--;
                        throw Error("Expected ',' or '}'");
                    }
                }
            }

            JsonValue ParseArray(int depth)
            {
                JsonValue array = JsonValue.NewArray();
                m_Pos++; // [
                SkipTrivia();
                if (Peek() == ']')
                {
                    m_Pos++;
                    return array;
                }

                while (true)
                {
                    SkipTrivia();
                    array.Add(ParseValue(depth + 1));
                    SkipTrivia();
                    char c = Peek();
                    m_Pos++;
                    if (c == ']')
                    {
                        return array;
                    }

                    if (c != ',')
                    {
                        m_Pos--;
                        throw Error("Expected ',' or ']'");
                    }
                }
            }

            string ParseString()
            {
                m_Pos++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd)
                    {
                        throw Error("Unterminated string");
                    }

                    char c = m_Text[m_Pos++];
                    if (c == '"')
                    {
                        return sb.ToString();
                    }

                    if (c < 0x20)
                    {
                        m_Pos--;
                        throw Error("Control character in string");
                    }

                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }

                    if (AtEnd)
                    {
                        throw Error("Unterminated escape");
                    }

                    char e = m_Text[m_Pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (m_Pos + 4 > m_Text.Length || !int.TryParse(m_Text.Substring(m_Pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            {
                                throw Error("Invalid \\u escape");
                            }

                            sb.Append((char)code);
                            m_Pos += 4;
                            break;
                        default:
                            m_Pos--;
                            throw Error($"Invalid escape '\\{e}'");
                    }
                }
            }

            double ParseNumber()
            {
                int start = m_Pos;
                if (Peek() == '-')
                {
                    m_Pos++;
                }

                if (Peek() == '0')
                {
                    m_Pos++;
                }
                else if (IsDigit(Peek()))
                {
                    while (IsDigit(Peek()))
                    {
                        m_Pos++;
                    }
                }
                else
                {
                    throw Error("Invalid number");
                }

                if (Peek() == '.')
                {
                    m_Pos++;
                    if (!IsDigit(Peek()))
                    {
                        throw Error("Invalid number");
                    }

                    while (IsDigit(Peek()))
                    {
                        m_Pos++;
                    }
                }

                if (Peek() == 'e' || Peek() == 'E')
                {
                    m_Pos++;
                    if (Peek() == '+' || Peek() == '-')
                    {
                        m_Pos++;
                    }

                    if (!IsDigit(Peek()))
                    {
                        throw Error("Invalid number");
                    }

                    while (IsDigit(Peek()))
                    {
                        m_Pos++;
                    }
                }

                return double.Parse(m_Text.Substring(start, m_Pos - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(m_Text, m_Pos, word, 0, word.Length) != 0)
                {
                    throw Error($"Expected '{word}'");
                }

                m_Pos += word.Length;
            }

            char Peek() => m_Pos < m_Text.Length ? m_Text[m_Pos] : '\0';

            static bool IsDigit(char c) => c >= '0' && c <= '9';
        }
    }
}
