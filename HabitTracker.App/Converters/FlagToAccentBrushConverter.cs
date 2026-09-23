using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using HabitTracker.Core.Domain;

namespace HabitTracker.App.Converters;

/// <summary>
/// Maps an item's flag to its text accent colour (card subtitle and streak counter). Muted grey when
/// on track; only <see cref="ItemFlag.Missed"/> is red, and one missed day is amber so a broken
/// streak is visibly distinct from a two-day lapse. Lightened for legibility on the dark theme.
/// </summary>
[ValueConversion(typeof(ItemFlag), typeof(Brush))]
public sealed class FlagToAccentBrushConverter : IValueConverter
{
    private static readonly Brush OnTrack = Freeze(Color.FromRgb(0xA6, 0xAE, 0xBB));
    private static readonly Brush AtRisk = Freeze(Color.FromRgb(0xFF, 0xC4, 0x6B));
    private static readonly Brush Missed = Freeze(Color.FromRgb(0xFF, 0x7B, 0x7B));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ItemFlag flag
            ? flag switch
            {
                ItemFlag.AtRisk => AtRisk,
                ItemFlag.Missed => Missed,
                _ => OnTrack,
            }
            : OnTrack;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
