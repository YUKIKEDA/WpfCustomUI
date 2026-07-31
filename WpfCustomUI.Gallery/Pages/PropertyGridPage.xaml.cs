using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Controls;
using WpfCustomUI.Controls;

namespace WpfCustomUI.Gallery.Pages
{
    public partial class PropertyGridPage : UserControl
    {
        private readonly ObservableCollection<PropertyItem> _items = [];
        private readonly ChoicePropertyItem _analysisType;
        private readonly List<PropertyItem> _dynamicItems = [];
        private readonly NumericPropertyItem _thickness;

        public PropertyGridPage()
        {
            InitializeComponent();

            _items.Add(new TextPropertyItem
            {
                Name = "名前",
                Category = "一般",
                Description = "部品の表示名",
                Value = "Bracket-01",
            });
            _items.Add(new TextPropertyItem
            {
                Name = "ID",
                Category = "一般",
                Description = "内部識別子(編集不可)",
                Value = "PT-000123",
                IsReadOnly = true,
            });
            _items.Add(new BoolPropertyItem
            {
                Name = "表示",
                Category = "一般",
                Description = "3D ビューでの表示/非表示",
                Value = true,
            });

            _items.Add(new ChoicePropertyItem
            {
                Name = "材料",
                Category = "材料",
                Description = "材料データベースから選択",
                Choices = new[] { "Steel SS400", "Aluminum A5052", "Titanium Ti-6Al-4V" },
                Value = "Steel SS400",
            });
            _items.Add(new NumericPropertyItem
            {
                Name = "ヤング率",
                Category = "材料",
                Description = "縦弾性係数",
                Value = 205,
                Minimum = 0,
                Unit = "GPa",
            });
            _items.Add(new NumericPropertyItem
            {
                Name = "ポアソン比",
                Category = "材料",
                Description = "0〜0.5 の範囲",
                Value = 0.3,
                Minimum = 0,
                Maximum = 0.5,
                Increment = 0.05,
            });
            _thickness = new NumericPropertyItem
            {
                Name = "板厚",
                Category = "材料",
                Description = "表示は mm、内部値は m(\"500 mm\" のような単位付き入力も可)",
                Value = 0.0032,
                Minimum = 0,
                Maximum = 0.1,
                UnitProvider = new LengthUnitProvider(),
            };
            _items.Add(_thickness);

            _analysisType = new ChoicePropertyItem
            {
                Name = "解析タイプ",
                Category = "解析設定",
                Description = "選択すると下のパラメータが入れ替わります",
                Choices = new[] { "静解析", "熱伝導解析" },
                Value = "静解析",
            };
            _analysisType.PropertyChanged += OnAnalysisTypeChanged;
            _items.Add(_analysisType);

            RebuildDynamicItems();

            Grid1.ItemsSource = _items;

            _thickness.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(NumericPropertyItem.Value))
                {
                    UpdateMonitor();
                }
            };
            UpdateMonitor();
        }

        private void OnAnalysisTypeChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChoicePropertyItem.Value))
            {
                RebuildDynamicItems();
            }
        }

        private void RebuildDynamicItems()
        {
            foreach (var item in _dynamicItems)
            {
                _items.Remove(item);
            }

            _dynamicItems.Clear();

            if (Equals(_analysisType.Value, "静解析"))
            {
                _dynamicItems.Add(new NumericPropertyItem
                {
                    Name = "荷重",
                    Category = "解析設定",
                    Description = "先端に加える集中荷重",
                    Value = 1000,
                    Unit = "N",
                });
                _dynamicItems.Add(new NumericPropertyItem
                {
                    Name = "収束判定値",
                    Category = "解析設定",
                    Description = "残差ノルムのしきい値",
                    Value = 1e-6,
                    Format = "G3",
                });
            }
            else
            {
                _dynamicItems.Add(new NumericPropertyItem
                {
                    Name = "周囲温度",
                    Category = "解析設定",
                    Description = "環境温度",
                    Value = 25,
                    Unit = "℃",
                });
                _dynamicItems.Add(new NumericPropertyItem
                {
                    Name = "熱伝達率",
                    Category = "解析設定",
                    Description = "表面の対流熱伝達率",
                    Value = 10,
                    Unit = "W/m²K",
                });
            }

            foreach (var item in _dynamicItems)
            {
                _items.Add(item);
            }
        }

        private void UpdateMonitor() =>
            ValueMonitor.Text = $"板厚の内部値 = {_thickness.Value} m";
    }
}
