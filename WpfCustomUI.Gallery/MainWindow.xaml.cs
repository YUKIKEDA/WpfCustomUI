using System.Windows;
using System.Windows.Controls;
using WpfCustomUI.Gallery.Pages;

namespace WpfCustomUI.Gallery
{
    public partial class MainWindow : Window
    {
        private static readonly (string Title, Func<object> CreatePage)[] Pages =
        [
            ("Design Tokens", () => new TokensPage()),
        ];

        public MainWindow()
        {
            InitializeComponent();

            foreach (var (title, _) in Pages)
            {
                NavList.Items.Add(title);
            }

            NavList.SelectedIndex = 0;
        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavList.SelectedIndex >= 0)
            {
                PageHost.Content = Pages[NavList.SelectedIndex].CreatePage();
            }
        }
    }
}
