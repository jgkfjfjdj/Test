using System;
using System.Numerics;
using Kindling.Net.Protocol;

namespace Kindling.Net.Client
{
    /// <summary>Recent snapshots of one remote player, sampled at a render tick (snapshot interpolation).</summary>
    public sealed class RemoteEntityBuffer
    {
        const int Capacity = 32;

        readonly uint[] m_Ticks = new uint[Capacity];
        readonly EntitySnapshot[] m_Samples = new EntitySnapshot[Capacity];
        int m_Count;
        int m_Head; // index of the newest sample

        public double LastUpdateTime { get; private set; }

        public uint NewestTick => m_Count > 0 ? m_Ticks[m_Head] : 0;

        public int Count => m_Count;

        public void Add(uint tick, in EntitySnapshot sample, double localTime)
        {
            LastUpdateTime = localTime;
            if (m_Count > 0)
            {
                uint newest = m_Ticks[m_Head];
                if (tick == newest)
                {
                    m_Samples[m_Head] = sample;
                    return;
                }

                if (tick < newest)
                {
                    return; // late part of an old tick
                }
            }

            m_Head = (m_Head + 1) % Capacity;
            m_Ticks[m_Head] = tick;
            m_Samples[m_Head] = sample;
            m_Count = Math.Min(m_Count + 1, Capacity);
        }

        /// <summary>Interpolates at a (fractional) server tick. Holds the newest sample past the end.</summary>
        public EntitySnapshot Sample(double renderTick)
        {
            if (m_Count == 0)
            {
                return default;
            }

            int newer = m_Head;
            for (int i = 1; i < m_Count; i++)
            {
                int older = (m_Head - i + Capacity) % Capacity;
                if (m_Ticks[older] <= renderTick)
                {
                    if (m_Ticks[newer] <= renderTick)
                    {
                        return m_Samples[newer];
                    }

                    ref readonly EntitySnapshot a = ref m_Samples[older];
                    ref readonly EntitySnapshot b = ref m_Samples[newer];
                    float t = (float)((renderTick - m_Ticks[older]) / (m_Ticks[newer] - m_Ticks[older]));
                    return new EntitySnapshot
                    {
                        Id = b.Id,
                        Position = Vector3.Lerp(a.Position, b.Position, t),
                        Yaw = LerpAngle(a.Yaw, b.Yaw, t),
                        Pitch = a.Pitch + (b.Pitch - a.Pitch) * t,
                        Flags = t < 0.5f ? a.Flags : b.Flags,
                    };
                }

                newer = older;
            }

            return m_Samples[newer]; // render tick is older than everything we have
        }

        static float LerpAngle(float a, float b, float t)
        {
            float delta = ((b - a) % 360f + 540f) % 360f - 180f;
            float result = a + delta * t;
            return result < 0f ? result + 360f : (result >= 360f ? result - 360f : result);
        }
    }
}
