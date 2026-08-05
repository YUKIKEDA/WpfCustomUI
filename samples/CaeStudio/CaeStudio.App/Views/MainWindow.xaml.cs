using AvalonDock.Layout;
using CaeStudio.App.ViewModels;
using CaeStudio.Application;
using R3;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WpfCustomUI.Controls;
using WpfCustomUI.Docking;

namespace CaeStudio.App.Views;

/// <summary>
/// メインシェル。VM に還元できない View 固有の責務のみ持つ:
/// ドックレイアウトの保存/復元(spec 6.26.6)、閉じる前の未保存確認、
/// 設定に従う解析実行ショートカットの動的登録、
/// 段階連動パネルの表示状態と AvalonDock アンカラブルの同期(spec 6.27.3)。
/// </summary>
public partial class MainWindow : WcuWindow
{
    /// <summary>
    /// 段階連動・常設を含む必須 ContentId。保存レイアウトに欠けていれば復元を捨てる
    /// (Phase 27 で追加した Cae.Jobs が古い XML に無いとパネルごと消えるため)。
    /// </summary>
    private static readonly string[] RequiredDockContentIds =
    [
        "Cae.Model", "Cae.Viewport", "Cae.Properties", "Cae.Log",
        "Cae.Convergence", "Cae.PathPlot", "Cae.Histogram", "Cae.Frf",
        "Cae.Study", "Cae.Jobs", "Cae.Modes", "Cae.Material", "Cae.Legend",
    ];

    private readonly MainViewModel _viewModel;
    private readonly ISettingsService _settings;
    private KeyBinding? _runBinding;
    private bool _panelSyncReady;

    public MainWindow(MainViewModel viewModel, ISettingsService settings)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        _settings = settings;

