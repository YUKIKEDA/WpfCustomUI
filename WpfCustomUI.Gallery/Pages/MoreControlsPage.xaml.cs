using System.Windows;
using System.Windows.Controls;

namespace WpfCustomUI.Gallery.Pages
{
    public partial class MoreControlsPage : UserControl
    {
        private sealed record Material(string Name, double YoungsModulus, double PoissonsRatio, double Density);

        public MoreControlsPage()
        {
            InitializeComponent();

            MaterialList.ItemsSource = new[]
            {
                new Material("構造用鋼 (S45C)", 205, 0.29, 7850),
                new Material("アルミ合金 (A6061-T6)", 68.9, 0.33, 2700),
                new Material("チタン合金 (Ti-6Al-4V)", 113.8, 0.342, 4430),
                new Material("銅 (C1100)", 117, 0.34, 8940),
                new Material("PEEK", 3.6, 0.38, 1300),
                new Material("窒化ケイ素 (Si3N4)", 310, 0.27, 3250),
            };
        }

        private void Reopen_Click(object sender, RoutedEventArgs e)
        {
            InfoBarInfo.IsOpen = true;
            InfoBarSuccess.IsOpen = true;
            InfoBarWarning.IsOpen = true;
            InfoBarError.IsOpen = true;
            InfoBarStatus.Text = "";
        }

        private void InfoBar_Closed(object sender, RoutedEventArgs e)
        {
            InfoBarStatus.Text = "エラーの InfoBar が閉じられました(Closed イベント)";
        }

        private void Retry_Click(object sender, RoutedEventArgs e)
        {
            InfoBarStatus.Text = "再試行アクションが実行されました";
        }

        private void HelpLink_Click(object sender, RoutedEventArgs e)
        {
            LinkStatus.Text = "ヘルプリンクがクリックされました";
        }
    }
}
