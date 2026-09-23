namespace HabitTracker.Core.Domain;

/// <summary>
/// Resolves "today" against a fixed UTC+8 offset rather than a named timezone or the machine's
/// local clock. UTC+8 observes no daylight saving, so a day is always exactly 24 hours and no
/// instant can land on an ambiguous or repeated local time.
/// </summary>
public static class Utc8Clock
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(8);

    public static DateOnly Today => FromInstant(DateTime.UtcNow);

    /// <summary>The UTC+8 calendar day a given UTC instant falls on.</summary>
    public static DateOnly FromInstant(DateTime utcInstant) =>
        DateOnly.FromDateTime(utcInstant.Add(Offset));
}
