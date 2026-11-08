using Kindling.Content;
using Kindling.Core.Numerics;
using Kindling.Simulation.World;

namespace Kindling.Simulation.Game
{
    public sealed partial class GameWorld
    {
        public struct LootSpawnState
        {
            /// <summary>Entity currently at this spawn (0 = none).</summary>
            public uint EntityId;

            /// <summary>World time of the next refill or respawn.</summary>
            public double NextAt;
        }

        readonly LootSpawnState[] m_LootSpawnState;
        readonly DeterministicRandom m_BeachRng;

        public LootSpawnState[] LootSpawnStates => m_LootSpawnState;

        void SpawnLoot(int index)
        {
            LootSpawn spawn = Map.LootSpawns[index];
            WorldEntity e = Entities.Spawn(spawn.Def, spawn.Position, spawn.Yaw);
            e.SpawnerIndex = index;
            if (e.Container != null && spawn.Def.LootTable != null)
            {
                LootRoller.Fill(e.Container, spawn.Def.LootTable, Rng, m_StackScratch);
            }

            Entities.MarkChanged(e);
            m_LootSpawnState[index] = new LootSpawnState { EntityId = e.Id, NextAt = Now + spawn.Def.RefillSeconds };
        }

        /// <summary>Crates refill on their timer; broken barrels come back after theirs.</summary>
        void UpdateLoot()
        {
            for (int i = 0; i < m_LootSpawnState.Length; i++)
            {
                LootSpawnState s = m_LootSpawnState[i];
                if (Now < s.NextAt)
                {
                    continue;
                }

                WorldEntity e = s.EntityId != 0 ? Entities.Get(s.EntityId) : null;
                if (e == null)
                {
                    SpawnLoot(i);
                }
                else if (e.Container != null && e.Def.LootTable != null)
                {
                    LootRoller.Fill(e.Container, e.Def.LootTable, Rng, m_StackScratch);
                    Entities.MarkChanged(e);
                    m_LootSpawnState[i].NextAt = Now + e.Def.RefillSeconds;
                }
                else
                {
                    m_LootSpawnState[i].NextAt = Now + Map.LootSpawns[i].Def.RefillSeconds;
                }
            }
        }

        void OnLootEntityGone(WorldEntity e)
        {
            if (e.SpawnerIndex >= 0 && e.SpawnerIndex < m_LootSpawnState.Length)
            {
                m_LootSpawnState[e.SpawnerIndex] = new LootSpawnState { EntityId = 0, NextAt = Now + e.Def.RefillSeconds };
            }
        }

        /// <summary>Restores a spawn's state from a save.</summary>
        public void RestoreLootSpawn(int index, uint entityId, double nextAt)
        {
            if (index >= 0 && index < m_LootSpawnState.Length)
            {
                m_LootSpawnState[index] = new LootSpawnState { EntityId = entityId, NextAt = nextAt };
            }
        }
    }
}
