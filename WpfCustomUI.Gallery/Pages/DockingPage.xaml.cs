using System.Windows;
using System.Windows.Controls;

namespace WpfCustomUI.Gallery.Pages
{
    public partial class DockingPage : UserControl
    {
        public DockingPage()
        {
            InitializeComponent();
        }

        private void OnLaunchShell(object sender, RoutedEventArgs e)
        {
            var window = new DockingShellWindow
            {
                Owner = Window.GetWindow(this),
            };
            window.Show();
        }
    }
}
