namespace HabitTracker.Core.Domain;

/// <summary>
/// One completed day for one daily. The unique (ItemId, Date) index in the database makes a
/// second completion for the same day impossible, so double-marking cannot inflate a streak.
/// </summary>
public class Completion
{
    public int Id { get; set; }

    public int ItemId { get; set; }

    /// <summary>Calendar day completed, in UTC+8.</summary>
    public DateOnly Date { get; set; }

    public Item? Item { get; set; }
}
