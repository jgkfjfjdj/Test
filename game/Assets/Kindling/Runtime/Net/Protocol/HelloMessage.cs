using Kindling.Core.Serialization;

namespace Kindling.Net.Protocol
{
    /// <summary>Client → server, first message after the transport connects (reliable).</summary>
    public struct HelloMessage
    {
        public ushort ProtocolVersion;
        public string BuildVersion;
        public string DisplayName;

        /// <summary>Join ticket from the backend (or empty on dev servers that allow dev auth).</summary>
        public string AuthToken;

        /// <summary>Token from an earlier Welcome, to take back the same player after a drop. Empty for a new join.</summary>
        public byte[] ResumeToken;

        /// <summary>Fingerprint of the client's game content; must match the server's.</summary>
        public string ContentFingerprint;

        public void Write(PacketWriter w)
        {
            w.WriteByte((byte)MessageType.Hello);
            w.WriteUShort(ProtocolVersion);
            w.WriteString(BuildVersion, NetProtocol.MaxBuildVersionBytes);
            w.WriteString(DisplayName, NetProtocol.MaxDisplayNameBytes);
            w.WriteString(AuthToken, NetProtocol.MaxAuthTokenBytes);
            w.WriteBlob(ResumeToken);
            w.WriteString(ContentFingerprint ?? string.Empty, 32);
        }

        /// <summary>Reads the body (after the type byte). Only the version is read if it doesn't match ours.</summary>
        public static HelloMessage Read(ref PacketReader r)
        {
            var m = new HelloMessage { ProtocolVersion = r.ReadUShort() };
            if (m.ProtocolVersion != NetProtocol.Version)
            {
                return m;
            }

            m.BuildVersion = r.ReadString(NetProtocol.MaxBuildVersionBytes);
            m.DisplayName = r.ReadString(NetProtocol.MaxDisplayNameBytes);
            m.AuthToken = r.ReadString(NetProtocol.MaxAuthTokenBytes);
            m.ResumeToken = r.ReadBlob(NetProtocol.ResumeTokenLength);
            m.ContentFingerprint = r.ReadString(32);
            return m;
        }
    }
}
