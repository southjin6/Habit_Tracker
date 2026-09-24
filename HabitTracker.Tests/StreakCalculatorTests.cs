using HabitTracker.Core.Domain;

namespace HabitTracker.Tests;

/// <summary>
/// Covers the streak, missed-day and backdating acceptance criteria. Everything here is pure —
/// no database, no clock — because StreakCalculator takes `today` as an argument.
/// </summary>
public class StreakCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 9, 22);

    // Far enough back that it never bounds the missed-day walk, except where a test overrides it.
    private static readonly DateOnly CreatedLongAgo = Today.AddDays(-365);

    /// <summary>Builds a completion set from day offsets back from Today (0 = today, 1 = yesterday).</summary>
    private static HashSet<DateOnly> Days(params int[] offsetsBackFromToday) =>
        offsetsBackFromToday.Select(offset => Today.AddDays(-offset)).ToHashSet();

    private static StreakInfo Evaluate(
        HashSet<DateOnly> completions,
        DateOnly? createdOn = null) =>
        StreakCalculator.Evaluate(completions, createdOn ?? CreatedLongAgo, Today);

    // ---------------------------------------------------------------- streak

    [Fact]
    public void Streak_IsZero_WhenNothingHasBeenCompleted()
    {
        Assert.Equal(0, StreakCalculator.ComputeStreak(Days(), Today));
    }

    [Fact]
    public void Streak_IsOne_WhenOnlyTodayIsCompleted()
    {
        Assert.Equal(1, StreakCalculator.ComputeStreak(Days(0), Today));
    }

    [Fact]
    public void Streak_IncrementsByExactlyOnePerConsecutiveDay()
    {
        Assert.Equal(1, StreakCalculator.ComputeStreak(Days(0), Today));
        Assert.Equal(2, StreakCalculator.ComputeStreak(Days(0, 1), Today));
        Assert.Equal(3, StreakCalculator.ComputeStreak(Days(0, 1, 2), Today));
        Assert.Equal(4, StreakCalculator.ComputeStreak(Days(0, 1, 2, 3), Today));
    }

    [Fact]
    public void Streak_CountsALongUnbrokenRun()
    {
        var thirtyDays = Enumerable.Range(0, 30).ToArray();
        Assert.Equal(30, StreakCalculator.ComputeStreak(Days(thirtyDays), Today));
    }

    [Fact]
    public void Streak_StopsAtTheFirstGap()
    {
        // Today and yesterday done, the day before missed, then older completions that must not count.
        Assert.Equal(2, StreakCalculator.ComputeStreak(Days(0, 1, 3, 4), Today));
    }

    [Fact]
    public void Streak_SurvivesTheMorningBeforeTodaysCompletion()
    {
        // Nothing recorded for today yet, but yesterday was done: the streak must still show.
        Assert.Equal(3, StreakCalculator.ComputeStreak(Days(1, 2, 3), Today));
    }

    [Fact]
    public void Streak_BreaksToOneMissedDay()
    {
        // Yesterday missed, so today is not an anchor and neither is yesterday.
        Assert.Equal(0, StreakCalculator.ComputeStreak(Days(2, 3, 4), Today));
    }

    [Fact]
    public void Streak_IsZero_WhenTheLastCompletionWasTwoDaysAgo()
    {
        Assert.Equal(0, StreakCalculator.ComputeStreak(Days(2), Today));
    }

    [Fact]
    public void Streak_RestartsAtOne_AfterABreak_RatherThanResumingTheOldValue()
    {
        // An old 2-day run, a gap, then today only. Must be 1, not 3.
        Assert.Equal(1, StreakCalculator.ComputeStreak(Days(0, 5, 6), Today));
    }

    // ------------------------------------------------------- missed days/flag

    [Fact]
    public void MissedDays_IsZero_WhenYesterdayWasCompleted()
    {
        var info = Evaluate(Days(0, 1));

        Assert.Equal(0, info.MissedDays);
        Assert.Equal(ItemFlag.OnTrack, info.Flag);
    }

    [Fact]
    public void OneMissedDay_IsAtRisk_AndIsNotRed()
    {
        // Last completion two days ago: yesterday is the single missed day.
        var info = Evaluate(Days(2, 3));

        Assert.Equal(1, info.MissedDays);
        Assert.Equal(ItemFlag.AtRisk, info.Flag);
        Assert.NotEqual(ItemFlag.Missed, info.Flag);
    }

    [Fact]
    public void TwoMissedDays_IsRed()
    {
        // Last completion three days ago: yesterday and the day before are both missed.
        var info = Evaluate(Days(3, 4));

        Assert.Equal(2, info.MissedDays);
        Assert.Equal(ItemFlag.Missed, info.Flag);
    }

    [Fact]
    public void ManyMissedDays_StaysRed()
    {
        // The only completion is 10 days back, so days 1 through 9 before today are all missed.
        var info = Evaluate(Days(10));

        Assert.Equal(9, info.MissedDays);
        Assert.Equal(ItemFlag.Missed, info.Flag);
    }

    [Fact]
    public void TheOneVersusTwoMissedBoundary_IsExact()
    {
        Assert.Equal(ItemFlag.AtRisk, Evaluate(Days(2)).Flag);   // exactly one missed day
        Assert.Equal(ItemFlag.Missed, Evaluate(Days(3)).Flag);   // exactly two missed days
    }

    [Fact]
    public void CompletingToday_AfterMissingYesterday_StillCountsAsOneMissedDay()
    {
        // Recovered today, but yesterday went unrecorded: at risk, not red, and the streak restarts.
        var info = Evaluate(Days(0, 2, 3));

        Assert.Equal(1, info.Streak);
        Assert.Equal(1, info.MissedDays);
        Assert.Equal(ItemFlag.AtRisk, info.Flag);
    }

    // ------------------------------------------------- creation-day boundaries

    [Fact]
    public void ADailyCreatedToday_IsNeverTreatedAsMissed()
    {
        var info = Evaluate(Days(), createdOn: Today);

        Assert.Equal(0, info.MissedDays);
        Assert.Equal(0, info.Streak);
        Assert.Equal(ItemFlag.OnTrack, info.Flag);
    }

    [Fact]
    public void ADailyCreatedYesterday_AndNeverCompleted_IsAtRiskNotRed()
    {
        var info = Evaluate(Days(), createdOn: Today.AddDays(-1));

        Assert.Equal(1, info.MissedDays);
        Assert.Equal(ItemFlag.AtRisk, info.Flag);
    }

    [Fact]
    public void MissedDays_NeverCountsDaysBeforeTheItemExisted()
    {
        // Created three days ago with nothing completed: days 1, 2 and 3 count, but nothing earlier.
        var info = Evaluate(Days(), createdOn: Today.AddDays(-3));

        Assert.Equal(3, info.MissedDays);
    }

    [Fact]
    public void MissedDays_StopsAtTheMostRecentCompletion()
    {
        // Created long ago; only the two days immediately before today are missed.
        var info = Evaluate(Days(3), createdOn: Today.AddDays(-30));

        Assert.Equal(2, info.MissedDays);
        Assert.Equal(ItemFlag.Missed, info.Flag);
    }

    [Fact]
    public void TheEarliestRepresentableCreationDay_EndsTheWalkInsteadOfThrowing()
    {
        // The walk counts back one day at a time and DateOnly has no day below MinValue. This used to
        // throw out of Evaluate — which runs while the window's lists are rebuilt, so a single such row
        // left every card unable to load, not just its own.
        var info = Evaluate(Days(), createdOn: DateOnly.MinValue);

        Assert.Equal(0, info.Streak);
        Assert.Equal(ItemFlag.Missed, info.Flag);
    }

    [Fact]
    public void ACompletionOnTheEarliestRepresentableDay_IsCountedAndEndsTheWalk()
    {
        // The other half of the same bound: ComputeStreak walks back from the anchor too.
        var completions = new HashSet<DateOnly> { DateOnly.MinValue };

        Assert.Equal(1, StreakCalculator.ComputeStreak(completions, DateOnly.MinValue));
    }

    // ------------------------------------------------------------- backdating

    [Fact]
    public void BackdatingAGap_RepairsAnAlreadyBrokenStreak()
    {
        var broken = Evaluate(Days(0, 2, 3));
        Assert.Equal(1, broken.Streak);
        Assert.Equal(ItemFlag.AtRisk, broken.Flag);

        // Yesterday is filled in from history, after the fact.
        var repaired = Evaluate(Days(0, 1, 2, 3));
        Assert.Equal(4, repaired.Streak);
        Assert.Equal(0, repaired.MissedDays);
        Assert.Equal(ItemFlag.OnTrack, repaired.Flag);
    }

    [Fact]
    public void BackdatingCanTurnARedItemBackOnTrack()
    {
        var red = Evaluate(Days(0, 3));
        Assert.Equal(ItemFlag.Missed, red.Flag);

        var repaired = Evaluate(Days(0, 1, 2, 3));
        Assert.Equal(ItemFlag.OnTrack, repaired.Flag);
        Assert.Equal(4, repaired.Streak);
    }

    [Fact]
    public void RemovingABackdatedDay_RecomputesTheStreakBackDown()
    {
        var full = Evaluate(Days(0, 1, 2, 3));
        Assert.Equal(4, full.Streak);

        // Yesterday's backfilled completion is withdrawn.
        var afterRemoval = Evaluate(Days(0, 2, 3));
        Assert.Equal(1, afterRemoval.Streak);
        Assert.Equal(1, afterRemoval.MissedDays);
    }
}
