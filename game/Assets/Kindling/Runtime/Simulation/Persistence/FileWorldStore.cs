using System;
using System.Collections.Generic;
using System.IO;
using Kindling.Core.Serialization;

namespace Kindling.Simulation.Persistence
{
    /// <summary>
    /// Saves the whole world as one binary file, replaced atomically on every write (temp file + rename),
    /// keeping the previous version as <c>.bak</c>. A checksum detects corruption; loading falls back to the
    /// backup. Dependency-free, so it works everywhere (offline play on iOS, the Unity server, tests).
    /// Writes are synchronous: meant for small worlds; big servers use the SQLite store.
    /// </summary>
    public sealed class FileWorldStore : IWorldStore
    {
        static readonly byte[] Magic = { (byte)'K', (byte)'W', (byte)'S', (byte)'1' };
        const uint FormatVersion = 1;

        readonly string m_Path;
        WorldSaveData m_Image;

        public FileWorldStore(string path)
        {
            m_Path = path;
        }

        public string Path => m_Path;

        /// <summary>Problems seen while loading (e.g. fell back to the backup).</summary>
        public List<string> Warnings { get; } = new List<string>();

        public WorldSaveData Load()
        {
            m_Image = TryRead(m_Path, out string error);
            if (m_Image == null && error != null)
            {
                Warnings.Add($"{m_Path}: {error}; trying the backup.");
                m_Image = TryRead(m_Path + ".bak", out string backupError);
                if (m_Image == null && backupError != null)
                {
                    throw new InvalidDataException($"World save {m_Path} and its backup are unreadable: {error}; {backupError}");
                }
            }

            WorldSaveData result = m_Image;
            m_Image ??= new WorldSaveData();
            return result;
        }

        public void Write(SaveBatch batch)
        {
            m_Image ??= TryRead(m_Path, out _) ?? new WorldSaveData();
            m_Image.Apply(batch);
            byte[] bytes = Serialize(m_Image);
            string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(m_Path));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string temp = m_Path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if (File.Exists(m_Path))
            {
                try
                {
                    File.Replace(temp, m_Path, m_Path + ".bak");
                    return;
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(m_Path, m_Path + ".bak", true);
                    File.Delete(m_Path);
                }
            }

            File.Move(temp, m_Path);
        }

        public void Flush()
        {
        }

        public void Dispose()
        {
        }

