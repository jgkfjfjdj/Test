using System;
using System.Numerics;

namespace Kindling.Controls
{
    /// <summary>Axis-aligned rectangle in screen points (origin bottom-left, like Unity screen space).</summary>
    public readonly struct ScreenRect : IEquatable<ScreenRect>
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Width;
        public readonly float Height;

        public ScreenRect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public static ScreenRect FromCenter(Vector2 center, float size) => new ScreenRect(center.X - size / 2, center.Y - size / 2, size, size);

        public float XMax => X + Width;

        public float YMax => Y + Height;

        public Vector2 Center => new Vector2(X + Width / 2, Y + Height / 2);

        public bool Contains(Vector2 p) => p.X >= X && p.X <= XMax && p.Y >= Y && p.Y <= YMax;

        public bool Overlaps(in ScreenRect o) => X < o.XMax && o.X < XMax && Y < o.YMax && o.Y < YMax;

        /// <summary>True if this rectangle lies completely inside <paramref name="outer"/>.</summary>
        public bool Inside(in ScreenRect outer) => X >= outer.X && Y >= outer.Y && XMax <= outer.XMax && YMax <= outer.YMax;

        public bool Equals(ScreenRect o) => X == o.X && Y == o.Y && Width == o.Width && Height == o.Height;

        public override bool Equals(object obj) => obj is ScreenRect o && Equals(o);

        public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

        public override string ToString() => $"({X:0.#}, {Y:0.#}, {Width:0.#}×{Height:0.#})";
    }
}
