namespace Kindling.Simulation
{
    /// <summary>Why a player action was refused. Sent to the client so the UI can explain.</summary>
    public enum ActionError : byte
    {
        None = 0,
        InvalidSlot,
        EmptySlot,
        InvalidAmount,
        SlotRestricted,
        NotEnoughSpace,
        NotEnoughItems,
        NotUsable,
        NoAccess,
        TooFar,
        UnknownRecipe,
        StationMissing,
        NotKnown,
        QueueFull,
        InvalidPlacement,
        Cooldown,
        Dead,
        Downed,
        NothingThere,
        NotPermitted,
        InvalidTarget,
    }
}
