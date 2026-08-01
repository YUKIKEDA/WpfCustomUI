using System.Globalization;
using System.Windows.Data;
using System.Windows.Input;

namespace CaeStudio.App.Converters;

/// <summary>KeyGestureBox の <see cref="KeyGesture"/> ⇔ 設定保存用文字列("Ctrl+R" 等)。</summary>
public sealed class KeyGestureStringConverter : IValueConverter
{
    private static readonly KeyGestureConverter Inner = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return Inner.ConvertFromInvariantString(text) as KeyGesture;
        }
        catch (Exception exception) when (exception is NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is KeyGesture gesture ? Inner.ConvertToInvariantString(gesture) : "";
}
