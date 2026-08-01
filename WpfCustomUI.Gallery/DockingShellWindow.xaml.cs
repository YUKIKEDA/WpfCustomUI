using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvalonDock.Layout;
using WpfCustomUI.Controls;
using WpfCustomUI.Docking;

namespace WpfCustomUI.Gallery
{
    /// <summary>
    /// ドッキングシステムのフルシェルデモ(spec 6.13.6)。
    /// CAE アプリの典型構成(モデルツリー / ビューポート / プロパティ / 凡例 / ログ)を
    /// AvalonDock + WcuDockTheme で組み、レイアウトの保存・復元も体験できる。
    /// </summary>
    public partial class DockingShellWindow : WcuWindow
    {
        private static readonly string LayoutPath =
            Path.Combine(Path.GetTempPath(), "WpfCustomUI.Gallery.docklayout.xml");

        private readonly LogBuffer _logBuffer = new(capacity: 2000);
        private readonly Dictionary<string, object> _contentMap;
        private string? _defaultLayout;

        public DockingShellWindow()
        {
            InitializeComponent();

            // ContentId → ペイン内容。レイアウト復元時のリゾルバが参照する
            _contentMap = new Dictionary<string, object>
            {
                ["Shell.ModelTree"] = Tree,
                ["Shell.Properties"] = Props,
                ["Shell.Legend"] = LegendHost,
                ["Shell.Log"] = Console,
                ["Shell.Doc.Case1"] = ViewportCase1,
                ["Shell.Doc.Case2"] = ViewportCase2,
            };

            PopulateModelTree();
            PopulatePropertyGrid();

            Legend.Scale = new ColorScale { ColorMap = ColorMap.Jet, Minimum = 0, Maximum = 350 };

            Console.Source = _logBuffer;
            _logBuffer.Append(LogLevel.Info, "ドッキングシェルを初期化しました");
            _logBuffer.Append(LogLevel.Info, "タブをドラッグして再配置、ウィンドウ外へドロップでフローティング化できます");

            // 起動直後のレイアウトを「既定」として控えておく(リセット用)
            Loaded += (_, _) => _defaultLayout ??= DockLayout.SaveToString(Dock);
        }

        private void OnSaveLayout(object sender, RoutedEventArgs e)
        {
            DockLayout.Save(Dock, LayoutPath);
            _logBuffer.Append(LogLevel.Info, $"レイアウトを保存しました: {LayoutPath}");
            StatusMessage.Content = "レイアウトを保存しました";
        }

        private void OnLoadLayout(object sender, RoutedEventArgs e)
        {
            if (DockLayout.Load(Dock, LayoutPath, ResolveContent))
            {
                _logBuffer.Append(LogLevel.Info, "保存済みレイアウトを復元しました");
                StatusMessage.Content = "レイアウトを復元しました";
            }
            else
            {
                _logBuffer.Append(LogLevel.Warning, "保存済みレイアウトがありません(先に「レイアウトを保存」を実行してください)");
                StatusMessage.Content = "保存済みレイアウトがありません";
            }
        }

        private void OnResetLayout(object sender, RoutedEventArgs e)
        {
            if (_defaultLayout is not null)
            {
                DockLayout.LoadFromString(Dock, _defaultLayout, ResolveContent);
                _logBuffer.Append(LogLevel.Info, "既定のレイアウトに戻しました");
                StatusMessage.Content = "既定のレイアウトに戻しました";
            }
        }

        private void OnShowToolWindow(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { Tag: string contentId })
            {
                return;
            }

            // 表示中に加え、非表示(Hidden)のツールウィンドウも検索対象にする
            var anchorable = Dock.Layout.Descendents().OfType<LayoutAnchorable>()
                .Concat(Dock.Layout.Hidden)
                .FirstOrDefault(a => a.ContentId == contentId);

            if (anchorable is not null)
            {
                anchorable.Show();
                anchorable.IsActive = true;
            }
        }

        private object? ResolveContent(string contentId) => _contentMap.GetValueOrDefault(contentId);

        private void PopulateModelTree()
        {
            var assembly = new TreeNode { Name = "Assembly", IsExpanded = true };
            foreach (var name in new[] { "Bracket-01", "Bracket-02", "Base-Plate" })
            {
                var part = new TreeNode { Name = name };
                part.Children.Add(new TreeNode { Name = "メッシュ" });
                part.Children.Add(new TreeNode { Name = "材料: SS400" });
                assembly.Children.Add(part);
            }

            var analysis = new TreeNode { Name = "解析ケース", IsExpanded = true };
            analysis.Children.Add(new TreeNode { Name = "静解析 (Case 1)" });
            analysis.Children.Add(new TreeNode { Name = "固有値解析 (Case 2)" });

            Tree.ItemsSource = new[] { assembly, analysis };
        }

        private void PopulatePropertyGrid()
        {
            Props.ItemsSource = new PropertyItem[]
            {
                new TextPropertyItem
                {
                    Name = "名前", Category = "一般", Value = "Bracket-01",
                },
                new BoolPropertyItem
                {
                    Name = "表示", Category = "一般", Value = true,
                },
                new ChoicePropertyItem
                {
                    Name = "材料", Category = "材料",
                    Choices = new[] { "Steel SS400", "Aluminum A5052" },
                    Value = "Steel SS400",
                },
                new NumericPropertyItem
                {
                    Name = "ヤング率", Category = "材料", Value = 205, Unit = "GPa", Minimum = 0,
                },
                new ColorPropertyItem
                {
                    Name = "パーツ色", Category = "表示",
                    Value = Color.FromArgb(0xFF, 0x00, 0x78, 0xD7),
                },
            };
        }
    }
}
