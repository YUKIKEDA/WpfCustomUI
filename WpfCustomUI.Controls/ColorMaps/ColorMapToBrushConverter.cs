using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace WpfCustomUI.Controls;

/// <summary>
/// <see cref="ColorMap"/> を水平グラデーションブラシに変換する
/// (ColorScaleEditor のカラーマップ選択プレビュー等に使用)。
/// </summary>
public sealed class ColorMapToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ColorMap map)
        {
            return null;
        }

        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
        };
        foreach (var (offset, color) in map.Stops)
        {
            brush.GradientStops.Add(new GradientStop(color, offset));
        }

        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
