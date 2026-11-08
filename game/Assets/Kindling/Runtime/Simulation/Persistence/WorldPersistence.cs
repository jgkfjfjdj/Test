using System;
using System.Collections.Generic;
using System.Globalization;
using Kindling.Content;
using Kindling.Core.World;
using Kindling.Simulation.Game;
using Kindling.Simulation.World;

namespace Kindling.Simulation.Persistence
{
    /// <summary>
    /// Saves and restores a <see cref="GameWorld"/>. The main thread captures what changed into records
    /// (cheap); the store writes them, possibly on its own thread. Characters are flushed immediately on
    /// disconnect; everything else goes out in periodic batches plus full checkpoints.
    /// </summary>
    public sealed class WorldPersistence
    {
        public const int SchemaVersion = 1;

        readonly GameWorld m_World;
        readonly IWorldStore m_Store;
        readonly HashSet<string> m_DirtyCharacters = new HashSet<string>();
        readonly List<WorldEntity> m_DirtyEntities = new List<WorldEntity>();
        readonly List<uint> m_Removed = new List<uint>();
        readonly List<int> m_Nodes = new List<int>();

        public WorldPersistence(GameWorld world, IWorldStore store)
        {
            m_World = world;
            m_Store = store;
        }

        public IWorldStore Store => m_Store;

        /// <summary>Loads the saved world into a new <see cref="GameWorld"/>, or creates and populates a fresh one.</summary>
        public static GameWorld LoadOrCreate(ContentDatabase content, WorldMap map, GameConfig config, ulong seed, IWorldStore store, List<string> warnings, out bool loaded)
        {
            var world = new GameWorld(content, map, config, seed);
            WorldSaveData data = store.Load();
            loaded = data != null && data.Meta.Count > 0;
            if (!loaded)
            {
                world.PopulateFreshWorld();
                return world;
            }

            if (data.Meta.TryGetValue("seed", out string savedSeed) && savedSeed != map.Static.Seed.ToString(CultureInfo.InvariantCulture))
            {
                throw new InvalidOperationException($"The save is for map seed {savedSeed}, but the server runs seed {map.Static.Seed}. Wipe the world or change the seed back.");
            }

            Restore(world, data, warnings);
            return world;
        }

        public static void Restore(GameWorld world, WorldSaveData data, List<string> warnings)
        {
            ContentDatabase content = world.Content;
            world.Clock.Seconds = GetDouble(data.Meta, "world_time");
            bool sameMap = data.Meta.TryGetValue("map_hash", out string hash) && hash == MapHash(world.Map);
            if (!sameMap)
            {
                warnings.Add("The map's resource nodes changed since the save: node states were reset.");
            }

            foreach (EntityRecord rec in data.Entities.Values)
            {
                EntityDef def = content.ByNetId<EntityDef>(rec.TypeId);
                if (def == null)
                {
                    warnings.Add($"Entity {rec.Id}: unknown type id {rec.TypeId}, removed.");
                    continue;
                }

                WorldEntity e = world.Entities.Add(rec.Id, def, rec.Position, rec.Yaw);
                e.OwnerId = rec.OwnerId;
                e.Health = rec.Health;
                e.Flags = (WorldEntityFlags)rec.Flags;
                if (rec.Data != null)
                {
                    SaveCodec.DecodeEntity(rec.Data, e, content, warnings);
                }
            }

            world.Entities.NextId = Math.Max(world.Entities.NextId, (uint)GetDouble(data.Meta, "next_entity_id"));

            if (sameMap)
            {
                foreach (NodeRecord n in data.Nodes.Values)
                {
                    world.Resources.Restore(n.Index, n.Alive, n.Remaining, n.RespawnAt);
                }

                foreach (LootSpawnRecord l in data.LootSpawns.Values)
                {
                    world.RestoreLootSpawn(l.Index, l.EntityId, l.NextAt);
                }
            }
            else
            {
                world.PopulateFreshWorld();
            }

            foreach (CharacterRecord rec in data.Characters.Values)
            {
                Character c = world.AddCharacter(rec.AccountId, rec.Name);
                SaveCodec.DecodeCharacter(rec.Data, c, content, warnings);
                c.Online = false;
                c.OfflineSince = world.Now;
                world.RestoreSleeper(c);
            }

            // Restoring logged everything as changes; a fresh replication/save cycle starts from here.
            world.Entities.ConsumeChanges(new List<EntityChangeRecord>());
            world.Entities.ConsumeDirty(new List<WorldEntity>(), new List<uint>());
            world.Resources.ConsumeChanges(new List<int>());
            world.Resources.ConsumeSaveDirty(new List<int>());
        }

        /// <summary>Marks a character to be saved in the next batch (or call <see cref="FlushCharacter"/>).</summary>
        public void MarkCharacterDirty(Character c) => m_DirtyCharacters.Add(c.AccountId);

        /// <summary>Writes one character right away (disconnects, deaths).</summary>
        public void FlushCharacter(Character c)
        {
            var batch = new SaveBatch();
            batch.Characters.Add(ToRecord(c));
            m_Store.Write(batch);
        }

