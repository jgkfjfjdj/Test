using System;

namespace Kindling.Core.Numerics
{
    /// <summary>
    /// Fixed-point encoding of positions for the network and save formats
    /// (docs/blueprint/05-multiplayer-networking.md §5.1):
    /// chunk-local X/Z and world Y are stored as 16-bit values with 1 cm resolution.
    /// Round-trip error is at most half a step (0.5 cm) for in-range values.
    /// </summary>
    public static class PositionQuantizer
    {
        /// <summary>Metres per quantization step.</summary>
        public const float Step = 0.01f;

        /// <summary>Lowest encodable world height (metres).</summary>
        public const float MinY = -128f;

        /// <summary>Highest encodable world height (metres): MinY + 65535 steps.</summary>
        public const float MaxY = MinY + ushort.MaxValue * Step;

        /// <summary>Largest chunk size whose local coordinates fit in 16 bits at <see cref="Step"/>.</summary>
        public const float MaxChunkSize = ushort.MaxValue * Step;

        /// <summary>Encodes a chunk-local coordinate in [0, chunkSize]. Values outside are clamped.</summary>
        public static ushort EncodeLocal(float local, float chunkSize)
        {
            if (!(chunkSize > 0) || chunkSize > MaxChunkSize)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize), chunkSize, $"Chunk size must be in (0, {MaxChunkSize}].");
            }

            RequireFinite(local, nameof(local));
            return ToSteps(Clamp(local, 0f, chunkSize));
        }

        public static float DecodeLocal(ushort encoded) => encoded * Step;

        /// <summary>Encodes a world height. Values outside [MinY, MaxY] are clamped.</summary>
        public static ushort EncodeY(float y)
        {
            RequireFinite(y, nameof(y));
            return ToSteps(Clamp(y, MinY, MaxY) - MinY);
        }

        public static float DecodeY(ushort encoded) => MinY + encoded * Step;

        static ushort ToSteps(float metresFromOrigin)
        {
            double steps = Math.Round(metresFromOrigin / (double)Step, MidpointRounding.AwayFromZero);
            if (steps < 0)
            {
                return 0;
            }

            return steps > ushort.MaxValue ? ushort.MaxValue : (ushort)steps;
        }

        static float Clamp(float value, float min, float max) => value < min ? min : (value > max ? max : value);

        static void RequireFinite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentException("Value must be finite.", name);
            }
        }
    }
}
