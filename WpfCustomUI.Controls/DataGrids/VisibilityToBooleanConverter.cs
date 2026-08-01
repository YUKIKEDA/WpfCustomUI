using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WpfCustomUI.Controls;

/// <summary>
/// Visibility ⇔ bool の双方向コンバーター。
/// DataGridRow.DetailsVisibility(Visibility)を ToggleButton.IsChecked(bool)に
/// バインドする用途(Wcu.DataGrid.DetailsToggle)を想定している。
/// 標準の BooleanToVisibilityConverter は変換方向が逆で使えない。
/// </summary>
public sealed class VisibilityToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
}
