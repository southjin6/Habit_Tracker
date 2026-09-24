using HabitTracker.Core.Backup;
using HabitTracker.Core.Domain;

namespace HabitTracker.Core.Data;

/// <summary>How much of a backup actually reached the database.</summary>
public sealed record ImportResult(int Items, int Completions);

public interface IHabitRepository
{
    Task<IReadOnlyList<Item>> GetItemsAsync(CancellationToken cancellationToken = default);

    /// <summary>Completion dates for every daily, keyed by item id, in one query.</summary>
    Task<Dictionary<int, HashSet<DateOnly>>> GetCompletionDatesAsync(CancellationToken cancellationToken = default);

    Task<Item> AddItemAsync(string name, ItemKind kind, CancellationToken cancellationToken = default);

    Task DeleteItemAsync(int itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a completion for a daily on a UTC+8 calendar day. Idempotent: marking a day that is
    /// already complete is a no-op, so it cannot advance a streak twice. The database's unique
    /// (ItemId, Date) index is what guarantees this, even against a concurrent write.
    /// </summary>
    Task MarkDailyCompleteAsync(int itemId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>Removes a completion, which recomputes the streak back down on the next read.</summary>
    Task UnmarkDailyCompleteAsync(int itemId, DateOnly date, CancellationToken cancellationToken = default);

    Task SetTaskCompletedAsync(int itemId, bool completed, CancellationToken cancellationToken = default);

    /// <summary>
    /// The entire saved state — every item and every completion day — as one document. Nothing here
    /// is derived: streaks and missed-day counts are recomputed from these dates on read, so a
    /// restore that only writes these numbers rebuilds the display state by itself.
    /// </summary>
    Task<BackupFile> CreateBackupAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces <em>everything</em> currently stored with the document's contents: both tables are
    /// emptied and repopulated inside one transaction, so a failure midway leaves the old data
    /// untouched rather than half-restored. Items take new ids, since the ids in a file belong to
    /// whichever database wrote it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The document is not restorable; nothing is changed.</exception>
    Task<ImportResult> ReplaceAllAsync(BackupFile backup, CancellationToken cancellationToken = default);
}
