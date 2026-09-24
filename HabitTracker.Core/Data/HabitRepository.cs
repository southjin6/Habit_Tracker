using System.Data;
using HabitTracker.Core.Backup;
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

    public async Task<BackupFile> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);

        // Both reads in one transaction. MySQL's default isolation is already REPEATABLE READ, but
        // under autocommit that default applies per statement, so two reads are two points in time: an
        // unmark committed in between would leave an item in the file with fewer days than the database
        // held when the export started, and a file keeps that. The snapshot is taken at the first read,
        // so one explicit transaction is what makes the export a single point in time.
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);

        // Same ordering the lists use, so the file reads like the window.
        var items = await db.Items
            .AsNoTracking()
            .OrderBy(i => i.Kind)
            .ThenBy(i => i.CreatedOn)
            .ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);

        var completionRows = await db.Completions
            .AsNoTracking()
            .Select(c => new { c.ItemId, c.Date })
            .ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        var daysByItem = completionRows
            .GroupBy(r => r.ItemId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Date).OrderBy(d => d).ToList());

        return new BackupFile
        {
            ExportedOn = Utc8Clock.Today,
            Items = items.Select(item => new BackupItem
            {
                Name = item.Name,
                Kind = item.Kind,
                CreatedOn = item.CreatedOn,
                CompletedOn = item.CompletedOn,
                Completions = daysByItem.TryGetValue(item.Id, out var days) ? days : new List<DateOnly>(),
            }).ToList(),
        };
    }

    public async Task<ImportResult> ReplaceAllAsync(
        BackupFile backup,
        CancellationToken cancellationToken = default)
    {
        var problems = backup.Validate(Utc8Clock.Today);
        if (problems.Count > 0)
        {
            // The caller is expected to validate before asking for a replace, so reaching this point
            // means a document was built in memory and never checked. Throwing here keeps a bad file
            // from being able to empty the database on its way past a missing check.
            throw new InvalidOperationException(problems[0]);
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Completions first. The FK cascades from Items, so this is not strictly needed, but clearing
        // both tables explicitly means the restore does not depend on how the server walks the
        // cascade — and it stays inside the same transaction either way.
        await db.Completions.ExecuteDeleteAsync(cancellationToken);
        await db.Items.ExecuteDeleteAsync(cancellationToken);

        var completionsWritten = 0;

        foreach (var entry in backup.Items)
        {
            var inserted = await db.Items.AddAsync(
                new Item
                {
                    Name = entry.Name.Trim(),
                    Kind = entry.Kind,
                    CreatedOn = entry.CreatedOn,
                    CompletedOn = entry.Kind == ItemKind.Task ? entry.CompletedOn : null,
                },
                cancellationToken);

            // Saved per item because its auto-generated id is needed to attach its own completion
            // days; a personal habit database has tens of items, not thousands.
            await db.SaveChangesAsync(cancellationToken);

            if (entry.Kind != ItemKind.Daily)
            {
                continue;
            }

            var days = entry.Completions.Distinct().ToList();
            db.Completions.AddRange(days.Select(date => new Completion
            {
                ItemId = inserted.Entity.Id,
                Date = date,
            }));
            completionsWritten += days.Count;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ImportResult(backup.Items.Count, completionsWritten);
    }

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
