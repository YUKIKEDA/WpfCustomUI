using System.Windows;
using System.Windows.Media;

namespace WpfCustomUI.Controls;

/// <summary>
/// ボタン系コントロールへのアイコン添付プロパティ(spec 6.27.2)。
/// <c>wcu:ButtonAssist.Icon</c> に Geometry を設定すると、
/// Wcu のボタンテンプレートがコンテンツの左にアイコンを表示する。
/// </summary>
public static class ButtonAssist
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(Geometry), typeof(ButtonAssist),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static Geometry? GetIcon(DependencyObject element) =>
        (Geometry?)element.GetValue(IconProperty);

    public static void SetIcon(DependencyObject element, Geometry? value) =>
        element.SetValue(IconProperty, value);

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.RegisterAttached(
        "IconSize", typeof(double), typeof(ButtonAssist),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>アイコンの表示サイズ。既定 16(リボン大ボタンは 24 を使う)。</summary>
    public static double GetIconSize(DependencyObject element) =>
        (double)element.GetValue(IconSizeProperty);

    public static void SetIconSize(DependencyObject element, double value) =>
        element.SetValue(IconSizeProperty, value);
}
