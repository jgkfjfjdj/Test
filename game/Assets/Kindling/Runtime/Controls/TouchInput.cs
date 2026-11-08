using System.Numerics;

namespace Kindling.Controls
{
    public enum TouchPhase : byte
    {
        Began,
        Moved,
        Stationary,
        Ended,
        Canceled,
    }

    /// <summary>One finger's state this frame, in screen points (origin bottom-left).</summary>
    public struct TouchInput
    {
        public int FingerId;
        public Vector2 Position;
        public TouchPhase Phase;

        public TouchInput(int fingerId, Vector2 position, TouchPhase phase)
        {
            FingerId = fingerId;
            Position = position;
            Phase = phase;
        }
    }
}
