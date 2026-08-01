using System.Windows;
using System.Windows.Controls;

namespace WpfCustomUI.Controls;

/// <summary>
/// 回転スピナー単体(spec 6.10.4)。BusyOverlay の内蔵スピナーを切り出したもの。
/// サイズは Width / Height で指定する(既定 24×24)。
/// </summary>
public class ProgressRing : Control
{
    static ProgressRing()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ProgressRing), new FrameworkPropertyMetadata(typeof(ProgressRing)));
    }

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(ProgressRing), new PropertyMetadata(true));

    /// <summary>false にすると非表示になり、回転アニメーションも停止する。</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }
}
