using System.Collections.Generic;
using System.Numerics;

namespace Kindling.Simulation.Persistence
{
    /// <summary>
    /// Store-independent save records. Inventories and type-specific state are versioned binary blobs
    /// (<see cref="SaveCodec"/>); items are stored by their permanent content net id.
    /// </summary>
    public sealed class CharacterRecord
    {
        public string AccountId;
        public string Name;
        public byte Life;
        public Vector3 Position;
        public float Yaw;
        public bool Sleeping;
        public double LastSeen;
        public byte[] Data;
    }

    public sealed class EntityRecord
    {
        public uint Id;
        public ushort TypeId;
        public int ChunkX;
        public int ChunkZ;
        public Vector3 Position;
        public float Yaw;
        public string OwnerId;
        public float Health;
        public byte Flags;
        public byte[] Data;
    }

    public struct NodeRecord
    {
        public int Index;
        public bool Alive;
        public float Remaining;
        public double RespawnAt;

        /// <summary>Alive and untouched: stores delete the row instead of keeping it.</summary>
        public bool IsDefault;
    }

    public struct LootSpawnRecord
    {
        public int Index;
        public uint EntityId;
        public double NextAt;
    }

    /// <summary>What changed since the last save (or everything, when <see cref="Full"/>).</summary>
    public sealed class SaveBatch
    {
        public bool Full;
        public readonly Dictionary<string, string> Meta = new Dictionary<string, string>();
        public readonly List<CharacterRecord> Characters = new List<CharacterRecord>();
        public readonly List<EntityRecord> Entities = new List<EntityRecord>();
        public readonly List<uint> RemovedEntities = new List<uint>();
        public readonly List<NodeRecord> Nodes = new List<NodeRecord>();
        public readonly List<LootSpawnRecord> LootSpawns = new List<LootSpawnRecord>();

        public int RecordCount => Characters.Count + Entities.Count + RemovedEntities.Count + Nodes.Count + LootSpawns.Count;
    }

    /// <summary>A whole saved world as loaded from a store.</summary>
    public sealed class WorldSaveData
    {
        public readonly Dictionary<string, string> Meta = new Dictionary<string, string>();
        public readonly Dictionary<string, CharacterRecord> Characters = new Dictionary<string, CharacterRecord>();
        public readonly Dictionary<uint, EntityRecord> Entities = new Dictionary<uint, EntityRecord>();
        public readonly Dictionary<int, NodeRecord> Nodes = new Dictionary<int, NodeRecord>();
        public readonly Dictionary<int, LootSpawnRecord> LootSpawns = new Dictionary<int, LootSpawnRecord>();

        /// <summary>Applies a batch (stores that keep an in-memory image use this).</summary>
        public void Apply(SaveBatch batch)
        {
            if (batch.Full)
            {
                Meta.Clear();
                Characters.Clear();
                Entities.Clear();
                Nodes.Clear();
                LootSpawns.Clear();
            }

            foreach (var pair in batch.Meta)
            {
                Meta[pair.Key] = pair.Value;
            }

            foreach (CharacterRecord c in batch.Characters)
            {
                Characters[c.AccountId] = c;
            }

            foreach (uint id in batch.RemovedEntities)
            {
                Entities.Remove(id);
            }

            foreach (EntityRecord e in batch.Entities)
            {
                Entities[e.Id] = e;
            }

            foreach (NodeRecord n in batch.Nodes)
            {
                if (n.IsDefault)
                {
                    Nodes.Remove(n.Index);
                }
                else
                {
                    Nodes[n.Index] = n;
                }
            }

            foreach (LootSpawnRecord l in batch.LootSpawns)
            {
                LootSpawns[l.Index] = l;
            }
        }
    }

    /// <summary>Where a world is saved. Writes may complete in the background; <see cref="Flush"/> waits for them.</summary>
    public interface IWorldStore : System.IDisposable
    {
        /// <summary>The saved world, or null if there is none yet.</summary>
        WorldSaveData Load();

        void Write(SaveBatch batch);

        void Flush();
    }
}
