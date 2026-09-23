namespace HabitTracker.Core.Domain;

public class Item
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public ItemKind Kind { get; set; }

    /// <summary>Calendar day the item was created, in UTC+8. Bounds how far back missed days count.</summary>
    public DateOnly CreatedOn { get; set; }

    /// <summary>Tasks only: the day the one-off objective was completed. Null means still pending.</summary>
    public DateOnly? CompletedOn { get; set; }

    public List<Completion> Completions { get; set; } = new();

    public bool IsDaily => Kind == ItemKind.Daily;

    public bool IsTask => Kind == ItemKind.Task;
}