        RestoreDockLayout();
        UpdateRunGesture(viewModel.RunGestureText.Value);
        viewModel.RunGestureText.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(viewModel.RunGestureText.Value))
            {
                UpdateRunGesture(viewModel.RunGestureText.Value);
            }
        };

        // AvalonDock の Show/Hide はビジュアルツリー接続後でないとネイティブクラッシュしうる
        Loaded += OnLoadedSetupPanelSync;
    }

    private void OnLoadedSetupPanelSync(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedSetupPanelSync;
        // レイアウト適用後のアイドルで同期を開始する
        Dispatcher.BeginInvoke(SetupPanelSync, DispatcherPriority.Loaded);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_viewModel.ConfirmClose())
        {
            e.Cancel = true;
            base.OnClosing(e);
            return;
        }

        var xml = DockLayout.SaveToString(DockManager);
        _settings.Update(s => s with { DockLayoutXml = xml });
        base.OnClosing(e);
    }

    /// <summary>
    /// 段階連動パネル(spec 6.27.3)の VM 表示状態と AvalonDock アンカラブルを双方向同期する。
    /// LayoutAnchorable は DependencyObject でないため XAML バインドできず、ここで配線する。
    /// </summary>
    private void SetupPanelSync()
    {
        if (_panelSyncReady)
        {
            return;
        }

        _panelSyncReady = true;

        SyncPanel("Cae.Convergence", _viewModel.IsConvergenceVisible);
        SyncPanel("Cae.PathPlot", _viewModel.IsPathPlotVisible);
        SyncPanel("Cae.Histogram", _viewModel.IsHistogramVisible);
        SyncPanel("Cae.Frf", _viewModel.IsFrfVisible);
        SyncPanel("Cae.Study", _viewModel.IsStudyVisible);
        SyncPanel("Cae.Jobs", _viewModel.IsJobsVisible, activateOnShow: true);
        SyncPanel("Cae.Modes", _viewModel.IsModesVisible);
        SyncPanel("Cae.Material", _viewModel.IsMaterialVisible);
        SyncPanel("Cae.Legend", _viewModel.IsLegendVisible);

        // 解析開始時は収束モニタを前面に出す(表示だけでなくタブ選択も切り替える)
        _viewModel.ActivateConvergenceRequest.Subscribe(this, static (count, self) =>
        {
            if (count == 0)
            {
                return;
            }

            self.Dispatcher.BeginInvoke(() =>
            {
                if (self.FindAnchorable("Cae.Convergence") is not { } convergence)
                {
                    return;
                }

                MainWindow.EnsureShown(convergence);
                convergence.IsSelected = true;
            }, DispatcherPriority.Background);
        });
    }

    private LayoutAnchorable? FindAnchorable(string contentId) =>
        DockManager.Layout.Descendents()
            .OfType<LayoutAnchorable>()
            .FirstOrDefault(a => a.ContentId == contentId);

    /// <summary>VM の bool と 1 つのアンカラブルの表示状態を双方向に同期する。</summary>
    private void SyncPanel(
        string contentId, BindableReactiveProperty<bool> isVisible, bool activateOnShow = false)
    {
        if (FindAnchorable(contentId) is not { } anchorable)
        {
            return;
        }

        var syncing = false;

        // VM → View(初期購読で起動時の非表示化も行われる)
        isVisible.Subscribe(visible =>
        {
            if (syncing)
            {
                return;
            }

            // レイアウト更新と重ならないよう Dispatcher 経由で適用する
            Dispatcher.BeginInvoke(() =>
            {
                if (syncing)
                {
                    return;
                }

                syncing = true;
                try
                {
                    if (visible)
                    {
                        EnsureShown(anchorable);
                        if (activateOnShow)
                        {
                            anchorable.IsSelected = true;
                        }
                    }
                    else
                    {
                        TryHide(anchorable);
                    }
                }
                finally
                {
                    syncing = false;
                }
            }, DispatcherPriority.Background);
        });

        // View → VM(ユーザーがピン留め解除などで隠した場合にトグルへ反映)
        anchorable.IsVisibleChanged += (_, _) =>
        {
            if (syncing)
            {
                return;
            }

            syncing = true;
            try
            {
                isVisible.Value = anchorable.IsVisible;
            }
            finally
            {
                syncing = false;
            }
        };
    }

    /// <summary>隠れていれば Show。既に可視なら何もしない(二重呼び出しで AvalonDock を壊さない)。</summary>
    private static void EnsureShown(LayoutAnchorable anchorable)
    {
        try
        {
            if (anchorable.IsHidden)
            {
                anchorable.Show();
            }
            else if (!anchorable.IsVisible)
            {
                // AutoHide 等で IsHidden=false かつ IsVisible=false のケース
                anchorable.Show();
            }
        }
        catch (Exception)
        {
            // AvalonDock のレイアウト不整合時は Show が失敗しうる。解析フローは止めない
        }
    }

    private static void TryHide(LayoutAnchorable anchorable)
    {
        try
        {
            if (anchorable.IsVisible)
            {
                anchorable.Hide();
            }
        }
        catch (Exception)
        {
            // 同上: Hide 失敗でアプリを落とさない
        }
    }

    /// <summary>前回終了時のドックレイアウトを ContentId ベースで復元する。</summary>
    private void RestoreDockLayout()
    {
        if (_settings.Current.DockLayoutXml is not { } xml)
        {
            return;
        }

        // 必須 ContentId が欠けた古い XML(例: Cae.Jobs 追加前)は破棄して XAML 既定に戻す。
        // AvalonDock は XML に無いアンカラブルをレイアウトから消すため、欠けたパネルは
        // Show しても二度と出てこない
        if (!RequiredDockContentIds.All(id =>
                xml.Contains($"ContentId=\"{id}\"", StringComparison.Ordinal)))
        {
            _settings.Update(s => s with { DockLayoutXml = null });
            return;
        }

        // 復元前に既定レイアウト(XAML 定義)の ContentId → 中身を控えておく
        var contents = DockManager.Layout.Descendents()
            .OfType<LayoutContent>()
            .Where(c => !string.IsNullOrEmpty(c.ContentId))
            .ToDictionary(c => c.ContentId, c => c.Content);

        try
        {
            DockLayout.LoadFromString(DockManager, xml,
                contentId => contents.GetValueOrDefault(contentId));
        }
        catch (Exception)
        {
            // 破損したレイアウト XML は無視して既定レイアウトのまま起動する
            _settings.Update(s => s with { DockLayoutXml = null });
        }
    }

    /// <summary>設定のジェスチャ文字列で解析実行の KeyBinding を差し替える。</summary>
    private void UpdateRunGesture(string gestureText)
    {
        if (_runBinding is not null)
        {
            InputBindings.Remove(_runBinding);
            _runBinding = null;
        }

        try
        {
            var gesture = (KeyGesture?)new KeyGestureConverter().ConvertFromInvariantString(gestureText);
            if (gesture is not null)
            {
                _runBinding = new KeyBinding(_viewModel.RunCommand, gesture);
                InputBindings.Add(_runBinding);
            }
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or FormatException)
        {
            // 不正なジェスチャ文字列はショートカットなしで続行
        }
    }
}