        /// <summary>Saves what changed: online and dirty characters, changed/removed entities, node and loot state, meta.</summary>
        public SaveBatch SaveChanges()
        {
            var batch = new SaveBatch();
            WriteMeta(batch);
            foreach (Character c in m_World.Characters)
            {
                if (c.Online || m_DirtyCharacters.Contains(c.AccountId))
                {
                    batch.Characters.Add(ToRecord(c));
                }
            }

            m_DirtyCharacters.Clear();
            m_World.Entities.ConsumeDirty(m_DirtyEntities, m_Removed);
            foreach (WorldEntity e in m_DirtyEntities)
            {
                if (e.Def.Kind != EntityKind.Sleeper)
                {
                    batch.Entities.Add(ToRecord(e));
                }
            }

            batch.RemovedEntities.AddRange(m_Removed);
            m_World.Resources.ConsumeSaveDirty(m_Nodes);
            foreach (int i in m_Nodes)
            {
                batch.Nodes.Add(ToRecord(i));
            }

            AddLootSpawns(batch);
            m_Store.Write(batch);
            return batch;
        }

        /// <summary>Writes the whole world (checkpoint, shutdown).</summary>
        public SaveBatch Checkpoint()
        {
            var batch = new SaveBatch { Full = true };
            WriteMeta(batch);
            foreach (Character c in m_World.Characters)
            {
                batch.Characters.Add(ToRecord(c));
            }

            foreach (WorldEntity e in m_World.Entities.All)
            {
                if (e.Def.Kind != EntityKind.Sleeper)
                {
                    batch.Entities.Add(ToRecord(e));
                }
            }

            for (int i = 0; i < m_World.Resources.Count; i++)
            {
                NodeRecord n = ToRecord(i);
                if (!n.IsDefault)
                {
                    batch.Nodes.Add(n);
                }
            }

            AddLootSpawns(batch);
            m_World.Entities.ConsumeDirty(m_DirtyEntities, m_Removed);
            m_World.Resources.ConsumeSaveDirty(m_Nodes);
            m_DirtyCharacters.Clear();
            m_Store.Write(batch);
            return batch;
        }

        void WriteMeta(SaveBatch batch)
        {
            batch.Meta["schema_version"] = SchemaVersion.ToString(CultureInfo.InvariantCulture);
            batch.Meta["seed"] = m_World.Map.Static.Seed.ToString(CultureInfo.InvariantCulture);
            batch.Meta["map_hash"] = MapHash(m_World.Map);
            batch.Meta["world_time"] = m_World.Now.ToString("R", CultureInfo.InvariantCulture);
            batch.Meta["next_entity_id"] = m_World.Entities.NextId.ToString(CultureInfo.InvariantCulture);
        }

        void AddLootSpawns(SaveBatch batch)
        {
            GameWorld.LootSpawnState[] states = m_World.LootSpawnStates;
            for (int i = 0; i < states.Length; i++)
            {
                batch.LootSpawns.Add(new LootSpawnRecord { Index = i, EntityId = states[i].EntityId, NextAt = states[i].NextAt });
            }
        }

        static CharacterRecord ToRecord(Character c) => new CharacterRecord
        {
            AccountId = c.AccountId,
            Name = c.Name,
            Life = (byte)c.Life,
            Position = c.Motion.Position,
            Yaw = c.Motion.Yaw,
            Sleeping = !c.Online,
            LastSeen = c.Online ? 0 : c.OfflineSince,
            Data = SaveCodec.EncodeCharacter(c),
        };

        static EntityRecord ToRecord(WorldEntity e) => new EntityRecord
        {
            Id = e.Id,
            TypeId = e.Def.NetId,
            ChunkX = e.Chunk.X,
            ChunkZ = e.Chunk.Z,
            Position = e.Position,
            Yaw = e.Yaw,
            OwnerId = e.OwnerId,
            Health = e.Health,
            Flags = (byte)e.Flags,
            Data = SaveCodec.EncodeEntity(e),
        };

        NodeRecord ToRecord(int i)
        {
            ResourceField f = m_World.Resources;
            bool alive = f.IsAlive(i);
            float remaining = f.Remaining(i);
            return new NodeRecord
            {
                Index = i,
                Alive = alive,
                Remaining = remaining,
                RespawnAt = f.RespawnAt(i),
                IsDefault = alive && remaining >= f.Placement(i).Def.TotalYield,
            };
        }

        /// <summary>Identifies the node layout: saves only restore node states onto the same layout.</summary>
        public static string MapHash(WorldMap map)
        {
            unchecked
            {
                ulong h = 1469598103934665603UL;
                void Mix(int v)
                {
                    h ^= (uint)v;
                    h *= 1099511628211UL;
                }

                Mix(map.Static.Seed);
                Mix(map.Static.Nodes.Count);
                foreach (NodePlacement n in map.Static.Nodes)
                {
                    Mix(n.Def.NetId);
                    Mix(BitConverter.SingleToInt32Bits(n.Position.X));
                    Mix(BitConverter.SingleToInt32Bits(n.Position.Z));
                }

                Mix(map.LootSpawns.Count);
                return h.ToString("x16", CultureInfo.InvariantCulture);
            }
        }

        static double GetDouble(Dictionary<string, string> meta, string key) =>
            meta.TryGetValue(key, out string v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;
    }
}
