using System.Windows;
using System.Windows.Controls;
using WpfCustomUI.Controls;
using WpfCustomUI.Controls.Theming;
using WpfCustomUI.Gallery.Pages;

namespace WpfCustomUI.Gallery
{
    public partial class MainWindow : WcuWindow
    {
        private static readonly (string Title, Func<object> CreatePage)[] Pages =
        [
            ("Design Tokens", () => new TokensPage()),
            ("Inputs & Buttons", () => new InputsPage()),
            ("Navigation & Menus", () => new NavigationPage()),
            ("NumericBox & Expander", () => new NumericPage()),
            ("Pickers & Range", () => new PickersPage()),
            ("PropertyGrid", () => new PropertyGridPage()),
            ("ModelTree", () => new ModelTreePage()),
            ("ColorMap 凡例", () => new ColorMapPage()),
            ("Post-processing", () => new PostProcessingPage()),
            ("LogConsole & Progress", () => new LogConsolePage()),
            ("Shell", () => new ShellPage()),
            ("Windows & Dialogs", () => new WindowsPage()),
            ("DataGrid", () => new DataGridPage()),
            ("More Controls", () => new MoreControlsPage()),
            ("Misc Inputs & Wizard", () => new MiscInputsPage()),
            ("Docking", () => new DockingPage()),
            ("Charts", () => new ChartsPage()),
            ("3D Viewport", () => new Viewport3DPage()),
            ("3D Deformation", () => new ViewportDeformationPage()),
        ];

        public MainWindow()
        {
            InitializeComponent();

            foreach (var (title, _) in Pages)
            {
                NavList.Items.Add(title);
            }

            NavList.SelectedIndex = 0;

            // --light: ライトテーマで起動する(見た目確認・スクリーンショット用)
            if (Environment.GetCommandLineArgs().Contains("--light"))
            {
                ThemeToggle.IsChecked = true;
            }

            // --msgbox: 起動直後に WcuMessageBox を表示する(見た目確認・スクリーンショット用)
            if (Environment.GetCommandLineArgs().Contains("--msgbox"))
            {
                Loaded += (_, _) => Dispatcher.BeginInvoke(() => WcuMessageBox.Show(
                    this,
                    "モデルが変更されています。保存しますか?",
                    "確認",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question));
            }

            // --dockshell: 起動直後にドッキングシェルデモを開く(見た目確認・スクリーンショット用)
            if (Environment.GetCommandLineArgs().Contains("--dockshell"))
            {
                Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
                    new DockingShellWindow { Owner = this }.Show());
            }

            // --smoke: 全ページを一度生成して XAML・リソース解決のエラーを検出し、即終了する
            if (Environment.GetCommandLineArgs().Contains("--smoke"))
            {
                foreach (var (_, createPage) in Pages)
                {
                    _ = createPage();
                }

                // ドッキングシェルも XAML・テーマ解決の検証対象に含める
                _ = new DockingShellWindow();

                Application.Current.Shutdown(0);
            }
        }

        private void ThemeToggle_Checked(object sender, RoutedEventArgs e) =>
            ThemeManager.SetTheme(WcuThemeVariant.Light);

        private void ThemeToggle_Unchecked(object sender, RoutedEventArgs e) =>
            ThemeManager.SetTheme(WcuThemeVariant.Dark);

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavList.SelectedIndex >= 0)
            {
                PageHost.Content = Pages[NavList.SelectedIndex].CreatePage();
            }
        }
    }
}
