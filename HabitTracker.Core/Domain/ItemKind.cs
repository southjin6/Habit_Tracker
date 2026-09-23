namespace HabitTracker.Core.Domain;

/// <summary>Repeating daily habit, or a one-off objective.</summary>
public enum ItemKind : byte
{
    Daily = 0,
    Task = 1,
}
