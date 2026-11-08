using System;
using System.Collections.Generic;
using System.Numerics;
using Kindling.Content;
using Kindling.Core.Numerics;
using Kindling.Core.World;
using Kindling.Simulation.Collision;
using Kindling.Simulation.Items;
using Kindling.Simulation.Movement;
using Kindling.Simulation.World;

namespace Kindling.Simulation.Game
{
    /// <summary>
    /// The authoritative survival world: characters, resource nodes, world entities and time. The network
    /// server calls <see cref="Tick"/> every simulation tick and forwards player requests to the action
    /// methods, which validate everything and return an <see cref="ActionError"/>. Engine-independent.
    /// </summary>
    public sealed partial class GameWorld
    {
        readonly Dictionary<string, Character> m_Characters = new Dictionary<string, Character>();
        readonly List<Character> m_CharacterList = new List<Character>();
        readonly List<WorldEntity> m_EntityScratch = new List<WorldEntity>();
        readonly List<ItemStack> m_StackScratch = new List<ItemStack>();
        readonly List<int> m_IntScratch = new List<int>();
        float m_SlowTimer;

        public GameWorld(ContentDatabase content, WorldMap map, GameConfig config, ulong seed)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Map = map;
            Config = config ?? GameConfig.FromRules(content.Rules);
            Rules = content.Rules;
            Grid = new WorldGrid(map.Static.MapSize);
            Collision = map.Static.BuildCollisionWorld();
            Resources = new ResourceField(map.Static, Grid);
            Entities = new EntityStore(Grid);
            Clock = new WorldClock(new WorldRules { DayLengthMinutes = Config.DayLengthMinutes, NightFraction = content.Rules.World.NightFraction });
            Rng = new DeterministicRandom(seed);
            Movement = new MovementSettings();
            m_LootSpawnState = new LootSpawnState[map.LootSpawns.Count];
            m_BeachRng = new DeterministicRandom(seed ^ 0xBEAC4UL);
        }

        public ContentDatabase Content { get; }

        public RulesDef Rules { get; }

        public GameConfig Config { get; }

        public WorldMap Map { get; }

        public WorldGrid Grid { get; }

        public CollisionWorld Collision { get; }

        public ResourceField Resources { get; }

        public EntityStore Entities { get; }

        public WorldClock Clock { get; }

        public DeterministicRandom Rng { get; }

        public MovementSettings Movement { get; }

        public double Now => Clock.Seconds;

        public IReadOnlyList<Character> Characters => m_CharacterList;

        public Character FindCharacter(string accountId) => accountId != null && m_Characters.TryGetValue(accountId, out Character c) ? c : null;

        /// <summary>Spawns loot containers and breakables at their map positions. Call once for a fresh world (not after loading).</summary>
        public void PopulateFreshWorld()
        {
            for (int i = 0; i < Map.LootSpawns.Count; i++)
            {
                SpawnLoot(i);
            }
        }

        /// <summary>A player connects: their existing character wakes up, or a new one spawns.</summary>
        public Character Join(string accountId, string displayName)
        {
            Character c = FindCharacter(accountId);
            if (c == null)
            {
                c = AddCharacter(accountId, displayName);
                Respawn(c, 0, force: true);
            }

            c.Name = string.IsNullOrEmpty(displayName) ? c.Name : displayName;
            c.Online = true;
            RemoveSleeper(c);
            c.StateVersion++;
            return c;
        }

        /// <summary>Adds a character without spawning it (loading a save).</summary>
        public Character AddCharacter(string accountId, string name)
        {
            var c = new Character(accountId, name);
            m_Characters[accountId] = c;
            m_CharacterList.Add(c);
            return c;
        }

        /// <summary>A player disconnects: their character stays in the world asleep (see <see cref="SleeperMode"/>).</summary>
        public void Leave(Character c)
        {
            if (c == null || !c.Online)
            {
                return;
            }

            c.Online = false;
            c.OfflineSince = Now;
            c.OpenContainerId = 0;
            c.Motion.Velocity = Vector3.Zero;
            if (c.Life == LifeState.Downed)
            {
                Die(c, c.LastDamage); // logging out while downed doesn't save you
            }

            if (c.Life != LifeState.Dead)
            {
                SpawnSleeper(c);
            }
        }

        public void Tick(float dt)
        {
            if (!(dt > 0))
            {
                return;
            }

            Clock.Advance(dt);
            foreach (Character c in m_CharacterList)
            {
                if (c.Online)
                {
                    UpdateSurvival(c, dt);
                    UpdateCrafting(c, dt);
                }
            }

            m_SlowTimer += dt;
            if (m_SlowTimer >= 0.25f)
            {
                float slowDt = m_SlowTimer;
                m_SlowTimer = 0;
                UpdateProcessors(slowDt);
                Resources.Update(Now);
                UpdateEntityLifetimes();
                UpdateLoot();
                UpdateSleepers();
            }
        }

