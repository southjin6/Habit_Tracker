using HabitTracker.Core.Backup;
using HabitTracker.Core.Data;
using HabitTracker.Core.Domain;

namespace HabitTracker.App.Tests;

/// <summary>
/// In-memory stand-in for <see cref="HabitRepository"/>, mirroring the behaviour the view models
/// depend on: reads return copies rather than live objects, a daily's completion is idempotent per
/// day, and backdating is bounded by the item's creation day.
///
/// <see cref="FailReads"/> and <see cref="FailWrites"/> exist because the distinction between the two
/// is the whole content of the status banner: a write that lands while the reload fails must not be
/// reported as a clean success, and a write that fails must not be erased by the reload that follows.
/// Against real MySQL those states were only reachable by renaming tables out from under a running
/// app, which is not something a test suite can repeat.
/// </summary>
public class FakeHabitRepository : IHabitRepository
{
    private readonly List<Item> _items = new();
    private readonly Dictionary<int, HashSet<DateOnly>> _dates = new();
    private int _nextId;

    public int Reads { get; private set; }

    public int Writes { get; private set; }

    public bool FailReads { get; set; }

    public bool FailWrites { get; set; }

    public string ReadFailure { get; set; } = "Table 'habit_tracker.completions' doesn't exist";

    public string WriteFailure { get; set; } = "the write was refused";

    public static DateOnly Today => Utc8Clock.Today;

    /// <summary>The UTC+8 calendar day <paramref name="daysAgo"/> before today, so 0 is today.</summary>
    public static DateOnly Day(int daysAgo) => Today.AddDays(-daysAgo);

    public Item SeedDaily(DateOnly createdOn, params DateOnly[] completions) =>
        Seed(new Item { Name = "Seeded daily", Kind = ItemKind.Daily, CreatedOn = createdOn }, completions);

    public Item SeedTask(DateOnly? completedOn = null) =>
        Seed(new Item
        {
            Name = "Seeded task",
            Kind = ItemKind.Task,
            CreatedOn = Day(30),
            CompletedOn = completedOn,
        });

    private Item Seed(Item item, params DateOnly[] completions)
    {
        item.Id = ++_nextId;
        _items.Add(item);
        _dates[item.Id] = completions.ToHashSet();
        return item;
    }

    /// <summary>The stored completion days for one item, for asserting a write really landed.</summary>
    public IReadOnlyCollection<DateOnly> DatesFor(int itemId) => _dates.TryGetValue(itemId, out var d)
        ? d
        : Array.Empty<DateOnly>();

    public Task<IReadOnlyList<Item>> GetItemsAsync(CancellationToken cancellationToken = default)
    {
        Reads++;
        if (FailReads)
        {
            throw new InvalidOperationException(ReadFailure);
        }

        IReadOnlyList<Item> copy = _items
            .OrderBy(i => i.Kind)
            .ThenBy(i => i.CreatedOn)
            .ThenBy(i => i.Id)
            .Select(Copy)
            .ToList();

        return Task.FromResult(copy);
    }

    public Task<Dictionary<int, HashSet<DateOnly>>> GetCompletionDatesAsync(
        CancellationToken cancellationToken = default)
    {
        Reads++;
        if (FailReads)
        {
            throw new InvalidOperationException(ReadFailure);
        }

        return Task.FromResult(_dates.ToDictionary(e => e.Key, e => e.Value.ToHashSet()));
    }

    public Task<Item> AddItemAsync(string name, ItemKind kind, CancellationToken cancellationToken = default)
    {
        Writes++;
        if (FailWrites)
        {
            throw new InvalidOperationException(WriteFailure);
        }

        var item = Seed(new Item { Name = name.Trim(), Kind = kind, CreatedOn = Today });
        return Task.FromResult(Copy(item));
    }

    public Task DeleteItemAsync(int itemId, CancellationToken cancellationToken = default)
    {
        Writes++;
        if (FailWrites)
        {
            throw new InvalidOperationException(WriteFailure);
        }

        _items.RemoveAll(i => i.Id == itemId);
        _dates.Remove(itemId);
        return Task.CompletedTask;
    }

