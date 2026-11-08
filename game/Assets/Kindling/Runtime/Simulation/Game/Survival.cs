using System;
using Kindling.Content;
using Kindling.Simulation.World;

namespace Kindling.Simulation.Game
{
    public sealed partial class GameWorld
    {
        /// <summary>Hunger, thirst, warmth, status effects, damage over time and regeneration for one online character.</summary>
        void UpdateSurvival(Character c, float dt)
        {
            if (c.Life == LifeState.Dead)
            {
                return;
            }

            if (c.Life == LifeState.Downed)
            {
                c.DownedLeft -= dt;
                if (c.DownedLeft <= 0)
                {
                    Die(c, c.LastDamage);
                }

                return;
            }

            SurvivalRules r = Rules.Survival;
            SurvivalStats s = c.Stats;
            float minutes = dt / 60f;
            StatusEffect before = s.Effects;

            // Warmth: environment + clothing + nearby fires; moves gradually toward that target.
            float heat = HeatAt(c.Motion.Position);
            float target = Clock.AmbientWarmth(Rules.Temperature) + c.Inventory.Insulation + heat;
            target = Math.Max(-100f, Math.Min(100f, target));
            float step = r.WarmthChangePerMinute * minutes;
            s.Warmth = s.Warmth < target ? Math.Min(target, s.Warmth + step) : Math.Max(target, s.Warmth - step);
            s.Set(StatusEffect.Cold, s.Warmth < r.ComfortMin);
            s.Set(StatusEffect.Hot, s.Warmth > r.ComfortMax);
            s.Set(StatusEffect.Comfort, heat > 0);

            float hungerRate = r.HungerPerMinute;
            float thirstRate = r.ThirstPerMinute;
            if (c.Motion.Sprinting)
            {
                hungerRate *= r.SprintDrainMultiplier;
                thirstRate *= r.SprintDrainMultiplier;
            }

            if (s.Has(StatusEffect.Cold))
            {
                hungerRate *= r.ColdHungerMultiplier;
            }

            if (s.Has(StatusEffect.Hot))
            {
                thirstRate *= r.HotThirstMultiplier;
            }

            s.Hunger = Math.Max(0f, s.Hunger - hungerRate * minutes);
            s.Thirst = Math.Max(0f, s.Thirst - thirstRate * minutes);
            s.Set(StatusEffect.WellFed, s.Hunger >= r.WellFedThreshold && s.Thirst >= r.WellFedThreshold);

            if (s.PoisonLeft > 0)
            {
                s.PoisonLeft = Math.Max(0f, s.PoisonLeft - dt);
            }

            s.Set(StatusEffect.Poisoned, s.PoisonLeft > 0);

            // Damage over time (the worst cause is remembered for the death screen).
            float damage = 0f;
            DamageCause cause = DamageCause.None;
            void Hurt(float amount, DamageCause why)
            {
                if (amount > damage)
                {
                    cause = why;
                }

                damage += amount;
            }

            if (s.Hunger <= 0)
            {
                Hurt(r.StarvationDamagePerMinute * minutes, DamageCause.Starvation);
            }

            if (s.Thirst <= 0)
            {
                Hurt(r.DehydrationDamagePerMinute * minutes, DamageCause.Dehydration);
            }

            if (s.Has(StatusEffect.Cold))
            {
                float severity = Math.Min(2f, (r.ComfortMin - s.Warmth) / 20f);
                Hurt(r.ColdDamagePerMinute * severity * minutes, DamageCause.Cold);
            }

            if (s.Has(StatusEffect.Bleeding))
            {
                Hurt(r.BleedingDamagePerMinute * minutes, DamageCause.Bleeding);
            }

            if (s.Has(StatusEffect.Poisoned))
            {
                Hurt(r.PoisonDamagePerMinute * minutes, DamageCause.Poison);
            }

            float heal = 0f;
            if (s.HealLeft > 0)
            {
                float h = Math.Min(s.HealLeft, s.HealRate * dt);
                s.HealLeft -= h;
                heal += h;
            }

            bool canRegen = s.Hunger >= r.RegenMinHunger && s.Thirst >= r.RegenMinThirst && !s.Has(StatusEffect.Bleeding) && !s.Has(StatusEffect.Poisoned) && damage <= 0;
            if (canRegen)
            {
                float rate = r.HealthRegenPerMinute;
                if (s.Has(StatusEffect.WellFed))
                {
                    rate += r.WellFedRegenBonusPerMinute;
                }

                if (s.Has(StatusEffect.Comfort))
                {
                    rate += r.ComfortRegenBonusPerMinute;
                }

                heal += rate * minutes;
            }

            s.Health = Math.Min(r.MaxHealth, s.Health + heal);
            if (damage > 0)
            {
                Damage(c, damage, cause);
            }

            if (s.Effects != before)
            {
                c.StateVersion++;
            }
        }

        /// <summary>Warmth from lit fires within their heat radius.</summary>
        public float HeatAt(System.Numerics.Vector3 position)
        {
            float heat = 0f;
            Entities.Query(position, 6f, m_EntityScratch, e => e.IsLit && e.Def.Heat > 0);
            foreach (WorldEntity e in m_EntityScratch)
            {
                float dx = e.Position.X - position.X;
                float dz = e.Position.Z - position.Z;
                if (dx * dx + dz * dz <= e.Def.HeatRadius * e.Def.HeatRadius)
                {
                    heat = Math.Max(heat, e.Def.Heat);
                }
            }

            return heat;
        }
    }
}
