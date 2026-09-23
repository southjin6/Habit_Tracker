using HabitTracker.Core.Domain;

namespace HabitTracker.Core.Data;

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
}
