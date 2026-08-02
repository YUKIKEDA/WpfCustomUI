using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Markup;

namespace WpfCustomUI.Controls;

/// <summary>
/// <see cref="WcuRibbon"/> のタブ。XAML の直接の子として
/// <see cref="WcuRibbonGroup"/>(または任意の要素)を並べる。
/// </summary>
[ContentProperty(nameof(Groups))]
public class WcuRibbonTab : TabItem
{
    static WcuRibbonTab()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(WcuRibbonTab), new FrameworkPropertyMetadata(typeof(WcuRibbonTab)));
    }

    public WcuRibbonTab()
    {
        // タブのコンテンツ = グループの横並びホスト。
        // TabControl の SelectedContent 機構をそのまま使うため、
        // Groups を表示する ItemsControl を Content に据える。
        Content = CreateGroupsHost();
    }

    /// <summary>このタブに表示するグループのコレクション。</summary>
    public ObservableCollection<object> Groups { get; } = [];

    private ItemsControl CreateGroupsHost()
    {
        var panelFactory = new FrameworkElementFactory(typeof(StackPanel));
        panelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

        var host = new GroupsHost
        {
            ItemsSource = Groups,
            ItemsPanel = new ItemsPanelTemplate(panelFactory),
            Focusable = false,
        };
        // グループの高さをタブ間で揃える(コンテンツ行の高さはリボン側 MinHeight が担保)
        host.VerticalAlignment = VerticalAlignment.Stretch;
        return host;
    }

    /// <summary>
    /// グループホスト。既定の ItemsControl ピア(List + DataItem ラッパー)は
    /// Invoke 等のパターンを転送しないため、素のピアで子のピアを直接公開する。
    /// </summary>
    private sealed class GroupsHost : ItemsControl
    {
        protected override AutomationPeer OnCreateAutomationPeer() =>
            new FrameworkElementAutomationPeer(this);
    }
}
