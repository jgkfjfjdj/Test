using System;
using System.Collections.Generic;
using System.Numerics;
using Kindling.Content;
using Kindling.Simulation.World;
using PlayerState = Kindling.Simulation.Movement.PlayerState;

namespace Kindling.Simulation.Game
{
    public sealed partial class GameWorld
    {
        /// <summary>Applies damage. Health reaching 0 downs the character (or kills, if downed time is 0).</summary>
        public void Damage(Character c, float amount, DamageCause cause)
        {
            if (c.Life != LifeState.Alive || amount <= 0)
            {
                return;
            }

            c.Stats.Health -= amount;
            c.LastDamage = cause;
            if (c.Stats.Health > 0)
            {
                return;
            }

            c.Stats.Health = 0;
            if (Rules.Death.DownedSeconds > 0)
            {
                c.Life = LifeState.Downed;
                c.DownedLeft = Rules.Death.DownedSeconds;
                c.StateVersion++;
                c.Events.Add(new GameEvent { Kind = GameEventKind.Downed, Code = (int)cause });
            }
            else
            {
                Die(c, cause);
            }
        }

        /// <summary>Another player helps a downed character up.</summary>
        public ActionError Revive(Character rescuer, Character target)
        {
            if (!rescuer.IsAlive)
            {
                return ActionError.Dead;
            }

            if (target == null || target.Life != LifeState.Downed)
            {
                return ActionError.InvalidTarget;
            }

            if (Vector3.Distance(rescuer.Motion.Position, target.Motion.Position) > Rules.Crafting.InteractRange)
            {
                return ActionError.TooFar;
            }

            target.Life = LifeState.Alive;
            target.Stats.Health = 10f;
            target.StateVersion++;
            return ActionError.None;
        }

        /// <summary>A downed player gives up and dies now.</summary>
        public ActionError GiveUp(Character c)
        {
            if (c.Life != LifeState.Downed)
            {
                return ActionError.InvalidTarget;
            }

            Die(c, DamageCause.GaveUp);
            return ActionError.None;
        }

        /// <summary>Death: crafting is refunded, everything goes into a body container that decays later.</summary>
        public void Die(Character c, DamageCause cause)
        {
            if (c.Life == LifeState.Dead)
            {
                return;
            }

            CancelAllCrafting(c);
            WorldEntity body = Entities.Spawn(Content.Entity("entity.body"), new Vector3(c.Motion.Position.X, 0f, c.Motion.Position.Z), c.Motion.Yaw);
            body.Label = c.Name;
            body.OwnerId = c.AccountId;
            body.DecayAt = Now + body.Def.DecaySeconds;
            c.Inventory.MoveAllTo(body.Container);
            Entities.MarkChanged(body);

            c.Life = LifeState.Dead;
            c.Stats.Health = 0;
            c.Stats.Effects = StatusEffect.None;
            c.Stats.PoisonLeft = 0;
            c.Stats.HealLeft = 0;
            c.OpenContainerId = 0;
            c.LastDamage = cause;
            c.Motion.Velocity = Vector3.Zero;
            c.StateVersion++;
            c.Events.Add(new GameEvent { Kind = GameEventKind.Died, Code = (int)cause, Target = (int)body.Id });
            RemoveSleeper(c);
        }

        /// <summary>
        /// Respawns a dead character at one of its bedrolls (<paramref name="bedrollId"/>) or, with 0, at a
        /// random edge spawn. Bedrolls have a cooldown.
        /// </summary>
        public ActionError Respawn(Character c, uint bedrollId, bool force = false)
        {
            if (!force && c.Life != LifeState.Dead)
            {
                return ActionError.InvalidTarget;
            }

            Vector3 position;
            if (bedrollId != 0)
            {
                WorldEntity bed = Entities.Get(bedrollId);
                if (bed == null || !bed.Def.IsRespawnPoint)
                {
                    return ActionError.NothingThere;
                }

                if (bed.OwnerId != c.AccountId)
                {
                    return ActionError.NoAccess;
                }

                if (Now < bed.CooldownUntil)
                {
                    return ActionError.Cooldown;
                }

                bed.CooldownUntil = Now + bed.Def.RespawnCooldownSeconds;
                Entities.MarkChanged(bed);
                position = bed.Position + new Vector3(0f, 0.25f, 0f);
            }
            else
            {
                position = Map.SpawnPoints.Count > 0 ? Map.SpawnPoints[m_BeachRng.Range(0, Map.SpawnPoints.Count)] : new Vector3(0f, 0.01f, 0f);
            }

            c.Motion = PlayerState.SpawnAt(position, c.Motion.Yaw);
            c.Life = LifeState.Alive;
            c.Stats = new SurvivalStats
            {
                Health = Rules.Death.RespawnHealth,
                Hunger = Rules.Death.RespawnHunger,
                Thirst = Rules.Death.RespawnThirst,
                Warmth = Clock.AmbientWarmth(Rules.Temperature),
            };
            if (Config.StarterKit)
            {
                foreach (ItemAmount kit in Rules.Death.StarterKit)
                {
                    c.Inventory.Add(kit.Item, kit.Amount);
                }
            }

            c.StateVersion++;
            c.Events.Add(new GameEvent { Kind = GameEventKind.Respawned, Target = (int)bedrollId, Position = position });
            return ActionError.None;
        }

        /// <summary>Bedrolls this character owns (for the respawn screen).</summary>
        public void RespawnPointsOf(Character c, List<WorldEntity> results)
        {
            results.Clear();
            foreach (WorldEntity e in Entities.All)
            {
                if (e.Def.IsRespawnPoint && e.OwnerId == c.AccountId)
                {
                    results.Add(e);
                }
            }
        }

        // ---------------------------------------------------------------- sleepers

        void SpawnSleeper(Character c)
        {
            RemoveSleeper(c);
            WorldEntity sleeper = Entities.Spawn(Content.Entity("entity.sleeper"), c.Motion.Position, c.Motion.Yaw);
            sleeper.Label = c.Name;
            sleeper.OwnerId = c.AccountId;
            Entities.MarkChanged(sleeper);
            c.SleeperEntityId = sleeper.Id;
        }

        void RemoveSleeper(Character c)
        {
            if (c.SleeperEntityId != 0)
            {
                Entities.Remove(c.SleeperEntityId);
                c.SleeperEntityId = 0;
            }
        }

        /// <summary>After loading a save: characters that were in the world (alive or downed) sleep where they were.</summary>
        public void RestoreSleeper(Character c)
        {
            if (!c.Online && c.Life != LifeState.Dead && c.SleeperEntityId == 0)
            {
                SpawnSleeper(c);
            }
        }

        void UpdateSleepers()
        {
            if (Config.SleeperMode != SleeperMode.Vanish)
            {
                return;
            }

            foreach (Character c in m_CharacterList)
            {
                if (!c.Online && c.SleeperEntityId != 0 && Now - c.OfflineSince >= Rules.World.VanishAfterSeconds)
                {
                    RemoveSleeper(c); // the character itself stays saved and comes back on login
                }
            }
        }

        /// <summary>
        /// Whether a sleeping character can be hurt. Claims arrive in M4: until then "protected in claim"
        /// protects nobody, exactly like "vulnerable"; vanished sleepers can't be reached at all.
        /// </summary>
        public bool SleeperCanBeDamaged(Character c) => !c.Online && c.SleeperEntityId != 0;
    }
}
