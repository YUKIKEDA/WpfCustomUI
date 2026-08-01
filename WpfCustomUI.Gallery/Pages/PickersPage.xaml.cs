using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WpfCustomUI.Controls;

namespace WpfCustomUI.Gallery.Pages
{
    public partial class PickersPage : UserControl
    {
        /// <summary>RangeSlider と凡例(ColorScale)の連動デモ用。</summary>
        private readonly ColorScale _scale = new()
        {
            ColorMap = ColorMap.Jet,
            Minimum = 100,
            Maximum = 350,
            BelowRangeColor = Colors.Magenta,
            AboveRangeColor = Colors.White,
        };

        public PickersPage()
        {
            InitializeComponent();

            RangeLegend.Scale = _scale;

            // RangeSlider の値を ColorScale の Min/Max に双方向で連動させる
            ContourRange.SetBinding(RangeSlider.LowerValueProperty,
                new Binding(nameof(ColorScale.Minimum)) { Source = _scale, Mode = BindingMode.TwoWay });
            ContourRange.SetBinding(RangeSlider.UpperValueProperty,
                new Binding(nameof(ColorScale.Maximum)) { Source = _scale, Mode = BindingMode.TwoWay });
        }

        private void ViewPreset_Click(object sender, RoutedEventArgs e) =>
            DropDownResult.Text = $"ビュー切替: {((MenuItem)sender).Header}";

        private void SolveOption_Click(object sender, RoutedEventArgs e) =>
            DropDownResult.Text = $"実行オプション: {((MenuItem)sender).Header}";

        private void Solve_Click(object sender, RoutedEventArgs e) =>
            DropDownResult.Text = "解析実行(既定の設定)";
    }
}
