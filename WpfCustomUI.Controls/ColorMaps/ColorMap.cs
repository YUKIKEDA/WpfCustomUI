using System.Windows.Media;

namespace WpfCustomUI.Controls;

/// <summary>
/// カラーマップ(正規化値 0〜1 → 色)の定義(spec 6.4)。
/// 定番スケールを組み込みで提供し、アプリ独自のマップもコンストラクタで作成できる。
/// </summary>
public sealed class ColorMap
{
    private readonly (double Offset, Color Color)[] _stops;

    /// <param name="stops">オフセット(0〜1)昇順の色制御点。2点以上必要。</param>
    public ColorMap(string name, params (double Offset, Color Color)[] stops)
    {
        if (stops.Length < 2)
        {
            throw new ArgumentException("ColorMap requires at least two stops.", nameof(stops));
        }

        Name = name;
        _stops = [.. stops.OrderBy(s => s.Offset)];
    }

    public string Name { get; }

    /// <summary>色制御点(凡例のグラデーション構築などに使用)。</summary>
    public IReadOnlyList<(double Offset, Color Color)> Stops => _stops;

    /// <summary>正規化値 t(0〜1、範囲外はクランプ)に対応する色を線形補間で返す。</summary>
    public Color GetColor(double t)
    {
        if (double.IsNaN(t))
        {
            return _stops[0].Color;
        }

        t = Math.Clamp(t, 0.0, 1.0);

        for (var i = 1; i < _stops.Length; i++)
        {
            if (t <= _stops[i].Offset)
            {
                var (o0, c0) = _stops[i - 1];
                var (o1, c1) = _stops[i];
                var f = o1 > o0 ? (t - o0) / (o1 - o0) : 0.0;
                return Lerp(c0, c1, f);
            }
        }

        return _stops[^1].Color;
    }

    private static Color Lerp(Color a, Color b, double f) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * f),
        (byte)Math.Round(a.R + (b.R - a.R) * f),
        (byte)Math.Round(a.G + (b.G - a.G) * f),
        (byte)Math.Round(a.B + (b.B - a.B) * f));

    private static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    // ---------------- 組み込みカラーマップ ----------------

    public static ColorMap Jet { get; } = new("Jet",
        (0.000, C(0x00, 0x00, 0x7F)),
        (0.125, C(0x00, 0x00, 0xFF)),
        (0.375, C(0x00, 0xFF, 0xFF)),
        (0.625, C(0xFF, 0xFF, 0x00)),
        (0.875, C(0xFF, 0x00, 0x00)),
        (1.000, C(0x7F, 0x00, 0x00)));

    public static ColorMap Viridis { get; } = new("Viridis",
        (0.00, C(0x44, 0x01, 0x54)),
        (0.25, C(0x3B, 0x52, 0x8B)),
        (0.50, C(0x21, 0x91, 0x8C)),
        (0.75, C(0x5E, 0xC9, 0x62)),
        (1.00, C(0xFD, 0xE7, 0x25)));

    public static ColorMap Coolwarm { get; } = new("Coolwarm",
        (0.0, C(0x3B, 0x4C, 0xC0)),
        (0.5, C(0xDD, 0xDD, 0xDD)),
        (1.0, C(0xB4, 0x04, 0x26)));

    public static ColorMap Grayscale { get; } = new("Grayscale",
        (0.0, C(0x00, 0x00, 0x00)),
        (1.0, C(0xFF, 0xFF, 0xFF)));

    public static IReadOnlyList<ColorMap> BuiltIn { get; } = [Jet, Viridis, Coolwarm, Grayscale];
}
