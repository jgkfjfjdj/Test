using System.Collections.Generic;
using System.Numerics;
using Kindling.Content;
using Kindling.Core.Serialization;
using Kindling.Simulation.Collision;
using Kindling.Simulation.World;

namespace Kindling.Net.Protocol
{
    /// <summary>
    /// Server → client before Welcome (reliable, ~25 KB): the static map with exact floats, so the client's
    /// collision world (prediction) is identical to the server's.
    /// </summary>
    public static class MapDataMessage
    {
        public static void Write(PacketWriter w, StaticMap map)
        {
            w.WriteByte((byte)MessageType.MapData);
            w.WriteInt(map.Seed);
            w.WriteFloat(map.MapSize);
            w.WriteVarUInt((uint)map.Pieces.Count);
            foreach (GreyboxPiece p in map.Pieces)
            {
                w.WriteByte((byte)p.Kind);
                WriteVector(w, p.Box.Min);
                WriteVector(w, p.Box.Max);
            }

            w.WriteVarUInt((uint)map.Nodes.Count);
            foreach (NodePlacement n in map.Nodes)
            {
                w.WriteUShort(n.Def.NetId);
                WriteVector(w, n.Position);
                w.WriteFloat(n.Yaw);
            }

            w.WriteVarUInt((uint)map.Ponds.Count);
            foreach (Aabb pond in map.Ponds)
            {
                WriteVector(w, pond.Min);
                WriteVector(w, pond.Max);
            }
        }

        public static StaticMap Read(ref PacketReader r, ContentDatabase content)
        {
            int seed = r.ReadInt();
            float size = r.ReadFloat();
            uint pieceCount = Limit(r.ReadVarUInt(), 20000);
            var pieces = new List<GreyboxPiece>((int)pieceCount);
            for (uint i = 0; i < pieceCount; i++)
            {
                var kind = (GreyboxKind)r.ReadByte();
                pieces.Add(new GreyboxPiece(new Aabb(ReadVector(ref r), ReadVector(ref r)), kind));
            }

            uint nodeCount = Limit(r.ReadVarUInt(), 200000);
            var nodes = new List<NodePlacement>((int)nodeCount);
            for (uint i = 0; i < nodeCount; i++)
            {
                ushort id = r.ReadUShort();
                NodeDef def = content.ByNetId<NodeDef>(id) ?? throw new ProtocolException($"Unknown node type {id}: content mismatch.");
                nodes.Add(new NodePlacement { Def = def, Position = ReadVector(ref r), Yaw = r.ReadFloat() });
            }

            uint pondCount = Limit(r.ReadVarUInt(), 1000);
            var ponds = new List<Aabb>((int)pondCount);
            for (uint i = 0; i < pondCount; i++)
            {
                ponds.Add(new Aabb(ReadVector(ref r), ReadVector(ref r)));
            }

            return new StaticMap(seed, size, pieces, nodes, ponds);
        }

        static uint Limit(uint value, uint max) => value <= max ? value : throw new ProtocolException("Map too large.");

        internal static void WriteVector(PacketWriter w, Vector3 v)
        {
            w.WriteFloat(v.X);
            w.WriteFloat(v.Y);
            w.WriteFloat(v.Z);
        }

        internal static Vector3 ReadVector(ref PacketReader r) => new Vector3(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
    }
}
