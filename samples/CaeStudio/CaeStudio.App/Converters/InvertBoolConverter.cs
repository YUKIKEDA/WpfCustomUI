using System.Globalization;
using System.Windows.Data;

namespace CaeStudio.App.Converters;

/// <summary>bool 反転(排他的な RadioButton ペアの片側バインド用)。</summary>
public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is false;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is false;
}