        static WorldSaveData TryRead(string path, out string error)
        {
            error = null;
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return Deserialize(File.ReadAllBytes(path));
            }
            catch (Exception e) when (e is ProtocolException || e is InvalidDataException || e is IOException)
            {
                error = e.Message;
                return null;
            }
        }

        public static byte[] Serialize(WorldSaveData data)
        {
            var w = new PacketWriter(64 * 1024);
            w.WriteBytes(Magic, 0, Magic.Length);
            w.WriteUInt(FormatVersion);

            w.WriteVarUInt((uint)data.Meta.Count);
            foreach (var pair in data.Meta)
            {
                w.WriteString(pair.Key, 64);
                w.WriteString(pair.Value, 256);
            }

            w.WriteVarUInt((uint)data.Characters.Count);
            foreach (CharacterRecord c in data.Characters.Values)
            {
                w.WriteString(c.AccountId, 128);
                w.WriteString(c.Name, 64);
                w.WriteByte(c.Life);
                w.WriteFloat(c.Position.X);
                w.WriteFloat(c.Position.Y);
                w.WriteFloat(c.Position.Z);
                w.WriteFloat(c.Yaw);
                w.WriteBool(c.Sleeping);
                SaveCodec.WriteDouble(w, c.LastSeen);
                w.WriteBlob(c.Data);
            }

            w.WriteVarUInt((uint)data.Entities.Count);
            foreach (EntityRecord e in data.Entities.Values)
            {
                w.WriteUInt(e.Id);
                w.WriteUShort(e.TypeId);
                w.WriteInt(e.ChunkX);
                w.WriteInt(e.ChunkZ);
                w.WriteFloat(e.Position.X);
                w.WriteFloat(e.Position.Y);
                w.WriteFloat(e.Position.Z);
                w.WriteFloat(e.Yaw);
                w.WriteString(e.OwnerId ?? string.Empty, 128);
                w.WriteFloat(e.Health);
                w.WriteByte(e.Flags);
                w.WriteBlob(e.Data);
            }

            w.WriteVarUInt((uint)data.Nodes.Count);
            foreach (NodeRecord n in data.Nodes.Values)
            {
                w.WriteVarUInt((uint)n.Index);
                w.WriteBool(n.Alive);
                w.WriteFloat(n.Remaining);
                SaveCodec.WriteDouble(w, n.RespawnAt);
            }

            w.WriteVarUInt((uint)data.LootSpawns.Count);
            foreach (LootSpawnRecord l in data.LootSpawns.Values)
            {
                w.WriteVarUInt((uint)l.Index);
                w.WriteUInt(l.EntityId);
                SaveCodec.WriteDouble(w, l.NextAt);
            }

            ulong hash = Checksum(w.Buffer, w.Length);
            w.WriteUInt((uint)hash);
            w.WriteUInt((uint)(hash >> 32));
            return w.ToArray();
        }

        public static WorldSaveData Deserialize(byte[] bytes)
        {
            if (bytes.Length < 16)
            {
                throw new InvalidDataException("File too short.");
            }

            for (int i = 0; i < Magic.Length; i++)
            {
                if (bytes[i] != Magic[i])
                {
                    throw new InvalidDataException("Not a Kindling world save.");
                }
            }

            ulong expected = BitConverter.ToUInt32(bytes, bytes.Length - 8) | ((ulong)BitConverter.ToUInt32(bytes, bytes.Length - 4) << 32);
            if (Checksum(bytes, bytes.Length - 8) != expected)
            {
                throw new InvalidDataException("Checksum mismatch (the file is damaged).");
            }

            var r = new PacketReader(bytes, 4, bytes.Length - 12);
            uint version = r.ReadUInt();
            if (version != FormatVersion)
            {
                throw new InvalidDataException($"Unsupported save format {version}.");
            }

            var data = new WorldSaveData();
            uint meta = r.ReadVarUInt();
            for (uint i = 0; i < meta; i++)
            {
                data.Meta[r.ReadString(64)] = r.ReadString(256);
            }

            uint characters = r.ReadVarUInt();
            for (uint i = 0; i < characters; i++)
            {
                var c = new CharacterRecord
                {
                    AccountId = r.ReadString(128),
                    Name = r.ReadString(64),
                    Life = r.ReadByte(),
                    Position = new System.Numerics.Vector3(r.ReadFloat(), r.ReadFloat(), r.ReadFloat()),
                    Yaw = r.ReadFloat(),
                    Sleeping = r.ReadBool(),
                    LastSeen = SaveCodec.ReadDouble(ref r),
                    Data = r.ReadBlob(1 << 20),
                };
                data.Characters[c.AccountId] = c;
            }

            uint entities = r.ReadVarUInt();
            for (uint i = 0; i < entities; i++)
            {
                var e = new EntityRecord
                {
                    Id = r.ReadUInt(),
                    TypeId = r.ReadUShort(),
                    ChunkX = r.ReadInt(),
                    ChunkZ = r.ReadInt(),
                    Position = new System.Numerics.Vector3(r.ReadFloat(), r.ReadFloat(), r.ReadFloat()),
                    Yaw = r.ReadFloat(),
                };
                string owner = r.ReadString(128);
                e.OwnerId = owner.Length > 0 ? owner : null;
                e.Health = r.ReadFloat();
                e.Flags = r.ReadByte();
                e.Data = r.ReadBlob(1 << 20);
                data.Entities[e.Id] = e;
            }

            uint nodes = r.ReadVarUInt();
            for (uint i = 0; i < nodes; i++)
            {
                var n = new NodeRecord { Index = (int)r.ReadVarUInt(), Alive = r.ReadBool(), Remaining = r.ReadFloat(), RespawnAt = SaveCodec.ReadDouble(ref r) };
                data.Nodes[n.Index] = n;
            }

            uint loot = r.ReadVarUInt();
            for (uint i = 0; i < loot; i++)
            {
                var l = new LootSpawnRecord { Index = (int)r.ReadVarUInt(), EntityId = r.ReadUInt(), NextAt = SaveCodec.ReadDouble(ref r) };
                data.LootSpawns[l.Index] = l;
            }

            return data;
        }

        static ulong Checksum(byte[] data, int length)
        {
            ulong h = 1469598103934665603UL;
            for (int i = 0; i < length; i++)
            {
                h ^= data[i];
                h *= 1099511628211UL;
            }

            return h;
        }
    }
}
