using System.Windows.Media;
using HabitTracker.App.Converters;
using HabitTracker.Core.Domain;

namespace HabitTracker.App.Tests;

/// <summary>
/// The rendering half of the flag rules: one missed day must read as amber and two or more as red,
/// and an open task must never pick up a lapse colour. These are the values the cards actually use,
/// so a palette edit that makes "at risk" red fails here instead of needing someone to look at the
/// window.
/// </summary>
public class FlagBrushTests
{
    private static readonly FlagToBackgroundBrushConverter CardBg = new();
    private static readonly FlagToAccentBrushConverter Accent = new();
    private static readonly ActionSquareBrushConverter Square = new();

    // ------------------------------------------------------ the three states stay three colours

    [Fact]
    public void CardBackground_GivesOnTrack_AtRisk_AndMissed_ThreeDifferentColours()
    {
        var colours = new[] { ItemFlag.OnTrack, ItemFlag.AtRisk, ItemFlag.Missed }
            .Select(flag => Rgb(CardBg.Convert(flag, typeof(Brush), null!, null!)))
            .ToList();

        Assert.Equal(3, colours.Distinct().Count());
    }

    [Fact]
    public void CardBackgroundAtRisk_IsAmber_NotRed()
    {
        // Distinctness alone is not enough on a dark surface: a second, darker red would satisfy
        // "3 different colours" while reading as the two-day lapse. The card palette separates the
        // two by cast, not brightness — amber keeps green above blue (#3B3323), red inverts it
        // (#42282B) and leans further into its own red channel.
        var onTrack = Rgb(CardBg.Convert(ItemFlag.OnTrack, typeof(Brush), null!, null!));
        var atRisk = Rgb(CardBg.Convert(ItemFlag.AtRisk, typeof(Brush), null!, null!));
        var missed = Rgb(CardBg.Convert(ItemFlag.Missed, typeof(Brush), null!, null!));

        Assert.True(atRisk.G > atRisk.B, $"card amber {atRisk} has lost its yellow cast");
        Assert.True(atRisk.G > missed.G, $"card amber {atRisk} is not greener than card red {missed}");
        Assert.True(
            atRisk.R - atRisk.G < missed.R - missed.G,
            $"card amber {atRisk} is as red-dominant as card red {missed}");
        Assert.True(Math.Abs((int)onTrack.R - onTrack.G) < 30, $"card neutral {onTrack} should be neutral");
    }

    [Fact]
    public void AtRisk_IsAmber_NotRed()
    {
        // A single miss breaks the streak but must not look like a two-day lapse: amber keeps a far
        // stronger green than red does, so the two states cannot be confused.
        var atRisk = Rgb(Accent.Convert(ItemFlag.AtRisk, typeof(Brush), null!, null!));
        var missed = Rgb(Accent.Convert(ItemFlag.Missed, typeof(Brush), null!, null!));

        Assert.True(atRisk.G > missed.G + 40, $"amber {atRisk} is not greener than red {missed}");
        Assert.True(atRisk.B < atRisk.R, $"amber {atRisk} must sit below its own red channel");
        Assert.True(missed.G < 160 && missed.B < 160, $"red {missed} is too washed out to read as red");
    }

    [Fact]
    public void Accent_EscalatesGreyThroughAmberToRed()
    {
        var onTrack = Rgb(Accent.Convert(ItemFlag.OnTrack, typeof(Brush), null!, null!));
        var atRisk = Rgb(Accent.Convert(ItemFlag.AtRisk, typeof(Brush), null!, null!));
        var missed = Rgb(Accent.Convert(ItemFlag.Missed, typeof(Brush), null!, null!));

        // Neutral grey has no red dominance; both lapse colours are warm and get warmer.
        Assert.True(Math.Abs((int)onTrack.R - onTrack.G) < 30, $"{onTrack} should be neutral");
        Assert.True(atRisk.R > atRisk.G && atRisk.G > atRisk.B, $"{atRisk} should be amber");
        Assert.True(missed.R > missed.G && missed.G >= missed.B, $"{missed} should be red");
    }

    [Fact]
    public void UnknownOrMissingValue_FallsBackToOnTrack_InsteadOfThrowing()
    {
        var fallback = Rgb(CardBg.Convert(null!, typeof(Brush), null!, null!));

        Assert.Equal(Rgb(CardBg.Convert(ItemFlag.OnTrack, typeof(Brush), null!, null!)), fallback);
    }

    // ------------------------------------------- the action square: tasks never show a lapse colour

    [Fact]
    public void OpenTaskSquare_IsTheSameColour_HoweverBadItsFlagLooks()
    {
        // A task is never flagged in this app, but even a nonsensical flag must not leak a lapse
        // colour into its square: flag colours come from the daily branch only.
        var colours = new[] { ItemFlag.OnTrack, ItemFlag.AtRisk, ItemFlag.Missed }
            .Select(flag => Rgb(Square.Convert([flag, false, false], typeof(Brush), null!, null!)))
            .ToList();

        Assert.Single(colours.Distinct());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DoneSquare_IsGreen_WhateverFlagItCarries(bool isDaily)
    {
        // Completing today's daily clears its lapse colour, and a finished task reads the same way.
        var done = Rgb(Square.Convert([ItemFlag.Missed, true, isDaily], typeof(Brush), null!, null!));
        var clean = Rgb(Square.Convert([ItemFlag.OnTrack, true, isDaily], typeof(Brush), null!, null!));

        Assert.Equal(done, clean);
        Assert.True(done.G > done.R && done.G > done.B, $"{done} should be green");
    }

    [Fact]
    public void OpenDailySquare_GoesFromAmberToRed_AsTheMissesPileUp()
    {
        var due = Rgb(Square.Convert([ItemFlag.OnTrack, false, true], typeof(Brush), null!, null!));
        var atRisk = Rgb(Square.Convert([ItemFlag.AtRisk, false, true], typeof(Brush), null!, null!));
        var missed = Rgb(Square.Convert([ItemFlag.Missed, false, true], typeof(Brush), null!, null!));

        Assert.NotEqual(due, atRisk);
        Assert.NotEqual(atRisk, missed);
        // Red dominates green more strongly at each worse state.
        Assert.True(due.G - due.R > atRisk.G - atRisk.R, $"{due} vs {atRisk}");
        Assert.True(atRisk.G - atRisk.R > missed.G - missed.R, $"{atRisk} vs {missed}");
    }

    private static Color Rgb(object brush) => Assert.IsType<SolidColorBrush>(brush).Color;
}
