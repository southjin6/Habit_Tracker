using HabitTracker.App.ViewModels;
using HabitTracker.Core.Domain;

namespace HabitTracker.App.Tests;

/// <summary>
/// The spec rules as the user sees them, asserted where they are actually assembled.
///
/// <see cref="StreakCalculator"/> is already covered on its own in HabitTracker.Tests, but it only
/// produces a number and a flag. Whether a task ever turns red, what wording a given state produces,
/// and whether one daily's state can bleed into another are decided in the view models, which the
/// plain net8.0 test project cannot reference. That is the gap this file exists to close.
/// </summary>
public class SpecRuleTests
{
    private static (FakeHabitRepository Repo, MainViewModel Vm) Open(
        params (DateOnly CreatedOn, DateOnly[] Completions)[] dailies)
    {
        var repo = new FakeHabitRepository();
        foreach (var (createdOn, completions) in dailies)
        {
            repo.SeedDaily(createdOn, completions);
        }

        var vm = new MainViewModel(repo);
        return (repo, vm);
    }

    /// <summary>Four completed days, created long enough ago that only yesterday and the day before are gaps.</summary>
    private static DateOnly[] ThroughDay2() =>
        [FakeHabitRepository.Day(6), FakeHabitRepository.Day(5), FakeHabitRepository.Day(4), FakeHabitRepository.Day(3)];

    [Fact]
    public async Task ATaskThatIsOldAndStillOpen_IsNeverFlaggedAsMissed()
    {
        var (repo, vm) = Open();
        repo.SeedTask();
        await vm.RefreshAsync();

        var task = Assert.Single(vm.Tasks);

        // The whole missed-day walk is a daily concept; a task sitting undone for 30 days must stay
        // neutral, which is why the non-daily branch does not consult the calculator at all.
        Assert.Equal(ItemFlag.OnTrack, task.Flag);
        Assert.Equal(0, task.MissedDays);
        Assert.Equal(0, task.Streak);
        Assert.False(task.IsDone);
        Assert.Equal("Pending", task.StatusText);
    }

    [Fact]
    public async Task ACompletedTask_ReportsTheDayItWasDone_AndIsStillNotFlagged()
    {
        var (repo, vm) = Open();
        repo.SeedTask(FakeHabitRepository.Day(1));
        await vm.RefreshAsync();

        // The To Do column defaults to Active, so a finished one disappears rather than sitting
        // there looking undone.
        Assert.Empty(vm.Tasks);
        vm.TaskFilter = TaskFilter.Complete;

        var task = Assert.Single(vm.Tasks);

        Assert.True(task.IsDone);
        Assert.Equal(ItemFlag.OnTrack, task.Flag);
        Assert.Equal($"Completed {FakeHabitRepository.Day(1):yyyy-MM-dd}", task.StatusText);
    }

    [Fact]
    public async Task OneMissedDay_IsAmberAndBreaksTheStreak_ButIsNotRed()
    {
        var (_, vm) = Open((FakeHabitRepository.Day(6), [.. ThroughDay2().Take(3).Append(FakeHabitRepository.Day(2))]));
        await vm.RefreshAsync();

        var daily = Assert.Single(vm.Dailies);

        Assert.Equal(1, daily.MissedDays);
        Assert.Equal(ItemFlag.AtRisk, daily.Flag);
        Assert.NotEqual(ItemFlag.Missed, daily.Flag);
        Assert.Equal(0, daily.Streak);
        Assert.Equal("Missed yesterday — streak broken", daily.StatusText);
    }

    [Fact]
    public async Task TwoMissedDays_IsRed_AndNamesTheCount()
    {
        var (_, vm) = Open((FakeHabitRepository.Day(6), ThroughDay2()));
        await vm.RefreshAsync();

        var daily = Assert.Single(vm.Dailies);

        Assert.Equal(2, daily.MissedDays);
        Assert.Equal(ItemFlag.Missed, daily.Flag);
        Assert.Equal("Missed 2 days", daily.StatusText);
    }

    [Fact]
    public async Task EachDailyCarriesItsOwnState_WithoutAffectingTheOthers()
    {
        var (_, vm) = Open(
            (FakeHabitRepository.Day(6), ThroughDay2()),
            (FakeHabitRepository.Day(6), [.. ThroughDay2().Append(FakeHabitRepository.Day(2))]),
            (FakeHabitRepository.Day(6), [.. ThroughDay2().Append(FakeHabitRepository.Day(2)), FakeHabitRepository.Day(1)]));
        await vm.RefreshAsync();

        Assert.Collection(
            vm.Dailies.OrderBy(d => d.Id),
            red => Assert.Equal(ItemFlag.Missed, red.Flag),
            amber => Assert.Equal(ItemFlag.AtRisk, amber.Flag),
            green =>
            {
                Assert.Equal(ItemFlag.OnTrack, green.Flag);
                Assert.Equal(6, green.Streak);
                Assert.Equal("6-day streak", green.StatusText);
            });
    }

