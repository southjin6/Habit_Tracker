using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HabitTracker.Core.Backup;
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

        // Measured after trimming, because a trimmed name is what gets stored, so padding alone must
        // not push a name over the limit.
        var length = name.Trim().Length;
        if (length > Item.NameMaxLength)
        {
            // Refused here rather than by the column: MySQL rejects the row with a driver message and
            // the catch below would echo the whole paste back in the banner.
            ReportStatus($"That name is {length} characters; the limit is {Item.NameMaxLength}.");
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

    /// <summary>
    /// A backup file that has been read and accepted, paired with what is currently stored so the
    /// confirmation can name both sides of a replace. Created by <see cref="PreviewImportAsync"/> and
    /// spent by <see cref="CommitImportAsync"/>.
    /// </summary>
    public sealed record ImportPlan(BackupFile Backup, string FileName, int CurrentItems, int CurrentCompletions);

    /// <summary>
    /// Writes the whole saved state to <paramref name="path"/>. No confirmation and no database
    /// changes: an export cannot damage the thing it reads.
    /// </summary>
    public async Task ExportAsync(string path)
    {
        try
        {
            var backup = await _repository.CreateBackupAsync();
            await File.WriteAllTextAsync(path, backup.ToJson());

            var days = backup.Items.Sum(i => i.Completions.Count);
            ReportStatus(
                $"Exported {backup.Items.Count} item{(backup.Items.Count == 1 ? "" : "s")} " +
                $"({days} completed {(days == 1 ? "day" : "days")}) to {Path.GetFileName(path)}.");
        }
        catch (Exception ex)
        {
            ReportStatus($"Could not write the export: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads and checks a backup without writing anything, so the view can ask about a replace before
    /// anything is lost. Reports its own failure on the banner and returns null.
    /// </summary>
    public async Task<ImportPlan?> PreviewImportAsync(string path)
    {
        var fileName = Path.GetFileName(path);

        BackupFile backup;
        IReadOnlyList<string> problems;
        try
        {
            var parsed = BackupFile.TryParse(await File.ReadAllTextAsync(path));
            if (parsed.Backup is null)
            {
                ReportStatus($"Will not import {fileName}: {parsed.Failure}");
                return null;
            }

            backup = parsed.Backup;

            // Deliberately inside the try. This runs from an async void click handler and the app has
            // no DispatcherUnhandledException handler, so an exception raised here would end the
            // process instead of putting a message on the banner. Validate reports every problem it
            // knows about as a problem, so reaching this catch means a file shape nobody anticipated.
            problems = backup.Validate(Utc8Clock.Today);
        }
        catch (Exception ex)
        {
            ReportStatus($"Could not read {fileName}: {ex.Message}");
            return null;
        }

        if (problems.Count > 0)
        {
            // Every problem is listed because a file is restored as a whole: fixing one at a time
            // across re-import attempts would be slower than seeing the rest up front.
            ReportStatus(problems.Count == 1
                ? $"Will not import {fileName}: {problems[0]}"
                : $"Will not import {fileName}: {problems[0]} (and {problems.Count - 1} more problem{(problems.Count - 2 == 0 ? "" : "s")}.)");
            return null;
        }

        // Counted from MySQL, not from _allItems: the cache is only as fresh as the last successful
        // read, so a failed read left the prompt claiming "0 items, 0 completed days" — telling the
        // user there was nothing to lose while the database still held everything. Refusing beats
        // asking, because a replace must not be confirmed without knowing what it deletes.
        int currentItems;
        int currentCompletions;
        try
        {
            var stored = await _repository.CreateBackupAsync();
            currentItems = stored.Items.Count;
            currentCompletions = stored.Items.Sum(i => i.Completions.Count);
        }
        catch (Exception ex)
        {
            ReportStatus($"Could not check what is stored in MySQL, so nothing was imported: {ex.Message}");
            return null;
        }

        return new ImportPlan(backup, fileName, currentItems, currentCompletions);
    }

    /// <summary>
    /// Carries out an already-confirmed replace, then re-reads so the lists show what is now stored.
    /// The repository does the swap in one transaction, so a failure leaves the previous data intact
    /// and says so.
    /// </summary>
    public async Task CommitImportAsync(ImportPlan plan)
    {
        try
        {
            var result = await _repository.ReplaceAllAsync(plan.Backup);
            var reloadError = await RefreshAsync();

            ReportStatus(reloadError is null
                ? $"Imported {result.Items} item{(result.Items == 1 ? "" : "s")} " +
                  $"({result.Completions} completed {(result.Completions == 1 ? "day" : "days")}) from {plan.FileName}."
                : $"Imported from {plan.FileName} — but the list did not reload. {reloadError}");
        }
        catch (Exception ex)
        {
            // Refresh either way: the transaction rolled back, and the window should show the data
            // that survived rather than whatever was on screen a moment ago.
            await RefreshAsync();
            ReportStatus($"Could not import {plan.FileName}, so nothing was changed: {ex.Message}");
        }
    }
}
