using System.Windows;

namespace WpfCustomUI.Controls;

/// <summary>
/// アプリ内ダイアログの基底ウィンドウ(spec 6.8.5)。
/// WcuWindow のクロームに加え、下部にボタンバー(<see cref="Footer"/>)領域を持つ。
/// 既定でオーナー中央表示・タスクバー非表示・リサイズ不可。
/// </summary>
public class WcuDialogWindow : WcuWindow
{
    static WcuDialogWindow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(WcuDialogWindow),
            new FrameworkPropertyMetadata(typeof(WcuDialogWindow)));
        ResizeModeProperty.OverrideMetadata(
            typeof(WcuDialogWindow),
            new FrameworkPropertyMetadata(ResizeMode.NoResize));
        ShowInTaskbarProperty.OverrideMetadata(
            typeof(WcuDialogWindow),
            new FrameworkPropertyMetadata(false));
    }

    public WcuDialogWindow()
    {
        // Owner が未設定なら ShowDialog 時に画面中央へフォールバックする
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    public static readonly DependencyProperty FooterProperty =
        DependencyProperty.Register(
            nameof(Footer), typeof(object), typeof(WcuDialogWindow),
            new PropertyMetadata(null));

    /// <summary>
    /// ダイアログ下部のボタンバーに表示するコンテンツ。
    /// null の場合はバー自体が表示されない。
    /// </summary>
    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }
}
