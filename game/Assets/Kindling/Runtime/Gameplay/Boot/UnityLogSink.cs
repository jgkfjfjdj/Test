using System;
using Kindling.Core.Diagnostics;
using UnityEngine;

namespace Kindling.Gameplay.Boot
{
    /// <summary>Routes <see cref="Log"/> output to the Unity console / player log.</summary>
    public sealed class UnityLogSink : ILogSink
    {
        public void Write(LogLevel level, string category, string message, Exception exception)
        {
            string line = $"[{category}] {message}";
            switch (level)
            {
                case LogLevel.Error:
                    Debug.LogError(line);
                    if (exception != null)
                    {
                        Debug.LogException(exception);
                    }

                    break;
                case LogLevel.Warning:
                    Debug.LogWarning(line);
                    break;
                default:
                    Debug.Log(line);
                    break;
            }
        }
    }
}
