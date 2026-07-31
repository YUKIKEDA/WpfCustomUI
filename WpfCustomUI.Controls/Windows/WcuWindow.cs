using System.Windows;
using System.Windows.Input;

namespace WpfCustomUI.Controls;

/// <summary>
/// カスタムクローム(タイトルバー)を持つ Window。
/// WindowChrome ベースで、リサイズ・スナップ・最大化などの OS 挙動は維持しつつ、
/// タイトルバーをテーマに合わせて描画する。
/// <see cref="TitleBarContent"/> にメニューや検索ボックス等を配置できる(spec 6.8.4)。
/// </summary>
public class WcuWindow : Window
{
    static WcuWindow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(WcuWindow),
            new FrameworkPropertyMetadata(typeof(WcuWindow)));
    }

    public WcuWindow()
    {
        // キャプションボタンは SystemCommands 経由で動かす
        CommandBindings.Add(new CommandBinding(
            SystemCommands.MinimizeWindowCommand, (_, _) => SystemCommands.MinimizeWindow(this)));
        CommandBindings.Add(new CommandBinding(
            SystemCommands.MaximizeWindowCommand, (_, _) => SystemCommands.MaximizeWindow(this)));
        CommandBindings.Add(new CommandBinding(
            SystemCommands.RestoreWindowCommand, (_, _) => SystemCommands.RestoreWindow(this)));
        CommandBindings.Add(new CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => SystemCommands.CloseWindow(this)));
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        // WindowChrome + SizeToContent 併用時の WPF 既知バグ対策:
        // 初回表示時に標準フレーム分だけウィンドウが大きく計算され、
        // 右・下に未描画領域(黒帯)が残るため、再レイアウトで詰め直す。
        if (SizeToContent != SizeToContent.Manual)
        {
            InvalidateMeasure();
        }
    }

    public static readonly DependencyProperty TitleBarContentProperty =
        DependencyProperty.Register(
            nameof(TitleBarContent), typeof(object), typeof(WcuWindow),
            new PropertyMetadata(null));

    /// <summary>
    /// タイトル文字列とキャプションボタンの間に表示する任意コンテンツ。
    /// メニューバーや検索ボックスなどを想定。この領域はドラッグではなく
    /// コンテンツ操作が優先される(WindowChrome.IsHitTestVisibleInChrome)。
    /// </summary>
    public object? TitleBarContent
    {
        get => GetValue(TitleBarContentProperty);
        set => SetValue(TitleBarContentProperty, value);
    }
}
