using System.Windows;
using System.Windows.Input;
using WpfCustomUI.Controls.Theming;

namespace WpfCustomUI.Charts;

/// <summary>
/// Wcu トークン配色が適用済みの <see cref="ScottPlot.WPF.WpfPlot"/>(spec 6.14.2)。
/// <para>
/// 生成時に <see cref="WcuChartTheme.Apply"/> を適用し、表示中は
/// <see cref="ThemeManager.ThemeChanged"/> を購読してテーマ・アクセント変更へ
/// 自動追従する(既存プロットの色は再適用されないため、シリーズ色の追従が
/// 必要な複合コントロール側は再構築で対応する)。
/// </para>
/// <para>
/// ホイールズームは既定で <b>Ctrl+ホイール</b>(<see cref="WheelZoomRequiresCtrl"/>)。
/// 素のホイールは外側の ScrollViewer に委ねるため、スクロールページに
/// 埋め込んでもページスクロールと競合しない。
/// </para>
/// </summary>
public class WcuPlot : ScottPlot.WPF.WpfPlot
{
    public static readonly DependencyProperty WheelZoomRequiresCtrlProperty =
        DependencyProperty.Register(
            nameof(WheelZoomRequiresCtrl), typeof(bool), typeof(WcuPlot),
            new PropertyMetadata(true));

    public WcuPlot()
    {
        WcuChartTheme.Apply(Plot);

        // ScottPlot 既定のホイールズームは Ctrl を「縦軸ロック」キーに使うため、
        // Ctrl+ホイール=ズームのゲートと競合する。ロックキーを Shift(横軸)/
        // Alt(縦軸)に付け替える。
        UserInputProcessor.RemoveAll<ScottPlot.Interactivity.UserActionResponses.MouseWheelZoom>();
        UserInputProcessor.UserActionResponses.Add(
            new ScottPlot.Interactivity.UserActionResponses.MouseWheelZoom(
                ScottPlot.Interactivity.StandardKeys.Shift,
                ScottPlot.Interactivity.StandardKeys.Alt));

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// テーマ再適用後に発火する。複合コントロールがシリーズ再構築に使う。
    /// </summary>
    public event EventHandler? ThemeApplied;

    /// <summary>
    /// true(既定)の場合、ホイールズームに Ctrl キーを要求し、素のホイールは
    /// 外側のスクロールに委ねる。スクロールしない領域いっぱいにチャートを
    /// 配置するアプリでは false にすると素のホイールでズームできる。
    /// </summary>
    public bool WheelZoomRequiresCtrl
    {
        get => (bool)GetValue(WheelZoomRequiresCtrlProperty);
        set => SetValue(WheelZoomRequiresCtrlProperty, value);
    }

    private bool IsWheelZoomGesture =>
        !WheelZoomRequiresCtrl || (Keyboard.Modifiers & ModifierKeys.Control) != 0;

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);

        if (IsWheelZoomGesture)
        {
            return; // 内部の SKElement までトンネルさせ、ズームを実行させる
        }

        // Ctrl なしのホイール: ズームを抑止し(SKElement に届く前に止める)、
        // このコントロールを起点にバブリングイベントを再発行して
        // 外側の ScrollViewer のページスクロールに委ねる。
        e.Handled = true;
        var args = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = this,
        };
        RaiseEvent(args);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (IsWheelZoomGesture)
        {
            // ズーム時はページスクロールと競合しないようバブリングを止める
            e.Handled = true;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 静的イベントからのリークを防ぐため、表示中のみ購読する
        ThemeManager.ThemeChanged -= OnThemeChanged;
        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        WcuChartTheme.Apply(Plot);
        ThemeApplied?.Invoke(this, EventArgs.Empty);
        Refresh();
    }
}
