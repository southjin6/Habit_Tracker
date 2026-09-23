using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HabitTracker.Core.Data;
using HabitTracker.Core.Domain;

namespace HabitTracker.App.ViewModels;

/// <summary>
/// Display state for one item, rebuilt from stored history on every refresh. Because the streak is
/// always derived rather than cached, there is nothing here that can drift out of sync with MySQL.
/// </summary>
public partial class ItemViewModel : ObservableObject
{
    private readonly IHabitRepository _repository;
    private readonly MainViewModel _owner;
    private readonly DateOnly _today;

    public ItemViewModel(
        Item item,
        HashSet<DateOnly> completions,
        DateOnly today,
        IHabitRepository repository,
        MainViewModel owner)
    {
        _repository = repository;
        _owner = owner;
        _today = today;

        Id = item.Id;
        Name = item.Name;
        Kind = item.Kind;
        CreatedOn = item.CreatedOn;
        CompletionDates = completions.OrderByDescending(d => d).ToList();

        if (item.IsDaily)
        {
            var info = StreakCalculator.Evaluate(completions, item.CreatedOn, today);
            Streak = info.Streak;
            MissedDays = info.MissedDays;
            Flag = info.Flag;
            IsDone = completions.Contains(today);
            StatusText = DescribeDaily(info);
        }
        else
        {
            // Tasks are excluded from streak tracking and are never flagged as missed.
            Streak = 0;
            MissedDays = 0;
            Flag = ItemFlag.OnTrack;
            IsDone = item.CompletedOn is not null;
            StatusText = IsDone ? $"Completed {item.CompletedOn:yyyy-MM-dd}" : "Pending";
        }
    }

    public int Id { get; }
    public string Name { get; }
    public ItemKind Kind { get; }
    public DateOnly CreatedOn { get; }
    public bool IsDaily => Kind == ItemKind.Daily;
    public bool IsTask => Kind == ItemKind.Task;

    /// <summary>For a daily: completed today. For a task: completed at all.</summary>
    public bool IsDone { get; }

    public int Streak { get; }
    public int MissedDays { get; }
    public ItemFlag Flag { get; }
    public string StatusText { get; }
    public IReadOnlyList<DateOnly> CompletionDates { get; }

    public string CheckboxToolTip => IsDaily
        ? (IsDone ? "Undo today's completion" : "Mark done for today")
        : (IsDone ? "Mark this objective as not done" : "Mark this objective as done");

    [RelayCommand]
    private async Task ToggleDoneAsync()
    {
        try
        {
            if (IsDaily)
            {
                if (IsDone)
                {
                    await _repository.UnmarkDailyCompleteAsync(Id, _today);
                }
                else
                {
                    await _repository.MarkDailyCompleteAsync(Id, _today);
                }
            }
            else
            {
                await _repository.SetTaskCompletedAsync(Id, !IsDone);
            }

            await _owner.RefreshAsync();
        }
        catch (Exception ex)
        {
            // Refresh anyway, so the checkbox snaps back to what the database actually holds
            // instead of showing a toggle that never landed. Reported after the refresh because a
            // successful read clears the banner, which would otherwise erase this error.
            await _owner.RefreshAsync();
            _owner.ReportStatus($"Could not update \"{Name}\": {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenHistory() => _owner.RequestHistory(this);

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var consequence = IsDaily
            ? "Deleting it also permanently deletes its whole completion history and streak."
            : "Deleting it cannot be undone.";

        var confirmed = MessageBox.Show(
            $"Delete \"{Name}\"?{Environment.NewLine}{Environment.NewLine}{consequence}",
            "Delete item",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _repository.DeleteItemAsync(Id);
            // Report after the refresh, and only as a clean success if it re-read: the row is
            // genuinely gone even when the reload fails, so the banner must not claim either more
            // or less than that.
            var reloadError = await _owner.RefreshAsync();
            _owner.ReportStatus(reloadError is null
                ? $"Deleted \"{Name}\"."
                : $"Deleted \"{Name}\" — but the list did not reload. {reloadError}");
        }
        catch (Exception ex)
        {
            _owner.ReportStatus($"Could not delete \"{Name}\": {ex.Message}");
        }
    }

    private static string DescribeDaily(StreakInfo info) => info.Flag switch
    {
        ItemFlag.Missed => $"Missed {info.MissedDays} days",
        ItemFlag.AtRisk => "Missed yesterday — streak broken",
        _ when info.Streak == 1 => "1-day streak",
        _ when info.Streak > 1 => $"{info.Streak}-day streak",
        _ => "No streak yet",
    };
}
