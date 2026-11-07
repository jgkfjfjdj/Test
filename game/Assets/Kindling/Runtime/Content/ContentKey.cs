using System;

namespace Kindling.Content
{
    /// <summary>
    /// Stable, human-readable identifier of a content definition, e.g. <c>item.tool.iron_axe</c>.
    /// Keys never change once released (saves and the backend refer to them).
    /// Format: 2–6 dot-separated segments of <c>[a-z][a-z0-9_]*</c>, at most 64 characters,
    /// and the first segment must be a known <see cref="ContentKind"/> prefix.
    /// </summary>
    public readonly struct ContentKey : IEquatable<ContentKey>
    {
        public const int MaxLength = 64;
        public const int MinSegments = 2;
        public const int MaxSegments = 6;

        static readonly string[] s_KindPrefixes =
        {
            "item", "recipe", "piece", "loot", "season", "biome", "weather", "event", "cos", "map",
        };

        readonly string m_Value;

        ContentKey(string value, ContentKind kind)
        {
            m_Value = value;
            Kind = kind;
        }

        public string Value => m_Value ?? string.Empty;

        public ContentKind Kind { get; }

        public bool IsValid => m_Value != null;

        public static ContentKey Parse(string value)
        {
            if (!TryParse(value, out var key, out var error))
            {
                throw new FormatException($"Invalid content key '{value}': {error}");
            }

            return key;
        }

        public static bool TryParse(string value, out ContentKey key) => TryParse(value, out key, out _);

        public static bool TryParse(string value, out ContentKey key, out string error)
        {
            key = default;
            if (string.IsNullOrEmpty(value))
            {
                error = "key is empty";
                return false;
            }

            if (value.Length > MaxLength)
            {
                error = $"key is longer than {MaxLength} characters";
                return false;
            }

            int segments = 1;
            bool segmentStart = true;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '.')
                {
                    if (segmentStart)
                    {
                        error = "empty segment";
                        return false;
                    }

                    segments++;
                    segmentStart = true;
                    continue;
                }

                bool lower = c >= 'a' && c <= 'z';
                bool digitOrUnderscore = (c >= '0' && c <= '9') || c == '_';
                if (segmentStart ? !lower : !(lower || digitOrUnderscore))
                {
                    error = segmentStart
                        ? $"segment must start with a lowercase letter (position {i})"
                        : $"invalid character '{c}' at position {i}";
                    return false;
                }

                segmentStart = false;
            }

            if (segmentStart)
            {
                error = "key ends with '.'";
                return false;
            }

            if (segments < MinSegments || segments > MaxSegments)
            {
                error = $"key must have {MinSegments}-{MaxSegments} segments";
                return false;
            }

            int dot = value.IndexOf('.');
            string prefix = value.Substring(0, dot);
            int kindIndex = Array.IndexOf(s_KindPrefixes, prefix);
            if (kindIndex < 0)
            {
                error = $"unknown kind prefix '{prefix}'";
                return false;
            }

            key = new ContentKey(value, (ContentKind)kindIndex);
            error = null;
            return true;
        }

        public bool Equals(ContentKey other) => string.Equals(m_Value, other.m_Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ContentKey other && Equals(other);

        public override int GetHashCode() => m_Value == null ? 0 : StringComparer.Ordinal.GetHashCode(m_Value);

        public static bool operator ==(ContentKey a, ContentKey b) => a.Equals(b);

        public static bool operator !=(ContentKey a, ContentKey b) => !a.Equals(b);

        public override string ToString() => Value;
    }

    /// <summary>Kind of content, taken from the first key segment. Order matches the prefix table.</summary>
    public enum ContentKind
    {
        Item = 0,
        Recipe = 1,
        Piece = 2,
        LootTable = 3,
        Season = 4,
        Biome = 5,
        Weather = 6,
        Event = 7,
        Cosmetic = 8,
        Map = 9,
    }
}