    [Fact]
    public async Task BackdatingTheGap_RecomputesARedDailyAllTheWayBackToOnTrack()
    {
        var (repo, vm) = Open((FakeHabitRepository.Day(6), ThroughDay2()));
        await vm.RefreshAsync();
        Assert.Equal(ItemFlag.Missed, Assert.Single(vm.Dailies).Flag);

        var id = vm.Dailies[0].Id;
        await repo.MarkDailyCompleteAsync(id, FakeHabitRepository.Day(2));
        await repo.MarkDailyCompleteAsync(id, FakeHabitRepository.Day(1));
        await vm.RefreshAsync();

        var repaired = Assert.Single(vm.Dailies);

        // Nothing is stored about the streak, so filling two past days is enough to undo the red and
        // rebuild the six-day run — no repair pass, no counter to resynchronise.
        Assert.Equal(ItemFlag.OnTrack, repaired.Flag);
        Assert.Equal(0, repaired.MissedDays);
        Assert.Equal(6, repaired.Streak);
    }

    [Fact]
    public async Task RemovingABackdatedDay_RecomputesTheStreakBackDown()
    {
        var (repo, vm) = Open((FakeHabitRepository.Day(6), [.. ThroughDay2(), FakeHabitRepository.Day(2), FakeHabitRepository.Day(1)]));
        await vm.RefreshAsync();
        var id = vm.Dailies[0].Id;
        Assert.Equal(6, vm.Dailies[0].Streak);

        await repo.UnmarkDailyCompleteAsync(id, FakeHabitRepository.Day(1));
        await vm.RefreshAsync();

        var after = Assert.Single(vm.Dailies);
        Assert.Equal(ItemFlag.AtRisk, after.Flag);
        Assert.Equal(0, after.Streak);
    }

    [Fact]
    public async Task MarkingOneDayTwice_CannotAdvanceTheStreakTwice()
    {
        var (repo, vm) = Open((FakeHabitRepository.Day(5), []));
        await vm.RefreshAsync();
        var id = vm.Dailies[0].Id;

        await repo.MarkDailyCompleteAsync(id, FakeHabitRepository.Day(1));
        await repo.MarkDailyCompleteAsync(id, FakeHabitRepository.Day(1));
        await vm.RefreshAsync();

        var daily = Assert.Single(vm.Dailies);
        Assert.Equal(1, daily.Streak);
        Assert.Single(repo.DatesFor(id));
    }

    [Fact]
    public Task MarkingADayBeforeTheDailyExisted_IsRejected()
    {
        var (repo, _) = Open();
        var seeded = repo.SeedDaily(FakeHabitRepository.Day(2));

        return Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => repo.MarkDailyCompleteAsync(seeded.Id, FakeHabitRepository.Day(3)));
    }

    [Fact]
    public async Task ADailyCreatedToday_HasNoStreakYetAndIsNotAtRisk()
    {
        var (_, vm) = Open((FakeHabitRepository.Today, []));
        await vm.RefreshAsync();

        var daily = Assert.Single(vm.Dailies);

        Assert.Equal(ItemFlag.OnTrack, daily.Flag);
        Assert.Equal(0, daily.MissedDays);
        Assert.Equal("No streak yet", daily.StatusText);
    }

    [Fact]
    public async Task RecordedDays_AreListedMostRecentFirst()
    {
        var (_, vm) = Open((FakeHabitRepository.Day(9),
            [FakeHabitRepository.Day(4), FakeHabitRepository.Day(1), FakeHabitRepository.Day(2)]));
        await vm.RefreshAsync();

        var daily = Assert.Single(vm.Dailies);

        Assert.Equal(
            new[] { FakeHabitRepository.Day(1), FakeHabitRepository.Day(2), FakeHabitRepository.Day(4) },
            daily.CompletionDates);
    }

    [Fact]
    public async Task DailiesAndTasks_NeverShareAColumn()
    {
        var (repo, vm) = Open((FakeHabitRepository.Day(1), []));
        repo.SeedTask();
        await vm.RefreshAsync();

        var daily = Assert.Single(vm.Dailies);
        var task = Assert.Single(vm.Tasks);

        Assert.True(daily.IsDaily);
        Assert.False(daily.IsTask);
        Assert.True(task.IsTask);
    }

    [Fact]
    public async Task TheDueFilter_HidesOnlyWhatIsAlreadyDoneToday()
    {
        var (repo, vm) = Open(
            (FakeHabitRepository.Day(6), ThroughDay2()),
            (FakeHabitRepository.Day(3), [FakeHabitRepository.Day(2), FakeHabitRepository.Today]));
        await vm.RefreshAsync();
        Assert.Equal(2, vm.Dailies.Count);

        vm.DailyFilter = DailyFilter.Due;

        var due = Assert.Single(vm.Dailies);
        Assert.False(due.IsDone);
        Assert.Equal(ItemFlag.Missed, due.Flag);
    }

    [Fact]
    public async Task TodayIsNamedByItsUtcPlus8CalendarDay()
    {
        var (_, vm) = Open();
        await vm.RefreshAsync();

        Assert.Contains(FakeHabitRepository.Today.ToString("dddd, d MMMM yyyy"), vm.TodayText);
        Assert.EndsWith("UTC+8", vm.TodayText);
    }
}
