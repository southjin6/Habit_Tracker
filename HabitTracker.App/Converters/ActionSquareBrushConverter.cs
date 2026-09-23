using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using HabitTracker.Core.Domain;

namespace HabitTracker.App.Converters;

/// <summary>
/// Picks the colour of a card's left action square. Done is green; an open daily borrows the flag
/// colour so a lapse is visible at a glance, while tasks — which are never flagged — stay amber.
/// </summary>
public sealed class ActionSquareBrushConverter : IMultiValueConverter
{
    private static readonly Brush Done = Freeze(Color.FromRgb(0x39, 0xB2, 0x6B));
    private static readonly Brush Due = Freeze(Color.FromRgb(0xFF, 0xBE, 0x5D));
    private static readonly Brush AtRisk = Freeze(Color.FromRgb(0xE8, 0x96, 0x1E));
    private static readonly Brush Missed = Freeze(Color.FromRgb(0xD9, 0x53, 0x4F));

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = values.Length > 0 && values[0] is ItemFlag f ? f : ItemFlag.OnTrack;
        var isDone = values.Length > 1 && values[1] is bool done && done;
        var isDaily = values.Length > 2 && values[2] is bool daily && daily;

        if (isDone) return Done;
        if (!isDaily) return Due;
        return flag switch
        {
            ItemFlag.Missed => Missed,
            ItemFlag.AtRisk => AtRisk,
            _ => Due,
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
