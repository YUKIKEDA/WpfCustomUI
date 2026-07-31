using System.Windows.Controls;

namespace WpfCustomUI.Gallery.Pages
{
    public partial class NavigationPage : UserControl
    {
        public NavigationPage()
        {
            InitializeComponent();

            for (var i = 1; i <= 30; i++)
            {
                PartsList.Items.Add($"Part-{i:D3} (Solid)");
            }
        }
    }
}
