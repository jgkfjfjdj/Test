using System.Collections.Generic;
using Kindling.Core.Serialization;

namespace Kindling.Net.Protocol
{
    public struct PlayerInfo
    {
        public ushort Id;
        public string DisplayName;
    }

    /// <summary>Server → client (reliable): names of players (all of them after Welcome, then each new one).</summary>
    public static class PlayerInfoMessage
    {
        public static void Write(PacketWriter w, IReadOnlyList<PlayerInfo> players, int start, int count)
        {
            w.WriteByte((byte)MessageType.PlayerInfo);
            w.WriteVarUInt((uint)count);
            for (int i = start; i < start + count; i++)
            {
                w.WriteUShort(players[i].Id);
                w.WriteString(players[i].DisplayName, NetProtocol.MaxDisplayNameBytes);
            }
        }

        public static void Read(ref PacketReader r, List<PlayerInfo> results)
        {
            results.Clear();
            uint count = r.ReadVarUInt();
            if (count > 1024)
            {
                throw new ProtocolException("Too many players.");
            }

            for (int i = 0; i < count; i++)
            {
                results.Add(new PlayerInfo { Id = r.ReadUShort(), DisplayName = r.ReadString(NetProtocol.MaxDisplayNameBytes) });
            }
        }
    }
}
