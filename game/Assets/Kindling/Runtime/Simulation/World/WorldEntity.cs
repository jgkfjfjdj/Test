using System;
using System.Numerics;
using Kindling.Content;
using Kindling.Core.World;
using Kindling.Simulation.Items;

namespace Kindling.Simulation.World
{
    [Flags]
    public enum WorldEntityFlags : byte
    {
        None = 0,

        /// <summary>A processor (campfire, smelter) is burning.</summary>
        Lit = 1,
    }

    /// <summary>A persistent thing in the world that isn't a player: deployable, loot container, dropped item, body, sleeper.</summary>
    public sealed class WorldEntity
    {
        internal WorldEntity(uint id, EntityDef def, Vector3 position, float yaw, ChunkCoord chunk)
        {
            Id = id;
            Def = def;
            Position = position;
            Yaw = yaw;
            Chunk = chunk;
            Health = def.Health;
            if (def.ContainerSlots > 0)
            {
                Container = new ItemContainer(ContainerRole.Storage, def.ContainerSlots);
            }
        }

        public uint Id { get; }

        public EntityDef Def { get; }

        public Vector3 Position { get; internal set; }

        public float Yaw { get; internal set; }

        public ChunkCoord Chunk { get; internal set; }

        public ItemContainer Container { get; }

        public float Health { get; set; }

        public WorldEntityFlags Flags { get; set; }

        /// <summary>Account that placed it (deployables), or whose body/sleeper it is.</summary>
        public string OwnerId { get; set; }

        /// <summary>Shown to players: the name on a body or sleeper.</summary>
        public string Label { get; set; }

        /// <summary>World time when it removes itself (0 = never).</summary>
        public double DecayAt { get; set; }

        /// <summary>Index into the map's loot spawns, or -1.</summary>
        public int SpawnerIndex { get; set; } = -1;

        /// <summary>Seconds of fuel left in the item currently burning.</summary>
        public float FuelLeft { get; set; }

        /// <summary>Seconds of progress on the current conversion.</summary>
        public float Progress { get; set; }

        /// <summary>World time after which a respawn point can be used again.</summary>
        public double CooldownUntil { get; set; }

        public bool IsLit => (Flags & WorldEntityFlags.Lit) != 0;

        public uint Version { get; internal set; }

        public override string ToString() => $"{Def.Key}#{Id}";
    }
}
