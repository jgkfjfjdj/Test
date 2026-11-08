using System;

namespace Kindling.Core.Serialization
{
    /// <summary>A malformed or unexpected message. The receiver drops the connection that sent it.</summary>
    public sealed class ProtocolException : Exception
    {
        public ProtocolException(string message)
            : base(message)
        {
        }
    }
}
