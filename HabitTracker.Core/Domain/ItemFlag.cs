namespace HabitTracker.Core.Domain;

/// <summary>
/// Per-item visual state for a daily, driven by how many consecutive days immediately
/// before today went uncompleted. Strictly per item — one daily's state never affects another.
/// </summary>
public enum ItemFlag : byte
{
    /// <summary>No missed days.</summary>
    OnTrack = 0,

    /// <summary>Exactly one missed day: the streak is broken, but the item is not red.</summary>
    AtRisk = 1,

    /// <summary>Two or more consecutive missed days: the item is red.</summary>
    Missed = 2,
}
