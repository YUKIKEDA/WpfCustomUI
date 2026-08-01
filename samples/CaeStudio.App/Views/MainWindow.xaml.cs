using AvalonDock.Layout;
using CaeStudio.App.ViewModels;
using CaeStudio.Application;
using System.ComponentModel;
using System.Windows.Input;
using WpfCustomUI.Controls;
using WpfCustomUI.Docking;

namespace CaeStudio.App.Views;

/// <summary>
/// メインシェル。VM に還元できない View 固有の責務のみ持つ:
/// ドックレイアウトの保存/復元(spec 6.26.6)、閉じる前の未保存確認、
/// 設定に従う解析実行ショートカットの動的登録。
/// </summary>
public partial class MainWindow : WcuWindow
{
    private readonly MainViewModel _viewModel;
    private readonly ISettingsService _settings;
    private KeyBinding? _runBinding;

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

    /// <summary>前回終了時のドックレイアウトを ContentId ベースで復元する。</summary>
    private void RestoreDockLayout()
    {
        if (_settings.Current.DockLayoutXml is not { } xml)
        {
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
