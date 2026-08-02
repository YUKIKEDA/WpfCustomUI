using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Markup;
using WpfCustomUI.Controls;

namespace WpfCustomUI.Controls.Tests.Ribbon;

/// <summary>リボンの構造ロジック検証(spec 6.27.5)。UI 要素の生成のみ行う(表示はしない)。</summary>
public class WcuRibbonTests
{
    /// <summary>WPF 要素の生成は STA スレッドで行う。</summary>
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    [Fact]
    public void RibbonTab_GroupsIsContentProperty()
    {
        var attribute = (ContentPropertyAttribute?)Attribute.GetCustomAttribute(
            typeof(WcuRibbonTab), typeof(ContentPropertyAttribute));
        Assert.NotNull(attribute);
        Assert.Equal(nameof(WcuRibbonTab.Groups), attribute.Name);
    }

    [Fact]
    public void RibbonTab_GroupsAreHostedInContentItemsControl()
    {
        RunSta(() =>
        {
            var tab = new WcuRibbonTab();
            var group = new WcuRibbonGroup { Header = "テスト" };
            tab.Groups.Add(group);

            var host = Assert.IsAssignableFrom<ItemsControl>(tab.Content);
            Assert.Same(tab.Groups, host.ItemsSource);
            Assert.Single(tab.Groups);
        });
    }

    [Fact]
    public void Ribbon_GeneratesRibbonTabContainers()
    {
        RunSta(() =>
        {
            var ribbon = new WcuRibbon();
            var tab = new WcuRibbonTab { Header = "モデル" };
            ribbon.Items.Add(tab);

            // WcuRibbonTab はそれ自身がコンテナ(TabControl の TabItem と同じ流儀)
            Assert.True(ribbon.Items.Count == 1);
            Assert.Same(tab, ribbon.Items[0]);
        });
    }

    [Fact]
    public void Ribbon_ApplicationLabelDefaultsToEnglish()
    {
        RunSta(() =>
        {
            // ライブラリは内蔵文字列を持たない(既定は英語。spec 5)
            var ribbon = new WcuRibbon();
            Assert.Equal("File", ribbon.ApplicationLabel);
        });
    }

    [Fact]
    public void ButtonAssist_IconRoundTrips()
    {
        RunSta(() =>
        {
            var button = new Button();
            Assert.Null(ButtonAssist.GetIcon(button));

            ButtonAssist.SetIcon(button, WcuIcons.Play);
            Assert.Same(WcuIcons.Play, ButtonAssist.GetIcon(button));

            Assert.Equal(16.0, ButtonAssist.GetIconSize(button));
            ButtonAssist.SetIconSize(button, 24.0);
            Assert.Equal(24.0, ButtonAssist.GetIconSize(button));
        });
    }
}
