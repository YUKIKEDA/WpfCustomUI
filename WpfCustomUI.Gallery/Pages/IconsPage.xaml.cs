using System.Windows.Controls;
using System.Windows.Media;
using WpfCustomUI.Controls;

namespace WpfCustomUI.Gallery.Pages;

public partial class IconsPage : UserControl
{
    public sealed record IconItem(string Name, Geometry Geometry);

    public IconsPage()
    {
        InitializeComponent();

        var items = WcuIcons.Names
            .Select(name => new IconItem(name, WcuIcons.Get(name)))
            .ToList();
        IconList.ItemsSource = items;
        IconCountHeader.Text = $"一覧 (全 {items.Count} 種)";
    }
}
