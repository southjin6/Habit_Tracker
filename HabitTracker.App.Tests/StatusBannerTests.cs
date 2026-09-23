using HabitTracker.App.ViewModels;

namespace HabitTracker.App.Tests;

/// <summary>
/// Status banner semantics. Both rules here were found by audit and fixed under live observation, and
/// both are exactly the kind of bug that reads fine in code: the banner is the app's only feedback, so
/// a message that outlives its cause is as misleading as one that never appears.
/// </summary>
public class StatusBannerTests
{
    private static (FakeHabitRepository Repo, MainViewModel Vm) Setup()
    {
        var repo = new FakeHabitRepository();
        return (repo, new MainViewModel(repo));
    }

    [Fact]
    public async Task FailedRead_ReportsTheError_AndLeavesTheShownCardsAlone()
    {
        var (repo, vm) = Setup();
        repo.SeedDaily(FakeHabitRepository.Day(3));
        await vm.RefreshAsync();
        Assert.Single(vm.Dailies);

        repo.FailReads = true;
        var error = await vm.RefreshAsync();

        Assert.NotNull(error);
        Assert.Contains("Could not load from MySQL", vm.StatusMessage);
        Assert.Contains(repo.ReadFailure, vm.StatusMessage);

        // The rebuild only happens after both reads succeed, so an outage never blanks the screen.
        Assert.Single(vm.Dailies);
    }

    [Fact]
    public async Task GoodRead_ClearsTheErrorLeftByAnEarlierOutage()
    {
        var (repo, vm) = Setup();
        repo.FailReads = true;
        await vm.RefreshAsync();
        Assert.Contains("Could not load from MySQL", vm.StatusMessage);

        repo.FailReads = false;
        await vm.RefreshAsync();

        // This is the whole of finding 2: without the clear, the app is working and still says it is not.
        Assert.Equal(string.Empty, vm.StatusMessage);
    }

    [Fact]
    public async Task AddDaily_WithAFailedReload_DoesNotReportACleanSuccess()
    {
        var (repo, vm) = Setup();
        vm.NewDailyName = "Meditate";
        repo.FailReads = true;

        await vm.AddDailyCommand.ExecuteAsync(null);

        // The insert landed, so "could not add" would be a lie; the list is stale, so "added" alone is
        // too. The banner has to carry both halves.
        Assert.Contains("Added \"Meditate\"", vm.StatusMessage);
        Assert.Contains("did not reload", vm.StatusMessage);
        Assert.Contains(repo.ReadFailure, vm.StatusMessage);
    }

    [Fact]
    public async Task AddDaily_WithAWorkingReload_ReportsSuccessWithoutTheCaveat()
    {
        var (repo, vm) = Setup();
        vm.NewDailyName = "  Meditate  ";

        await vm.AddDailyCommand.ExecuteAsync(null);

        Assert.Equal("Added \"Meditate\".", vm.StatusMessage);
        Assert.Single(vm.Dailies);
        Assert.Empty(repo.DatesFor(1));
    }

    [Fact]
    public async Task AddDaily_WithABlankName_PromptsAndWritesNothing()
    {
        var (repo, vm) = Setup();
        vm.NewDailyName = "   ";

        await vm.AddDailyCommand.ExecuteAsync(null);

        Assert.Equal("Enter a name first.", vm.StatusMessage);
        Assert.Equal(0, repo.Writes);
    }

    [Fact]
    public async Task FailedWrite_StillReportsItsOwnError_AfterTheRefreshThatFollows()
    {
        var (repo, vm) = Setup();
        repo.SeedDaily(FakeHabitRepository.Day(3));
        await vm.RefreshAsync();

        repo.FailWrites = true;
        await vm.Dailies[0].ToggleDoneCommand.ExecuteAsync(null);

        // The refresh inside the catch path succeeds and clears the banner, so reporting has to come
        // afterwards or the user's failed click would appear to have done nothing at all.
        Assert.StartsWith("Could not update", vm.StatusMessage);
        Assert.Contains(repo.WriteFailure, vm.StatusMessage);
        Assert.DoesNotContain("Could not load", vm.StatusMessage);
    }

    [Fact]
    public async Task FailedWrite_RevertsTheCheckboxToWhatTheDatabaseActuallyHolds()
    {
        var (repo, vm) = Setup();
        var seeded = repo.SeedDaily(FakeHabitRepository.Day(3));
        await vm.RefreshAsync();
        Assert.False(vm.Dailies[0].IsDone);

        repo.FailWrites = true;
        await vm.Dailies[0].ToggleDoneCommand.ExecuteAsync(null);

        Assert.False(vm.Dailies[0].IsDone);
        Assert.Empty(repo.DatesFor(seeded.Id));
    }

    [Fact]
    public async Task SuccessfulToggle_ClearsAnEarlierError()
    {
        var (repo, vm) = Setup();
        var seeded = repo.SeedDaily(FakeHabitRepository.Day(3));
        await vm.RefreshAsync();
        vm.StatusMessage = "Could not load from MySQL: an older outage";

        await vm.Dailies[0].ToggleDoneCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.StatusMessage);
        Assert.Contains(FakeHabitRepository.Today, repo.DatesFor(seeded.Id));
    }
}
