using System;
using Kindling.Core.Diagnostics;

namespace Kindling.Tools
{
    /// <summary>Writes log lines to stdout with a UTC timestamp.</summary>
    public sealed class ConsoleLogSink : ILogSink
    {
        readonly object m_Lock = new object();

        public void Write(LogLevel level, string category, string message, Exception exception)
        {
            lock (m_Lock)
            {
                Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss.fff} {level,-7} [{category}] {message}");
                if (exception != null)
                {
                    Console.WriteLine(exception);
                }
            }
        }
    }
}
