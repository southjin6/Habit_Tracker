using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HabitTracker.Core.Data;
using HabitTracker.Core.Domain;

namespace HabitTracker.App.ViewModels;

/// <summary>
/// Backdating for one daily. Recording a past day recomputes the streak from history, so filling a
/// gap repairs an already-broken streak; removing a day recomputes it back down.
/// </summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly IHabitRepository _repository;
    private readonly ItemViewModel _item;

    public HistoryViewModel(ItemViewModel item, IHabitRepository repository)
    {
        _item = item;
        _repository = repository;
        Title = $"History — {item.Name}";
        CreatedOnText = $"Created {item.CreatedOn:yyyy-MM-dd} · completions cannot be recorded before that day";
        MinimumDate = item.CreatedOn.ToDateTime(TimeOnly.MinValue);
        MaximumDate = Utc8Clock.Today.ToDateTime(TimeOnly.MinValue);
        SelectedDate = MaximumDate;
    }

    public string Title { get; }
    public string CreatedOnText { get; }
    public DateTime MinimumDate { get; }
    public DateTime MaximumDate { get; }
    public ObservableCollection<DateOnly> Entries { get; } = new();

    [ObservableProperty] private DateTime? _selectedDate;
    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>
    /// Re-reads the recorded days. Returns <c>null</c> when the list on screen is what MySQL holds,
    /// otherwise the banner text it has just written — the same contract as
    /// <see cref="MainViewModel.RefreshAsync"/>, so a caller can tell a failed reload from a
    /// successful one instead of reporting a clean success over a stale list.
    ///
    /// Never throws. The dialog calls this from its Loaded handler, which is <c>async void</c> on the
    /// dispatcher and has no <c>DispatcherUnhandledException</c> handler above it, so an exception
    /// here would end the process instead of putting a message on the banner.
    /// </summary>
    public async Task<string?> LoadAsync()
    {
        try
        {
            var allDates = await _repository.GetCompletionDatesAsync();
            var dates = allDates.TryGetValue(_item.Id, out var found)
                ? found
                : new HashSet<DateOnly>();

            Entries.Clear();
            foreach (var date in dates.OrderByDescending(d => d))
            {
                Entries.Add(date);
            }

            return null;
        }
        catch (Exception ex)
        {
            // Whatever is already listed stays: those are the last days MySQL was known to hold, and
            // clearing them would turn an outage into "this habit has no history".
            return StatusMessage = $"Could not load the history: {ex.Message}";
        }
    }

    [RelayCommand]
    private Task RecordAsync() => MutateAsync(markComplete: true);

    [RelayCommand]
    private Task RemoveAsync(DateOnly date)
    {
        SelectedDate = date.ToDateTime(TimeOnly.MinValue);
        return MutateAsync(markComplete: false);
    }

    private async Task MutateAsync(bool markComplete)
    {
        if (SelectedDate is null)
        {
            StatusMessage = "Pick a date first.";
            return;
        }

        var date = DateOnly.FromDateTime(SelectedDate.Value);

        try
        {
            if (markComplete)
            {
                await _repository.MarkDailyCompleteAsync(_item.Id, date);
            }
            else
            {
                await _repository.UnmarkDailyCompleteAsync(_item.Id, date);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            return;
        }

        var done = markComplete
            ? $"{date:yyyy-MM-dd} recorded — streak recomputed"
            : $"{date:yyyy-MM-dd} removed — streak recomputed";

        // The write landed, so a bare error would be a lie; the list on screen is stale, so a bare
        // success would be one too. Both facts go in the sentence.
        var reloadProblem = await LoadAsync();
        StatusMessage = reloadProblem is null
            ? $"{done}."
            : $"{done}, but the list did not reload. {reloadProblem}";
    }
}
