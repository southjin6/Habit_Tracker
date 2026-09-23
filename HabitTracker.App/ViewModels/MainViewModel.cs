using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HabitTracker.Core.Data;
using HabitTracker.Core.Domain;

namespace HabitTracker.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IHabitRepository _repository;
    private readonly List<ItemViewModel> _allItems = new();

    public MainViewModel(IHabitRepository repository)
    {
        _repository = repository;
        UpdateTodayText();
    }

    public ObservableCollection<ItemViewModel> Dailies { get; } = new();
    public ObservableCollection<ItemViewModel> Tasks { get; } = new();

    /// <summary>Raised so the view can open the backdating dialog; keeps window creation out of the VM.</summary>
    public event Action<ItemViewModel>? HistoryRequested;

    [ObservableProperty] private string _newDailyName = string.Empty;
    [ObservableProperty] private string _newTaskName = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _todayText = string.Empty;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private DailyFilter _dailyFilter = DailyFilter.All;
    [ObservableProperty] private TaskFilter _taskFilter = TaskFilter.Active;

    partial void OnSearchTextChanged(string value) => ApplyFilters();

    partial void OnDailyFilterChanged(DailyFilter value) => ApplyFilters();

    partial void OnTaskFilterChanged(TaskFilter value) => ApplyFilters();

    /// <summary>
    /// Re-reads everything from MySQL and rebuilds both lists. Missed-day state is computed here on
    /// read, so streaks stay correct even if the app was closed for days — no scheduled job needed.
    /// A successful read clears <see cref="StatusMessage"/>, so callers that want their own message
    /// shown must report it <em>after</em> awaiting this, not before.
    /// Returns null when the read succeeded, otherwise the message that went on the banner. A caller
    /// that has just written to the database needs that distinction: its write landed even though the
    /// reload did not, so a bare success line would hide a failure and a bare error would lie the
    /// other way.
    /// </summary>
    public async Task<string?> RefreshAsync()
    {
        try
        {
            UpdateTodayText();

            var items = await _repository.GetItemsAsync();
            var completionDates = await _repository.GetCompletionDatesAsync();
            var today = Utc8Clock.Today;

            _allItems.Clear();

            foreach (var item in items)
            {
                var completions = completionDates.TryGetValue(item.Id, out var dates)
                    ? dates
                    : new HashSet<DateOnly>();

                _allItems.Add(new ItemViewModel(item, completions, today, _repository, this));
            }

            ApplyFilters();

            // A good read replaces whatever the banner was showing, so an error from an earlier
            // outage cannot outlive the outage. Callers that report their own outcome refresh
            // before reporting, so this never erases a message they still mean to show.
            StatusMessage = string.Empty;
            return null;
        }
        catch (Exception ex)
        {
            var message = $"Could not load from MySQL: {ex.Message}";
            ReportStatus(message);
            return message;
        }
    }

    [RelayCommand]
    private Task AddDailyAsync() => AddItemAsync(NewDailyName, ItemKind.Daily, () => NewDailyName = string.Empty);

    [RelayCommand]
    private Task AddTaskAsync() => AddItemAsync(NewTaskName, ItemKind.Task, () => NewTaskName = string.Empty);

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    internal void RequestHistory(ItemViewModel item) => HistoryRequested?.Invoke(item);

    internal void ReportStatus(string message) => StatusMessage = message;

    private void ApplyFilters()
    {
        var query = SearchText.Trim();

        Repopulate(Dailies, _allItems.Where(i => i.IsDaily && MatchesDaily(i) && MatchesSearch(i, query)));
        Repopulate(Tasks, _allItems.Where(i => i.IsTask && MatchesTask(i) && MatchesSearch(i, query)));
    }

    private bool MatchesDaily(ItemViewModel item) => DailyFilter switch
    {
        DailyFilter.Due => !item.IsDone,
        DailyFilter.NotDue => item.IsDone,
        _ => true,
    };

    private bool MatchesTask(ItemViewModel item) => TaskFilter switch
    {
        TaskFilter.Active => !item.IsDone,
        TaskFilter.Complete => item.IsDone,
        _ => true,
    };

    private static bool MatchesSearch(ItemViewModel item, string query) =>
        query.Length == 0 || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static void Repopulate(ObservableCollection<ItemViewModel> target, IEnumerable<ItemViewModel> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private async Task AddItemAsync(string name, ItemKind kind, Action clearInput)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ReportStatus("Enter a name first.");
            return;
        }

        try
        {
            var item = await _repository.AddItemAsync(name, kind);
            clearInput();
            var reloadError = await RefreshAsync();
            ReportStatus(reloadError is null
                ? $"Added \"{item.Name}\"."
                : $"Added \"{item.Name}\" — but the list did not reload. {reloadError}");
        }
        catch (Exception ex)
        {
            ReportStatus($"Could not add \"{name.Trim()}\": {ex.Message}");
        }
    }

    private void UpdateTodayText() => TodayText = $"{Utc8Clock.Today:dddd, d MMMM yyyy}  ·  UTC+8";
}
