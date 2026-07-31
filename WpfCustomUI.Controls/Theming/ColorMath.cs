using System.Windows.Media;

namespace WpfCustomUI.Controls.Theming;

/// <summary>
/// HSL 色空間での色計算ユーティリティ。
/// アクセント派生色の計算(spec 4.3)に使用。将来のカラーマップでも利用可能。
/// </summary>
public static class ColorMath
{
    /// <summary>RGB を HSL に変換する。H は 0-360、S / L は 0-1。アルファは無視される。</summary>
    public static (double H, double S, double L) ToHsl(Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double l = (max + min) / 2.0;

        if (delta == 0)
        {
            return (0, 0, l);
        }

        double s = delta / (1 - Math.Abs(2 * l - 1));

        double h;
        if (max == r)
        {
            h = 60 * (((g - b) / delta) % 6);
        }
        else if (max == g)
        {
            h = 60 * ((b - r) / delta + 2);
        }
        else
        {
            h = 60 * ((r - g) / delta + 4);
        }

        if (h < 0)
        {
            h += 360;
        }

        return (h, s, l);
    }

    /// <summary>HSL を RGB に変換する。H は 0-360、S / L は 0-1。アルファは不透明。</summary>
    public static Color FromHsl(double h, double s, double l)
    {
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        h = ((h % 360) + 360) % 360;

        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = l - c / 2;

        (double r, double g, double b) = (h / 60) switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    /// <summary>明度を指定量(0-1)だけ上げた色を返す。</summary>
    public static Color Lighten(Color color, double amount)
    {
        var (h, s, l) = ToHsl(color);
        return FromHsl(h, s, l + amount);
    }

    /// <summary>明度を指定量(0-1)だけ下げた色を返す。</summary>
    public static Color Darken(Color color, double amount)
    {
        var (h, s, l) = ToHsl(color);
        return FromHsl(h, s, l - amount);
    }
}
