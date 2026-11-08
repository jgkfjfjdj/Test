using System;
using System.Collections.Generic;
using System.Globalization;

namespace Kindling.Tools
{
    /// <summary>Minimal "--name value" / "--flag" argument parser.</summary>
    public sealed class Args
    {
        readonly Dictionary<string, string> m_Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Args(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (!a.StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"Unexpected argument '{a}'.");
                }

                string name = a.Substring(2);
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    m_Values[name] = args[++i];
                }
                else
                {
                    m_Values[name] = "true";
                }
            }
        }

        public bool Has(string name) => m_Values.ContainsKey(name);

        public string String(string name, string fallback) => m_Values.TryGetValue(name, out string v) ? v : fallback;

        public int Int(string name, int fallback) => m_Values.TryGetValue(name, out string v) ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;

        public double Double(string name, double fallback) => m_Values.TryGetValue(name, out string v) ? double.Parse(v, CultureInfo.InvariantCulture) : fallback;

        public bool Flag(string name) => m_Values.TryGetValue(name, out string v) && v != "false";
    }
}
