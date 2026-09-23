using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using HabitTracker.Core.Domain;

namespace HabitTracker.App.Converters;

/// <summary>
/// Maps an item's flag to its card background. Applied per card, so a missed daily tints only its
/// own card — other habits keep their normal appearance. These are the dark-theme surfaces and are
/// kept in step with the palette in <c>App.xaml</c>.
/// </summary>
[ValueConversion(typeof(ItemFlag), typeof(Brush))]
public sealed class FlagToBackgroundBrushConverter : IValueConverter
{
    private static readonly Brush OnTrack = Freeze(Color.FromRgb(0x2A, 0x2E, 0x37));
    private static readonly Brush AtRisk = Freeze(Color.FromRgb(0x3B, 0x33, 0x23));
    private static readonly Brush Missed = Freeze(Color.FromRgb(0x42, 0x28, 0x2B));

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
