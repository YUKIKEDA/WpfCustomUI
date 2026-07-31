using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WpfCustomUI.Controls;
using WpfCustomUI.Gallery.Dialogs;

namespace WpfCustomUI.Gallery.Pages
{
    public partial class WindowsPage : UserControl
    {
        public WindowsPage()
        {
            InitializeComponent();
        }

        // ---------------- WcuMessageBox ----------------

        private void InfoMessage_Click(object sender, RoutedEventArgs e)
        {
            var result = WcuMessageBox.Show(
                Window.GetWindow(this),
                "解析が正常に完了しました。\n結果ファイル: results/case01.frd",
                "解析完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            MessageResult.Text = result.ToString();
        }

        private void ConfirmMessage_Click(object sender, RoutedEventArgs e)
        {
            var result = WcuMessageBox.Show(
                Window.GetWindow(this),
                "モデルが変更されています。保存しますか?",
                "確認",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            MessageResult.Text = result.ToString();
        }

        private void WarningMessage_Click(object sender, RoutedEventArgs e)
        {
            var result = WcuMessageBox.Show(
                Window.GetWindow(this),
                "メッシュ品質の低い要素が 12 個あります。このまま解析を続行しますか?",
                "警告",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            MessageResult.Text = result.ToString();
        }

        private void ErrorMessage_Click(object sender, RoutedEventArgs e)
        {
            var result = WcuMessageBox.Show(
                Window.GetWindow(this),
                "ソルバーが異常終了しました (exit code: -1073741819)。\nログを確認してください。",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            MessageResult.Text = result.ToString();
        }

        // ---------------- WcuWindow / WcuDialogWindow ----------------

        private void OpenToolWindow_Click(object sender, RoutedEventArgs e)
        {
            var menu = new Menu { VerticalAlignment = VerticalAlignment.Center };
            var fileMenu = new MenuItem { Header = "ファイル" };
            fileMenu.Items.Add(new MenuItem { Header = "開く..." });
            fileMenu.Items.Add(new MenuItem { Header = "保存" });
            var viewMenu = new MenuItem { Header = "表示" };
            viewMenu.Items.Add(new MenuItem { Header = "ワイヤーフレーム", IsCheckable = true });
            menu.Items.Add(fileMenu);
            menu.Items.Add(viewMenu);

            var window = new WcuWindow
            {
                Title = "ツールウィンドウ",
                Width = 520,
                Height = 340,
                Owner = Window.GetWindow(this),
                TitleBarContent = menu,
                Content = new TextBlock
                {
                    Text = "タイトルバーに Menu を埋め込んだ WcuWindow の例。\n"
                         + "最大化/スナップ/リサイズは OS 標準の挙動のまま。",
                    Margin = new Thickness(16),
                    TextWrapping = TextWrapping.Wrap,
                },
            };
            window.Show();
        }

        private void OpenDialog_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SampleDialog { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                Toasts.Show($"材料 \"{dialog.MaterialName}\" を追加しました。", ToastLevel.Success);
            }
        }

        // ---------------- ToastHost ----------------

        private void InfoToast_Click(object sender, RoutedEventArgs e) =>
            Toasts.Show("バックグラウンドでメッシュを生成しています。");

        private void SuccessToast_Click(object sender, RoutedEventArgs e) =>
            Toasts.Show("解析が完了しました。", ToastLevel.Success);

        private void WarningToast_Click(object sender, RoutedEventArgs e) =>
            Toasts.Show("接触面のペアが自動判定されました。設定を確認してください。", ToastLevel.Warning);

        private void ErrorToast_Click(object sender, RoutedEventArgs e) =>
            Toasts.Show("ライセンスサーバーへの接続が切断されました。", ToastLevel.Error, TimeSpan.FromSeconds(8));

        // ---------------- BusyOverlay ----------------

        private void StartBusy_Click(object sender, RoutedEventArgs e)
        {
            DefaultBusyOverlay.IsBusy = true;
            ProgressBusyOverlay.IsBusy = true;
            BusyProgress.IsRunning = true;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                DefaultBusyOverlay.IsBusy = false;
                ProgressBusyOverlay.IsBusy = false;
                BusyProgress.IsRunning = false;
            };
            timer.Start();
        }
    }
}
