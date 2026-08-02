using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WpfCustomUI.Controls;

/// <summary>
/// <see cref="WcuIcons"/> のラインアイコンを表示するコントロール(spec 6.27.2)。
/// 16x16 キャンバスに描かれた Geometry を Viewbox でストロークごと拡大するため、
/// どのサイズでも線の太さの比率が保たれる。色は Foreground(テーマトークン)に追従する。
/// </summary>
public class WcuIcon : Control
{
    static WcuIcon()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(WcuIcon), new FrameworkPropertyMetadata(typeof(WcuIcon)));
        FocusableProperty.OverrideMetadata(
            typeof(WcuIcon), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(
            typeof(WcuIcon), new FrameworkPropertyMetadata(false));
    }

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(WcuIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>表示するアイコン Geometry(通常は <see cref="WcuIcons"/> のメンバ)。</summary>
    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(WcuIcon),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>表示サイズ(正方形の一辺、DIP)。既定 16。</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(WcuIcon),
        new FrameworkPropertyMetadata(1.2, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>16x16 キャンバス基準のストローク太さ。既定 1.2(表示サイズに比例して拡大)。</summary>
    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }
}
