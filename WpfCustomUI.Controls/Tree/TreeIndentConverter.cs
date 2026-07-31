using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WpfCustomUI.Controls;

/// <summary>ノードの深さ(Level)を左インデントの Thickness に変換する。</summary>
public sealed class TreeIndentConverter : IValueConverter
{
    /// <summary>1階層あたりのインデント幅(px)。</summary>
    public double IndentSize { get; set; } = 16.0;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int level ? new Thickness(level * IndentSize, 0, 0, 0) : new Thickness(0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
