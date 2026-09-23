using HabitTracker.Core.Domain;

namespace HabitTracker.Tests;

/// <summary>
/// Covers the date-handling acceptance criteria: the day boundary is a fixed UTC+8 offset, so it
/// is unaffected by the machine's own timezone or clock settings.
/// </summary>
public class Utc8ClockTests
{
    [Fact]
    public void Offset_IsFixedAtPlusEightHours()
    {
        Assert.Equal(TimeSpan.FromHours(8), Utc8Clock.Offset);
    }

    [Fact]
    public void AnInstantAt2359UtcPlus8_BelongsToThatDay()
    {
        // 15:59Z is 23:59 in UTC+8.
        var instant = new DateTime(2026, 9, 22, 15, 59, 0, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 9, 22), Utc8Clock.FromInstant(instant));
    }

    [Fact]
    public void AnInstantAt0001TheNextUtcPlus8Day_BelongsToTheNextDay()
    {
        // 16:01Z is 00:01 the following day in UTC+8.
        var instant = new DateTime(2026, 9, 22, 16, 1, 0, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 9, 23), Utc8Clock.FromInstant(instant));
    }

    [Fact]
    public void TheTwoSidesOfMidnightUtcPlus8_AreDifferentDays()
    {
        var justBefore = new DateTime(2026, 9, 22, 15, 59, 59, DateTimeKind.Utc);
        var justAfter = new DateTime(2026, 9, 22, 16, 0, 0, DateTimeKind.Utc);

        Assert.NotEqual(
            Utc8Clock.FromInstant(justBefore),
            Utc8Clock.FromInstant(justAfter));
    }

    [Fact]
    public void TheDayBoundaryIsExactlyMidnightUtcPlus8()
    {
        // 15:59:59.999Z is still the 22nd; 16:00:00Z is the 23rd.
        var lastMoment = new DateTime(2026, 9, 22, 15, 59, 59, DateTimeKind.Utc).AddTicks(TimeSpan.TicksPerSecond - 1);
        var firstMoment = new DateTime(2026, 9, 22, 16, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 9, 22), Utc8Clock.FromInstant(lastMoment));
        Assert.Equal(new DateOnly(2026, 9, 23), Utc8Clock.FromInstant(firstMoment));
    }

    [Fact]
    public void Today_IsResolvedInUtcPlus8_NotMachineLocalTime()
    {
        // Brackets the read so the assertion cannot flake if it happens to run across midnight.
        var before = Utc8Clock.FromInstant(DateTime.UtcNow);
        var today = Utc8Clock.Today;
        var after = Utc8Clock.FromInstant(DateTime.UtcNow);

        Assert.Contains(today, new[] { before, after });
    }
}
