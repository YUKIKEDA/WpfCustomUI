using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;
using WpfCustomUI.Controls;

namespace WpfCustomUI.Gallery.Pages
{
    public partial class ShellPage : UserControl
    {
        private static readonly string[] Parts =
        [
            "Bracket-01", "Bracket-02", "Base-Plate", "Housing-Front", "Housing-Rear",
            "Shaft-Main", "Shaft-Sub", "Bearing-6204", "Bearing-6205", "Bolt-M8x20",
            "Bolt-M10x30", "Nut-M8", "Washer-8", "Cover-Top", "Cover-Bottom",
            "Gasket-A", "Gasket-B", "Flange-DN50", "Pipe-Inlet", "Pipe-Outlet",
        ];

        private readonly ICollectionView _partsView;

        public ShellPage()
        {
            InitializeComponent();

            PartsList.ItemsSource = Parts;
            _partsView = CollectionViewSource.GetDefaultView(PartsList.ItemsSource);

            // デバウンス確定後の SearchText でのみフィルタが走る
            DependencyPropertyDescriptor
                .FromProperty(SearchBox.SearchTextProperty, typeof(SearchBox))
                .AddValueChanged(PartsSearch, (_, _) => ApplyFilter());
        }

        private void ApplyFilter()
        {
            var text = PartsSearch.SearchText;
            SearchEcho.Text = string.IsNullOrEmpty(text) ? "(なし)" : text;
            _partsView.Filter = string.IsNullOrWhiteSpace(text)
                ? null
                : o => o is string s && s.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
