using System;
using System.Collections.Generic;
using Kindling.Core.Diagnostics;
using NUnit.Framework;

namespace Kindling.Tests.Pure
{
    public class LogTests
    {
        sealed class CaptureSink : ILogSink
        {
            public readonly List<string> Lines = new List<string>();

            public void Write(LogLevel level, string category, string message, Exception exception)
            {
                Lines.Add($"{level}|{category}|{message}|{exception?.Message}");
            }
        }

        ILogSink m_PreviousSink;
        LogLevel m_PreviousLevel;

        [SetUp]
        public void SaveGlobals()
        {
            m_PreviousSink = Log.Sink;
            m_PreviousLevel = Log.MinimumLevel;
        }

        [TearDown]
        public void RestoreGlobals()
        {
            Log.SetSink(m_PreviousSink);
            Log.MinimumLevel = m_PreviousLevel;
        }

        [Test]
        public void WritesAtOrAboveMinimumLevel()
        {
            var sink = new CaptureSink();
            Log.SetSink(sink);
            Log.MinimumLevel = LogLevel.Info;

            Log.Debug("Net", "dropped");
            Log.Info("Net", "hello");
            Log.Error("Save", "failed", new InvalidOperationException("disk full"));

            Assert.That(sink.Lines, Is.EqualTo(new[] { "Info|Net|hello|", "Error|Save|failed|disk full" }));
        }

        [Test]
        public void WithoutSinkNothingIsEnabled()
        {
            Log.SetSink(null);
            Assert.That(Log.IsEnabled(LogLevel.Error), Is.False);
            Assert.DoesNotThrow(() => Log.Error("x", "y"));
        }
    }
}
