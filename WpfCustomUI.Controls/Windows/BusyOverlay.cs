using System.Windows;
using System.Windows.Controls;

namespace WpfCustomUI.Controls;

/// <summary>
/// 任意の UI 領域をラップし、<see cref="IsBusy"/> の間だけ半透明ベールと
/// スピナー+メッセージを重ねて操作をブロックするコントロール(spec 6.8.7)。
/// 進捗を出したい場合は <see cref="BusyContent"/> に ProgressDisplay 等を渡す。
/// </summary>
public class BusyOverlay : ContentControl
{
    static BusyOverlay()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(BusyOverlay),
            new FrameworkPropertyMetadata(typeof(BusyOverlay)));
    }

    public static readonly DependencyProperty IsBusyProperty =
        DependencyProperty.Register(
            nameof(IsBusy), typeof(bool), typeof(BusyOverlay),
            new PropertyMetadata(false));

    /// <summary>true の間、オーバーレイを表示して配下の UI を無効化する。</summary>
    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public static readonly DependencyProperty BusyTextProperty =
        DependencyProperty.Register(
            nameof(BusyText), typeof(string), typeof(BusyOverlay),
            new PropertyMetadata(string.Empty));

    /// <summary>既定表示(スピナー)の下に出すメッセージ。</summary>
    public string BusyText
    {
        get => (string)GetValue(BusyTextProperty);
        set => SetValue(BusyTextProperty, value);
    }

    public static readonly DependencyProperty BusyContentProperty =
        DependencyProperty.Register(
            nameof(BusyContent), typeof(object), typeof(BusyOverlay),
            new PropertyMetadata(null));

    /// <summary>
    /// オーバーレイ中央に表示するカスタムコンテンツ(ProgressDisplay 等)。
    /// null の場合は既定のスピナー+<see cref="BusyText"/> を表示する。
    /// </summary>
    public object? BusyContent
    {
        get => GetValue(BusyContentProperty);
        set => SetValue(BusyContentProperty, value);
    }
}
