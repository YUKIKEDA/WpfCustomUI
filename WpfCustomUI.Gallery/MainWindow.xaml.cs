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
            ("Inputs & Buttons", () => new InputsPage()),
            ("Navigation & Menus", () => new NavigationPage()),
            ("NumericBox & Expander", () => new NumericPage()),
            ("PropertyGrid", () => new PropertyGridPage()),
            ("ModelTree", () => new ModelTreePage()),
            ("ColorMap 凡例", () => new ColorMapPage()),
            ("LogConsole & Progress", () => new LogConsolePage()),
        ];

        public MainWindow()
        {
            InitializeComponent();

            foreach (var (title, _) in Pages)
            {
                NavList.Items.Add(title);
            }

            NavList.SelectedIndex = 0;

            // --smoke: 全ページを一度生成して XAML・リソース解決のエラーを検出し、即終了する
            if (Environment.GetCommandLineArgs().Contains("--smoke"))
            {
                foreach (var (_, createPage) in Pages)
                {
                    _ = createPage();
                }

                Application.Current.Shutdown(0);
            }
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
