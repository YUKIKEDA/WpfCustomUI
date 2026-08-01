using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace WpfCustomUI.Gallery.Pages
{
    public partial class DataGridPage : UserControl
    {
        private static readonly string[] MaterialTypes = ["金属", "樹脂", "複合材", "セラミックス"];

        private readonly ObservableCollection<MaterialRow> _materials;

        public DataGridPage()
        {
            InitializeComponent();

            _materials =
            [
                new("Steel (S45C)", "金属", 205, 0.29, 7850),
                new("Aluminum (A5052)", "金属", 70.3, 0.33, 2680),
                new("Titanium (Ti-6Al-4V)", "金属", 113.8, 0.34, 4430),
                new("Copper (C1100)", "金属", 117, 0.34, 8940),
                new("Stainless (SUS304)", "金属", 193, 0.29, 8000),
                new("Cast Iron (FC250)", "金属", 100, 0.26, 7200),
                new("ABS", "樹脂", 2.3, 0.35, 1050),
                new("Polycarbonate", "樹脂", 2.4, 0.37, 1200),
                new("PEEK", "樹脂", 3.6, 0.38, 1300),
                new("Nylon 66", "樹脂", 2.9, 0.39, 1140),
                new("CFRP (UD 0°)", "複合材", 135, 0.30, 1600),
                new("GFRP", "複合材", 38.6, 0.26, 1900),
                new("Alumina (Al2O3)", "セラミックス", 370, 0.22, 3900),
                new("Silicon Carbide", "セラミックス", 410, 0.14, 3100),
                new("Zirconia", "セラミックス", 200, 0.31, 6050),
            ];

            TypeColumn.ItemsSource = MaterialTypes;
            Grid.ItemsSource = _materials;
        }

        private void DetailsCheck_Changed(object sender, RoutedEventArgs e)
        {
            // XAML パース中(IsChecked 初期値設定時)はまだ Grid が生成されていない
            if (Grid is null)
            {
                return;
            }

            var enabled = DetailsCheck.IsChecked == true;
            DetailsColumn.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            Grid.RowDetailsTemplate = enabled
                ? (DataTemplate)Resources["MaterialDetailsTemplate"]
                : null;

            if (!enabled)
            {
                // 開いたままの行詳細を閉じる(実体化済みの行のみで十分)
                foreach (var item in Grid.Items)
                {
                    if (Grid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
                    {
                        row.ClearValue(DataGridRow.DetailsVisibilityProperty);
                    }
                }
            }
        }

        private void GroupCheck_Changed(object sender, System.Windows.RoutedEventArgs e)
        {
            // XAML パース中(IsChecked 初期値設定時)はまだ Grid が生成されていない
            if (Grid?.ItemsSource is null)
            {
                return;
            }

            var view = CollectionViewSource.GetDefaultView(Grid.ItemsSource);
            view.GroupDescriptions.Clear();
            if (GroupCheck.IsChecked == true)
            {
                view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MaterialRow.Type)));
            }
        }

    }

    /// <summary>デモ用の材料行。IDataErrorInfo で物性値の検証エラーを表現する。</summary>
    public sealed class MaterialRow : INotifyPropertyChanged, IDataErrorInfo
    {
        private string _name;
        private string _type;
        private double _youngsModulus;
        private double _poissonRatio;
        private double _density;
        private bool _isActive = true;

        public MaterialRow(string name, string type, double youngsModulus, double poissonRatio, double density)
        {
            _name = name;
            _type = type;
            _youngsModulus = youngsModulus;
            _poissonRatio = poissonRatio;
            _density = density;
        }

        public string Name
        {
            get => _name;
            set => Set(ref _name, value);
        }

        public string Type
        {
            get => _type;
            set => Set(ref _type, value);
        }

        public double YoungsModulus
        {
            get => _youngsModulus;
            set
            {
                if (Set(ref _youngsModulus, value))
                {
                    OnPropertyChanged(nameof(ShearModulus));
                }
            }
        }

        public double PoissonRatio
        {
            get => _poissonRatio;
            set
            {
                if (Set(ref _poissonRatio, value))
                {
                    OnPropertyChanged(nameof(ShearModulus));
                }
            }
        }

        public double Density
        {
            get => _density;
            set => Set(ref _density, value);
        }

        public bool IsActive
        {
            get => _isActive;
            set => Set(ref _isActive, value);
        }

        /// <summary>せん断弾性率 G = E / 2(1+ν)(RowDetails 表示用)。</summary>
        public double ShearModulus => _youngsModulus / (2 * (1 + _poissonRatio));

        public string Error => string.Empty;

        public string this[string columnName] => columnName switch
        {
            nameof(YoungsModulus) when _youngsModulus <= 0 => "ヤング率は正の値を指定してください。",
            nameof(PoissonRatio) when _poissonRatio is <= -1 or >= 0.5 => "ポアソン比は -1 < ν < 0.5 の範囲で指定してください。",
            nameof(Density) when _density <= 0 => "密度は正の値を指定してください。",
            _ => string.Empty,
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged(string? propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
