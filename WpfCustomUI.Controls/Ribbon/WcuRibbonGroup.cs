using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace WpfCustomUI.Controls;

/// <summary>
/// リボンのラベル付きグループ。子要素を横に並べ、下部にグループ名を表示し、
/// 右端に区切り線を描く。子は任意のコントロール
/// (Wcu.Ribbon.Button.* スタイルのボタン、ComboBox、縦 StackPanel 等)。
/// </summary>
public class WcuRibbonGroup : HeaderedItemsControl
{
    static WcuRibbonGroup()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(WcuRibbonGroup), new FrameworkPropertyMetadata(typeof(WcuRibbonGroup)));
    }

    /// <summary>
    /// 既定の ItemsControl ピアは子を DataItem ラッパー(<see cref="ItemAutomationPeer"/>)で包み、
    /// Invoke 等のパターンを転送しないため UIA からボタンを操作できなくなる。
    /// 素のピアに差し替えて子コントロールのピアを直接公開する。
    /// </summary>
    protected override AutomationPeer OnCreateAutomationPeer() =>
        new FrameworkElementAutomationPeer(this);
}
