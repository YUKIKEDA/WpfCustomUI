using System.Windows;
using System.Windows.Controls;

namespace WpfCustomUI.Controls;

/// <summary>
/// ワークフロー指向のリボン(spec 6.27.2)。タブ+ラベル付きグループで
/// コマンドを作業段階順に配置する。v1 は最小実用核:
/// タブ / グループ / 大小ボタン(スタイルキー) / 任意コントロールホスト /
/// アプリケーションボタン / タブ列右端の補助領域。
/// 折り畳みは簡易版(コンテンツ行の横スクロール)。
/// QAT / Backstage / KeyTips / コンテキストタブはバックログ。
/// </summary>
public class WcuRibbon : TabControl
{
    static WcuRibbon()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(WcuRibbon), new FrameworkPropertyMetadata(typeof(WcuRibbon)));
    }

    public static readonly DependencyProperty ApplicationMenuProperty = DependencyProperty.Register(
        nameof(ApplicationMenu), typeof(ContextMenu), typeof(WcuRibbon),
        new PropertyMetadata(null));

    /// <summary>
    /// アプリケーションボタン(タブ列左端のアクセント色ボタン)が開くメニュー。
    /// ファイル操作等の低頻度コマンドを集約する(メニューバーの置き換え)。
    /// </summary>
    public ContextMenu? ApplicationMenu
    {
        get => (ContextMenu?)GetValue(ApplicationMenuProperty);
        set => SetValue(ApplicationMenuProperty, value);
    }

    public static readonly DependencyProperty ApplicationLabelProperty = DependencyProperty.Register(
        nameof(ApplicationLabel), typeof(object), typeof(WcuRibbon),
        new PropertyMetadata("File"));

    /// <summary>アプリケーションボタンのラベル。既定 "File"(表示文字列はアプリが注入する。spec 5)。</summary>
    public object? ApplicationLabel
    {
        get => GetValue(ApplicationLabelProperty);
        set => SetValue(ApplicationLabelProperty, value);
    }

    public static readonly DependencyProperty AuxiliaryContentProperty = DependencyProperty.Register(
        nameof(AuxiliaryContent), typeof(object), typeof(WcuRibbon),
        new PropertyMetadata(null));

    /// <summary>タブ列右端の補助領域(テーマトグル・ヘルプ等)。</summary>
    public object? AuxiliaryContent
    {
        get => GetValue(AuxiliaryContentProperty);
        set => SetValue(AuxiliaryContentProperty, value);
    }

    protected override DependencyObject GetContainerForItemOverride() => new WcuRibbonTab();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is WcuRibbonTab;
}
