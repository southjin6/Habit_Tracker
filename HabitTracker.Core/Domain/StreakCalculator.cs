namespace HabitTracker.Core.Domain;

/// <summary>
/// Derives a daily's streak and missed-day state purely from its stored completion history.
///
/// Nothing here is incremental: there is no counter that gets bumped up or down. Every value is
/// recomputed from the set of completed dates, which is what makes backdating able to repair an
/// already-broken streak and makes removing a backdated day recompute the streak back down —
/// both fall out for free instead of needing special cases.
///
/// All dates are UTC+8 calendar days with no time component, so day arithmetic is exact.
/// </summary>
public static class StreakCalculator
{
    /// <summary>Full evaluation of one daily: current streak, consecutive missed days, and flag.</summary>
    public static StreakInfo Evaluate(
        IReadOnlySet<DateOnly> completions,
        DateOnly createdOn,
        DateOnly today)
    {
        var missedDays = ComputeMissedDays(completions, createdOn, today);
        return new StreakInfo(
            ComputeStreak(completions, today),
            missedDays,
            FlagFor(missedDays));
    }

    /// <summary>
    /// Number of consecutive completed days, counted back from the most recent day that still
    /// keeps the streak alive.
    ///
    /// The streak survives "today not done yet": if today is incomplete but yesterday is complete,
    /// the anchor is yesterday, so opening the app in the morning still shows yesterday's streak
    /// instead of zero. It only collapses once neither today nor yesterday has a completion,
    /// i.e. a full day has genuinely passed with nothing recorded.
    /// </summary>
    public static int ComputeStreak(IReadOnlySet<DateOnly> completions, DateOnly today)
    {
        var anchor = ResolveAnchor(completions, today);
        if (anchor is null)
        {
            return 0;
        }

        var streak = 0;
        for (var day = anchor.Value; completions.Contains(day); day = day.AddDays(-1))
        {
            streak++;
        }

        return streak;
    }

    /// <summary>
    /// Number of consecutive days immediately before today with no completion recorded.
    ///
    /// The walk stops at the item's creation day, so a daily is never counted as missed for days
    /// before it existed — a daily created today has zero missed days no matter how long the app
    /// has been installed.
    /// </summary>
    public static int ComputeMissedDays(
        IReadOnlySet<DateOnly> completions,
        DateOnly createdOn,
        DateOnly today)
    {
        var missed = 0;
        for (var day = today.AddDays(-1);
             day >= createdOn && !completions.Contains(day);
             day = day.AddDays(-1))
        {
            missed++;
        }

        return missed;
    }

    /// <summary>
    /// Maps a missed-day count to a visual state. One miss breaks the streak but stays out of red;
    /// red is reserved for two or more consecutive misses.
    /// </summary>
    public static ItemFlag FlagFor(int missedDays) => missedDays switch
    {
        0 => ItemFlag.OnTrack,
        1 => ItemFlag.AtRisk,
        _ => ItemFlag.Missed,
    };

    /// <summary>
    /// The day the streak is counted back from: today if it is complete, otherwise yesterday if
    /// that is complete, otherwise null — meaning the streak is broken.
    /// </summary>
    private static DateOnly? ResolveAnchor(IReadOnlySet<DateOnly> completions, DateOnly today)
    {
        if (completions.Contains(today))
        {
            return today;
        }

        var yesterday = today.AddDays(-1);
        return completions.Contains(yesterday) ? yesterday : null;
    }
}
