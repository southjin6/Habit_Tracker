using System.Globalization;
using System.Windows.Data;

namespace HabitTracker.App.Converters;

/// <summary>
/// Compares a bound enum against the enum member named by <c>ConverterParameter</c>, so a column's
/// filter tabs can bind <c>IsChecked</c> straight to the view model's filter property.
/// </summary>
public sealed class EnumMatchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not null && parameter is string name &&
        Enum.TryParse(value.GetType(), name, out var target) && value.Equals(target);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && parameter is string name
            ? Enum.Parse(targetType, name)
            : Binding.DoNothing;
}
