using System.Windows;
using AvalonDock.Themes;

namespace WpfCustomUI.Docking;

/// <summary>
/// AvalonDock 用の Wcu ドックテーマ(spec 6.13.4)。
/// <para>
/// VS2013 テーマの制御テンプレート(タブ・キャプション・ドッキングガイド等)を
/// そのまま利用し、色だけを Wcu デザイントークンへ差し替えた
/// <see cref="DictionaryTheme"/>。ブラシは Wcu.Color.* を DynamicResource 参照
/// しているため、テーマ切替や <c>ThemeManager.SetAccent</c> に実行時追従する。
/// </para>
/// <example>
/// <code language="xaml">
/// &lt;ad:DockingManager&gt;
///     &lt;ad:DockingManager.Theme&gt;
///         &lt;wcud:WcuDockTheme /&gt;
///     &lt;/ad:DockingManager.Theme&gt;
///     ...
/// &lt;/ad:DockingManager&gt;
/// </code>
/// </example>
/// </summary>
public class WcuDockTheme : DictionaryTheme
{
    public WcuDockTheme()
        : base(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/WpfCustomUI.Docking;component/Themes/WcuDockResources.xaml"),
        })
    {
    }
}
