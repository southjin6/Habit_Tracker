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

    public async Task LoadAsync()
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

            await LoadAsync();
            StatusMessage = markComplete
                ? $"{date:yyyy-MM-dd} recorded — streak recomputed."
                : $"{date:yyyy-MM-dd} removed — streak recomputed.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }
}
