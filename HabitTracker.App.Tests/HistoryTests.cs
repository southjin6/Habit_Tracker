using HabitTracker.App.ViewModels;

namespace HabitTracker.App.Tests;

/// <summary>
/// The History dialog's banner. The same outage the main window reports as a message used to take
/// the whole process down here, because the dialog loads from a <c>Loaded</c> handler —
/// <c>async void</c> on the dispatcher with no <c>DispatcherUnhandledException</c> handler above it.
/// So these tests pin the two halves of that: the load reports rather than throws, and a reload that
/// fails after a write actually landed is not passed off as a clean success.
/// </summary>
public class HistoryTests
{
    private static async Task<(FakeHabitRepository Repo, ItemViewModel Daily)> SetupAsync()
    {
        var repo = new FakeHabitRepository();
        var main = new MainViewModel(repo);
        repo.SeedDaily(FakeHabitRepository.Day(5), FakeHabitRepository.Day(3), FakeHabitRepository.Day(1));
        await main.RefreshAsync();
        return (repo, main.Dailies.Single());
    }

    [Fact]
    public async Task Load_WhenMySQLIsDown_ReportsItInsteadOfThrowing_AndKeepsTheShownDays()
    {
        var (repo, daily) = await SetupAsync();
        var history = new HistoryViewModel(daily, repo);
        await history.LoadAsync();
        Assert.Equal(2, history.Entries.Count);

        repo.FailReads = true;
        var problem = await history.LoadAsync();

        // Pre-fix this line threw, and the exception left through the dialog's Loaded handler.
        Assert.NotNull(problem);
        Assert.Contains("Could not load the history", history.StatusMessage);
        Assert.Contains(repo.ReadFailure, history.StatusMessage);

        // An outage is not "this habit has no history", so the days already listed stay.
        Assert.Equal(2, history.Entries.Count);
    }

    [Fact]
    public async Task Record_WhenTheWriteLandsButTheReloadFails_ReportsBoth()
    {
        var (repo, daily) = await SetupAsync();
        var history = new HistoryViewModel(daily, repo);
        await history.LoadAsync();

        repo.FailReads = true;
        history.SelectedDate = FakeHabitRepository.Day(2).ToDateTime(TimeOnly.MinValue);
        await history.RecordCommand.ExecuteAsync(null);

        // The write went in, which is why the error cannot stand alone...
        Assert.Contains(FakeHabitRepository.Day(2), repo.DatesFor(daily.Id));

        // ...and the day is missing from the list, which is why the success cannot stand alone either.
        Assert.Equal(2, history.Entries.Count);
        Assert.Contains("recorded", history.StatusMessage);
        Assert.Contains("did not reload", history.StatusMessage);
        Assert.Contains(repo.ReadFailure, history.StatusMessage);
    }

    [Fact]
    public async Task Remove_WhenTheWriteFails_ReportsTheWriteError()
    {
        var (repo, daily) = await SetupAsync();
        var history = new HistoryViewModel(daily, repo);
        await history.LoadAsync();

        repo.FailWrites = true;
        await history.RemoveCommand.ExecuteAsync(FakeHabitRepository.Day(3));

        Assert.Contains(repo.WriteFailure, history.StatusMessage);
        Assert.DoesNotContain("removed", history.StatusMessage);
        Assert.Equal(2, history.Entries.Count);
    }

    [Fact]
    public async Task Record_WithNoDatePicked_SaysSoAndWritesNothing()
    {
        var (repo, daily) = await SetupAsync();
        var history = new HistoryViewModel(daily, repo);
        history.SelectedDate = null;

        await history.RecordCommand.ExecuteAsync(null);

        Assert.Equal("Pick a date first.", history.StatusMessage);
        Assert.Equal(2, repo.DatesFor(daily.Id).Count);
    }
}
