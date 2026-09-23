using HabitTracker.Core.Domain;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace HabitTracker.Core.Data;

/// <summary>
/// All data access. Every operation opens its own context from the factory and disposes it before
/// returning, because the app is a long-running WPF process where two overlapping async chains are
/// normal — clicking two cards quickly, or a toggle racing the startup refresh. A shared context
/// would abort one of them mid-flight and silently lose that write.
/// </summary>
public class HabitRepository : IHabitRepository
{
    private readonly IDbContextFactory<HabitDbContext> _factory;

    public HabitRepository(IDbContextFactory<HabitDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<Item>> GetItemsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);

        return await db.Items
            .AsNoTracking()
            .OrderBy(i => i.Kind)
            .ThenBy(i => i.CreatedOn)
            .ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<int, HashSet<DateOnly>>> GetCompletionDatesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);

        var rows = await db.Completions
            .AsNoTracking()
            .Select(c => new { c.ItemId, c.Date })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.ItemId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Date).ToHashSet());
    }

    /// <summary>Returns the new item detached; its generated Id and set properties are already populated.</summary>
    public async Task<Item> AddItemAsync(
        string name,
        ItemKind kind,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);

        var item = new Item
        {
            Name = name.Trim(),
            Kind = kind,
            CreatedOn = Utc8Clock.Today,
        };

        db.Items.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return item;
    }

    public async Task DeleteItemAsync(int itemId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);

        // A plain DELETE; MySQL's ON DELETE CASCADE removes the item's Completions rows.
        await db.Items
            .Where(i => i.Id == itemId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task MarkDailyCompleteAsync(
        int itemId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);

        var item = await LoadForCompletionAsync(db, itemId, cancellationToken);
        ValidateDateRange(item, date);

        var alreadyRecorded = await db.Completions
            .AnyAsync(c => c.ItemId == itemId && c.Date == date, cancellationToken);
        if (alreadyRecorded)
        {
            return;
        }

        db.Completions.Add(new Completion { ItemId = itemId, Date = date });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            // Another chain inserted the same day after the pre-check above. The unique index on
            // (ItemId, Date) is what actually guarantees one completion per day, so this is the
            // no-op the contract promises rather than a failure to report.
        }
    }

    public async Task UnmarkDailyCompleteAsync(
        int itemId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);

        await db.Completions
            .Where(c => c.ItemId == itemId && c.Date == date)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task SetTaskCompletedAsync(
        int itemId,
        bool completed,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);

        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken)
                   ?? throw new InvalidOperationException($"Item {itemId} was not found.");

        if (item.Kind != ItemKind.Task)
        {
            throw new InvalidOperationException($"\"{item.Name}\" is a daily, not a task.");
        }

        item.CompletedOn = completed ? Utc8Clock.Today : null;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool IsDuplicateKey(DbUpdateException exception) =>
        exception.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry };

    private static async Task<Item> LoadForCompletionAsync(
        HabitDbContext db,
        int itemId,
        CancellationToken cancellationToken) =>
        await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken)
        ?? throw new InvalidOperationException($"Item {itemId} was not found.");

    private static void ValidateDateRange(Item item, DateOnly date)
    {
        if (item.Kind != ItemKind.Daily)
        {
            throw new InvalidOperationException($"\"{item.Name}\" is a task; tasks have no daily completions.");
        }

        var today = Utc8Clock.Today;

        if (date < item.CreatedOn)
        {
            throw new ArgumentOutOfRangeException(
                nameof(date),
                $"\"{item.Name}\" was created on {item.CreatedOn:yyyy-MM-dd}; " +
                "a completion cannot be recorded before that day.");
        }

        if (date > today)
        {
            throw new ArgumentOutOfRangeException(
                nameof(date),
                $"A completion cannot be recorded for a future day (today is {today:yyyy-MM-dd} in UTC+8).");
        }
    }
}