        // ---------------------------------------------------------------- helpers

        public Vector3 EyeOf(Character c) => c.Motion.Position + new Vector3(0f, Movement.EyeHeightFor(c.Motion.Crouched), 0f);

        public static Vector3 ViewDirection(float yaw, float pitch)
        {
            double y = yaw * Math.PI / 180.0;
            double p = pitch * Math.PI / 180.0;
            return new Vector3((float)(Math.Sin(y) * Math.Cos(p)), (float)Math.Sin(p), (float)(Math.Cos(y) * Math.Cos(p)));
        }

        public ItemContainer Resolve(Character c, ContainerRef r, out ActionError error)
        {
            error = ActionError.None;
            switch (r.Kind)
            {
                case ContainerKind.Main: return c.Inventory.Main;
                case ContainerKind.Hotbar: return c.Inventory.Hotbar;
                case ContainerKind.Wear: return c.Inventory.Wear;
                case ContainerKind.Entity:
                    WorldEntity e = Entities.Get(r.EntityId);
                    if (e == null || e.Container == null)
                    {
                        error = ActionError.NothingThere;
                        return null;
                    }

                    if (c.OpenContainerId != e.Id)
                    {
                        error = ActionError.NoAccess;
                        return null;
                    }

                    if (!InReach(c, e, Rules.Crafting.InteractRange + 1f))
                    {
                        error = ActionError.TooFar;
                        return null;
                    }

                    return e.Container;
                default:
                    error = ActionError.InvalidSlot;
                    return null;
            }
        }

        public bool InReach(Character c, WorldEntity e, float range)
        {
            Vector3 eye = EyeOf(c);
            Aabb box = BoundsOf(e);
            float dx = Math.Max(Math.Max(box.Min.X - eye.X, 0f), eye.X - box.Max.X);
            float dy = Math.Max(Math.Max(box.Min.Y - eye.Y, 0f), eye.Y - box.Max.Y);
            float dz = Math.Max(Math.Max(box.Min.Z - eye.Z, 0f), eye.Z - box.Max.Z);
            return dx * dx + dy * dy + dz * dz <= range * range;
        }

        public static Aabb BoundsOf(WorldEntity e)
        {
            bool sideways = ((int)Math.Round(e.Yaw / 90f) & 1) == 1;
            float sx = sideways ? e.Def.Size.Z : e.Def.Size.X;
            float sz = sideways ? e.Def.Size.X : e.Def.Size.Z;
            return new Aabb(e.Position - new Vector3(sx / 2, 0f, sz / 2), e.Position + new Vector3(sx / 2, e.Def.Size.Y, sz / 2));
        }

        /// <summary>Gives items to a character; what doesn't fit is dropped at their feet.</summary>
        public void Give(Character c, ItemDef def, int amount, float condition = -1f)
        {
            if (def == null || amount <= 0)
            {
                return;
            }

            int left = c.Inventory.Add(def, amount, condition);
            if (left > 0)
            {
                DropAt(c.Motion.Position + Forward(c) * 0.8f, new ItemStack(def, left, condition < 0 ? def.MaxCondition : condition));
            }

            if (left < amount)
            {
                c.Events.Add(new GameEvent { Kind = GameEventKind.ItemsGained, ItemNetId = def.NetId, Amount = amount - left });
            }
        }

        public WorldEntity DropAt(Vector3 position, ItemStack stack)
        {
            if (stack.IsEmpty)
            {
                return null;
            }

            WorldEntity e = Entities.Spawn(Content.Entity("entity.dropped_item"), new Vector3(position.X, 0f, position.Z), 0f);
            e.Container?.Set(0, stack);
            if (e.Container == null)
            {
                return null;
            }

            e.DecayAt = Now + Math.Max(e.Def.DecaySeconds, Rules.World.DroppedItemSeconds);
            Entities.MarkChanged(e);
            return e;
        }

        static Vector3 Forward(Character c) => ViewDirection(c.Motion.Yaw, 0f);

        void UpdateEntityLifetimes()
        {
            m_IntScratch.Clear();
            foreach (WorldEntity e in Entities.All)
            {
                if (e.DecayAt > 0 && Now >= e.DecayAt)
                {
                    m_IntScratch.Add((int)e.Id);
                }
                else if (e.Def.Kind == EntityKind.DroppedItem && (e.Container == null || e.Container.IsEmpty))
                {
                    m_IntScratch.Add((int)e.Id);
                }
            }

            foreach (int id in m_IntScratch)
            {
                Entities.Remove((uint)id);
            }
        }
    }
}
