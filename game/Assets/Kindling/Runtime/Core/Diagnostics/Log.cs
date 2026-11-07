using System;

namespace Kindling.Core.Diagnostics
{
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
    }

    /// <summary>Destination for log output (Unity console, server log file, test capture...).</summary>
    public interface ILogSink
    {
        void Write(LogLevel level, string category, string message, Exception exception);
    }

    /// <summary>
    /// Engine-independent logging facade. Pure C# code (Core, Content, Simulation) logs through this,
    /// so it runs unchanged in Unity, the dedicated server and the .NET test project.
    /// With no sink installed, messages are discarded.
    /// </summary>
    public static class Log
    {
        static ILogSink s_Sink;

        /// <summary>Messages below this level are dropped before reaching the sink.</summary>
        public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

        public static ILogSink Sink => s_Sink;

        public static void SetSink(ILogSink sink)
        {
            s_Sink = sink;
        }

        public static bool IsEnabled(LogLevel level)
        {
            return s_Sink != null && level >= MinimumLevel;
        }

        public static void Debug(string category, string message) => Write(LogLevel.Debug, category, message, null);

        public static void Info(string category, string message) => Write(LogLevel.Info, category, message, null);

        public static void Warning(string category, string message) => Write(LogLevel.Warning, category, message, null);

        public static void Error(string category, string message, Exception exception = null) =>
            Write(LogLevel.Error, category, message, exception);

        static void Write(LogLevel level, string category, string message, Exception exception)
        {
            var sink = s_Sink;
            if (sink == null || level < MinimumLevel)
            {
                return;
            }

            sink.Write(level, category ?? string.Empty, message ?? string.Empty, exception);
        }
    }
}
