using System;
using System.Globalization;

namespace Kindling.Gameplay.Boot
{
    /// <summary>Reads "-name value" arguments from the process command line (dedicated server, desktop builds).</summary>
    public static class CommandLine
    {
        static string[] s_Args;

        static string[] Args => s_Args ??= SafeGetArgs();

        public static bool Has(string name) => Array.IndexOf(Args, name) >= 0;

        public static string Get(string name, string fallback)
        {
            int i = Array.IndexOf(Args, name);
            return i >= 0 && i + 1 < Args.Length ? Args[i + 1] : fallback;
        }

        public static int GetInt(string name, int fallback) =>
            int.TryParse(Get(name, null), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

        static string[] SafeGetArgs()
        {
            try
            {
                return Environment.GetCommandLineArgs() ?? Array.Empty<string>();
            }
            catch (Exception)
            {
                return Array.Empty<string>(); // some platforms don't expose a command line
            }
        }
    }
}