    public Task MarkDailyCompleteAsync(int itemId, DateOnly date, CancellationToken cancellationToken = default)
    {
        Writes++;
        if (FailWrites)
        {
            throw new InvalidOperationException(WriteFailure);
        }

        var item = Require(itemId);
        if (item.Kind != ItemKind.Daily)
        {
            throw new InvalidOperationException($"\"{item.Name}\" is a task; tasks have no daily completions.");
        }

        if (date < item.CreatedOn)
        {
            throw new ArgumentOutOfRangeException(
                nameof(date),
                $"\"{item.Name}\" was created on {item.CreatedOn:yyyy-MM-dd}; " +
                "a completion cannot be recorded before that day.");
        }

        if (date > Today)
        {
            throw new ArgumentOutOfRangeException(
                nameof(date),
                $"A completion cannot be recorded for a future day (today is {Today:yyyy-MM-dd} in UTC+8).");
        }

        // Idempotent, like the real one backed by the unique (ItemId, Date) index.
        _dates[itemId].Add(date);
        return Task.CompletedTask;
    }

    public Task UnmarkDailyCompleteAsync(int itemId, DateOnly date, CancellationToken cancellationToken = default)
    {
        Writes++;
        if (FailWrites)
        {
            throw new InvalidOperationException(WriteFailure);
        }

        Require(itemId);
        _dates[itemId].Remove(date);
        return Task.CompletedTask;
    }

    public Task SetTaskCompletedAsync(int itemId, bool completed, CancellationToken cancellationToken = default)
    {
        Writes++;
        if (FailWrites)
        {
            throw new InvalidOperationException(WriteFailure);
        }

        var item = Require(itemId);
        if (item.Kind != ItemKind.Task)
        {
            throw new InvalidOperationException($"\"{item.Name}\" is a daily, not a task.");
        }

        item.CompletedOn = completed ? Today : null;
        return Task.CompletedTask;
    }

    public Task<BackupFile> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        Reads++;
        if (FailReads)
        {
            throw new InvalidOperationException(ReadFailure);
        }

        return Task.FromResult(new BackupFile
        {
            ExportedOn = Today,
            Items = _items
                .OrderBy(i => i.Kind)
                .ThenBy(i => i.CreatedOn)
                .ThenBy(i => i.Id)
                .Select(i => new BackupItem
                {
                    Name = i.Name,
                    Kind = i.Kind,
                    CreatedOn = i.CreatedOn,
                    CompletedOn = i.CompletedOn,
                    Completions = _dates.TryGetValue(i.Id, out var d)
                        ? d.OrderBy(x => x).ToList()
                        : new List<DateOnly>(),
                })
                .ToList(),
        });
    }

    public Task<ImportResult> ReplaceAllAsync(BackupFile backup, CancellationToken cancellationToken = default)
    {
        Writes++;
        if (FailWrites)
        {
            throw new InvalidOperationException(WriteFailure);
        }

        var problems = backup.Validate(Today);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(problems[0]);
        }

        _items.Clear();
        _dates.Clear();

        var days = 0;
        foreach (var entry in backup.Items)
        {
            // Ids keep increasing rather than restarting, standing in for an auto-increment column
            // handing the restored rows fresh ones.
            var item = Seed(
                new Item
                {
                    Name = entry.Name.Trim(),
                    Kind = entry.Kind,
                    CreatedOn = entry.CreatedOn,
                    CompletedOn = entry.Kind == ItemKind.Task ? entry.CompletedOn : null,
                },
                entry.Kind == ItemKind.Daily ? entry.Completions.Distinct().ToArray() : Array.Empty<DateOnly>());

            days += _dates[item.Id].Count;
        }

        return Task.FromResult(new ImportResult(backup.Items.Count, days));
    }

    private Item Require(int itemId) =>
        _items.FirstOrDefault(i => i.Id == itemId)
        ?? throw new InvalidOperationException($"Item {itemId} was not found.");

    private static Item Copy(Item item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Kind = item.Kind,
        CreatedOn = item.CreatedOn,
        CompletedOn = item.CompletedOn,
    };
}
