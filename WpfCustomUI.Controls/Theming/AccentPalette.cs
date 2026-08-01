using System.Windows.Media;

namespace WpfCustomUI.Controls.Theming;

/// <summary>
/// アクセント基準色から状態別の派生色を計算した結果(spec 4.3)。
/// </summary>
public readonly record struct AccentPalette(Color Default, Color Hover, Color Pressed, Color Muted)
{
    /// <summary>
    /// 基準色から派生色を HSL 明度調整で自動計算する(ダークテーマ向け)。
    /// Muted は選択背景などに使う、大きく暗くした色。
    /// </summary>
    public static AccentPalette FromBase(Color baseColor) =>
        FromBase(baseColor, WcuThemeVariant.Dark);

    /// <summary>
    /// 基準色からテーマバリアントに応じた派生色を自動計算する(spec 6.15)。
    /// Muted は選択背景などに使う低彩度色で、ダークでは暗く、ライトでは
    /// 淡く(明るく)して前景テキストとのコントラストを確保する。
    /// </summary>
    public static AccentPalette FromBase(Color baseColor, WcuThemeVariant variant) => new(
        Default: baseColor,
        Hover: ColorMath.Lighten(baseColor, 0.10),
        Pressed: ColorMath.Darken(baseColor, 0.10),
        Muted: variant == WcuThemeVariant.Light
            ? ColorMath.Lighten(baseColor, 0.45)
            : ColorMath.Darken(baseColor, 0.35));
}
