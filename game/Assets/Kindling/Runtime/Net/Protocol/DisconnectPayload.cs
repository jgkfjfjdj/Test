namespace Kindling.Net.Protocol
{
    /// <summary>Encodes the reason carried with a transport disconnect: code byte + optional message.</summary>
    public static class DisconnectPayload
    {
        public static byte[] Encode(DisconnectCode code, string message = null)
        {
            var w = new PacketWriter(64);
            w.WriteByte((byte)code);
            w.WriteString(message ?? string.Empty, 200);
            return w.ToArray();
        }

        /// <summary>Decodes a payload; an empty or malformed payload gives <paramref name="fallback"/>.</summary>
        public static DisconnectCode Decode(byte[] data, int offset, int count, DisconnectCode fallback, out string message)
        {
            message = string.Empty;
            if (data == null || count <= 0)
            {
                return fallback;
            }

            try
            {
                var r = new PacketReader(data, offset, count);
                var code = (DisconnectCode)r.ReadByte();
                if (!r.AtEnd)
                {
                    message = r.ReadString(200);
                }

                return code;
            }
            catch (ProtocolException)
            {
                return fallback;
            }
        }
    }
}
