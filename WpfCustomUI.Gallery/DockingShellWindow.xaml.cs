using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AvalonDock.Layout;
using WpfCustomUI.Controls;
using WpfCustomUI.Docking;
using WpfCustomUI.Viewport3D;

namespace WpfCustomUI.Gallery
{
    /// <summary>
    /// 統合ミニ CAE シェル(spec 6.25)。
    /// 実 WcuViewport の 2 ドキュメント(静解析 Kirsch / 過渡応答 片持ち梁)を中央に置き、
    /// ModelTree 双方向選択同期・PropertyGrid 選択連動・プローブ→LogConsole・
    /// ColorMapLegend+ColorScaleEditor 共有・PlaybackBar・StatusBar 連動の
    /// 「全部つないだ」参照実装+相互運用検証を兼ねる。
    /// </summary>
    public partial class DockingShellWindow : WcuWindow
    {
        private static readonly string LayoutPath =
            Path.Combine(Path.GetTempPath(), "WpfCustomUI.Gallery.docklayout.xml");

        /// <summary>ドキュメント 1 枚分の状態(ビューポート・カラースケール・ツリー対応)。</summary>
        private sealed record ShellDocument(
            string Title,
            string LegendTitle,
            WcuViewport Viewport,
            ColorScale Scale,
            FrameworkElement Content,
            Dictionary<TreeNode, ViewportMesh> NodeToMesh,
            Dictionary<ViewportMesh, TreeNode> MeshToNode);

        private readonly LogBuffer _logBuffer = new(capacity: 2000);
        private readonly Dictionary<string, object> _contentMap;
        private readonly List<ShellDocument> _documents = [];
        private readonly ObservableCollection<ViewportMesh> _staticMeshes = [];
        private readonly ObservableCollection<ViewportMesh> _transientMeshes = [];
        private readonly ViewportMesh _transientPlate;
        private readonly double[][] _transientFrames;
        private string? _defaultLayout;
        private ShellDocument? _activeDocument;

        /// <summary>ツリー⇔ビューポートの双方向選択同期の再入ガード(spec 6.25.3)。</summary>
        private bool _syncingSelection;

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
                ["Shell.Doc.Case1"] = StaticDoc,
                ["Shell.Doc.Case2"] = TransientDoc,
            };

            // ---- 静解析ドキュメント(Kirsch 円孔平板+ボス) ----
            var staticScene = ShellScenes.CreateStaticScene();
            _staticMeshes.Add(staticScene.Plate);
            _staticMeshes.Add(staticScene.Boss);

            var staticScale = new ColorScale
            {
                ColorMap = ColorMap.Jet,
                Minimum = 0.0,
                Maximum = 3.0 * ShellScenes.NominalStress, // 孔縁の応力集中係数 3
            };
            ViewportStatic.ColorScale = staticScale;
            ViewportStatic.MeshSource = _staticMeshes;

            // 単位付きラベル書式(物理量の意味づけ=アプリ責務)。UIA が値をパースするため invariant
            ViewportStatic.ProbeLabelFormatter = r =>
                FormattableString.Invariant($"σ_vM = {r.ScalarValue:0.0} MPa (N{r.NodeIndex})");
            ViewportStatic.ProbePicked += OnProbePicked;

            // ---- 過渡応答ドキュメント(片持ち梁の減衰自由振動) ----
            var transientScene = ShellScenes.CreateTransientScene();
            _transientPlate = transientScene.Plate;
            _transientFrames = transientScene.Frames;
            _transientMeshes.Add(_transientPlate);

            var transientScale = new ColorScale
            {
                ColorMap = ColorMap.Jet,
                Minimum = 0.0,
                Maximum = ShellScenes.TipAmplitude,
            };
            ViewportTransient.ColorScale = transientScale;
            ViewportTransient.MeshSource = _transientMeshes;
            Playback.FrameCount = ShellScenes.TransientFrameCount;

            // ---- ドキュメントコンテキスト+モデルツリー(spec 6.25.3) ----
            _documents.Add(CreateDocument(
                "静解析: 円孔付き平板", "応力 [MPa]", ViewportStatic, staticScale, StaticDoc,
                [staticScene.Plate, staticScene.Boss]));
            _documents.Add(CreateDocument(
                "過渡応答: 片持ち梁", "|u| [mm]", ViewportTransient, transientScale, TransientDoc,
                [_transientPlate]));

            Tree.ItemsSource = BuildModelTree();
            Tree.SelectionChanged += OnTreeSelectionChanged;

            foreach (var document in _documents)
            {
                var doc = document;
                doc.Viewport.SelectionChanged += (_, _) => OnViewportSelectionChanged(doc);
                doc.Viewport.HoverChanged += (_, _) => OnViewportHoverChanged(doc);
                doc.Viewport.GeometryBuildCompleted += (_, _) => OnGeometryBuildCompleted(doc);
            }

            // ---- 凡例+カラースケールエディタ(アクティブドキュメントと共有。spec 6.25.5) ----
            SetActiveDocument(_documents[0]);
            Dock.ActiveContentChanged += OnActiveContentChanged;

            ShowViewportProperties(_documents[0]);

            Console.Source = _logBuffer;
            _logBuffer.Append(LogLevel.Info, "統合 CAE シェルを初期化しました(実 WcuViewport ×2)");
            _logBuffer.Append(LogLevel.Info, "ツリー選択⇔3D パーツピックは双方向同期、プローブ結果はこのログに記録されます");

            // 起動直後のレイアウトを「既定」として控えておく(リセット用)
            Loaded += (_, _) => _defaultLayout ??= DockLayout.SaveToString(Dock);
        }

        // ================= モデルツリー構築+同期(spec 6.25.3) =================

        private ShellDocument CreateDocument(
            string title, string legendTitle, WcuViewport viewport, ColorScale scale,
            FrameworkElement content, IReadOnlyList<ViewportMesh> parts)
        {
            var nodeToMesh = new Dictionary<TreeNode, ViewportMesh>();
            var meshToNode = new Dictionary<ViewportMesh, TreeNode>();
            foreach (var mesh in parts)
            {
                var node = new TreeNode { Name = mesh.Name ?? "(無名)", IsVisible = mesh.IsVisible };
                node.PropertyChanged += OnTreeNodePropertyChanged;
                nodeToMesh[node] = mesh;
                meshToNode[mesh] = node;
            }

            return new ShellDocument(title, legendTitle, viewport, scale, content, nodeToMesh, meshToNode);
        }

        private TreeNode[] BuildModelTree() =>
            [.. _documents.Select(doc =>
            {
                var root = new TreeNode { Name = doc.Title, IsExpanded = true };
                foreach (var node in doc.NodeToMesh.Keys)
                {
                    root.Children.Add(node);
                }

                return root;
            })];

        /// <summary>目アイコン(可視性)と名前変更(F2)をメッシュへ反映する。</summary>
        private void OnTreeNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not TreeNode node || FindMesh(node) is not { } mesh)
            {
                return;
            }

            if (e.PropertyName == nameof(TreeNode.IsVisible))
            {
                mesh.IsVisible = node.IsVisible != false;
            }
            else if (e.PropertyName == nameof(TreeNode.Name))
            {
                mesh.Name = node.Name;
            }
        }

        private ViewportMesh? FindMesh(TreeNode node) =>
            _documents.Select(d => d.NodeToMesh.GetValueOrDefault(node)).FirstOrDefault(m => m is not null);

        /// <summary>ツリー選択→各ビューポートのパーツ選択へ反映(片翼)。</summary>
        private void OnTreeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingSelection)
            {
                return;
            }

            _syncingSelection = true;
            try
            {
                var selected = Tree.GetSelectedNodes().OfType<TreeNode>().ToHashSet();
                foreach (var doc in _documents)
                {
                    doc.Viewport.Selection.Clear();
                    foreach (var (node, mesh) in doc.NodeToMesh)
                    {
                        if (selected.Contains(node))
                        {
                            doc.Viewport.Selection.AddPart(mesh);
                        }
                    }
                }

                UpdatePropertyTarget();
                UpdateSelectionStatus();
            }
            finally
            {
                _syncingSelection = false;
            }
        }

        /// <summary>ビューポートのピック→ツリー選択へ反映(逆翼)。</summary>
        private void OnViewportSelectionChanged(ShellDocument doc)
        {
            if (_syncingSelection)
            {
                UpdateSelectionStatus();
                return;
            }

            _syncingSelection = true;
            try
            {
                // この doc のパーツ選択だけをツリーへ反映する(他ドキュメントのノードは触らない)
                foreach (var (node, mesh) in doc.NodeToMesh)
                {
                    node.IsSelected = doc.Viewport.Selection.IsPartSelected(mesh);
                }

                UpdatePropertyTarget();
                UpdateSelectionStatus();
            }
            finally
            {
                _syncingSelection = false;
            }
        }

        private void UpdateSelectionStatus()
        {
            var parts = _documents.Sum(d => d.Viewport.Selection.PartCount);
            var faces = _documents.Sum(d => d.Viewport.Selection.FaceCount);
            var nodes = _documents.Sum(d => d.Viewport.Selection.NodeCount);
            SelectionStatus.Text = parts + faces + nodes == 0
                ? "選択: なし"
                : string.Create(CultureInfo.InvariantCulture,
                    $"選択: パーツ {parts} / 面 {faces} / 節点 {nodes}");
        }

        private void OnViewportHoverChanged(ShellDocument doc)
        {
            HoverStatus.Text = doc.Viewport.HoverInfo switch
            {
                null => "ホバー: -",
                { IsPart: true } h => $"ホバー: {h.Mesh.Name}",
                { NodeIndex: >= 0 } h => $"ホバー: {h.Mesh.Name} 節点 {h.NodeIndex}",
                { } h => $"ホバー: {h.Mesh.Name} 面 {h.TriangleIndex}",
            };
        }

        // ================= PropertyGrid 選択連動(spec 6.25.4) =================

        /// <summary>選択パーツがあればそのプロパティ、なければアクティブドキュメントの設定を表示。</summary>
        private void UpdatePropertyTarget()
        {
            foreach (var doc in _documents)
            {
                var mesh = doc.Viewport.Selection.SelectedParts.FirstOrDefault();
                if (mesh is not null && doc.MeshToNode.TryGetValue(mesh, out var node))
                {
                    ShowPartProperties(doc, mesh, node);
                    return;
                }
            }

            ShowViewportProperties(_activeDocument ?? _documents[0]);
        }

        /// <summary>選択パーツの表示プロパティ(編集は即 3D 反映)。</summary>
        private void ShowPartProperties(ShellDocument doc, ViewportMesh mesh, TreeNode node)
        {
            var name = new TextPropertyItem
            {
                Name = "名前", Category = "一般", Value = mesh.Name,
                Description = "パーツ名(ツリー表示と共有)",
            };
            name.PropertyChanged += OnValueChanged(name, () =>
            {
                if (!string.IsNullOrWhiteSpace(name.Value))
                {
                    node.Name = name.Value; // ノード経由でメッシュにも反映される
                }
            });

            var visible = new BoolPropertyItem
            {
                Name = "表示", Category = "一般", Value = mesh.IsVisible,
                Description = "ツリーの目アイコンと同じ可視性",
            };
            visible.PropertyChanged += OnValueChanged(visible, () => Tree.SetVisibility(node, visible.Value));

            var color = new ColorPropertyItem
            {
                Name = "パーツ色", Category = "表示", Value = mesh.Color, IsAlphaEnabled = false,
                Description = "スカラー(コンター)を持たないパーツの単色",
            };
            color.PropertyChanged += OnValueChanged(color, () => mesh.Color = color.Value);

            var opacity = new NumericPropertyItem
            {
                Name = "不透明度", Category = "表示", Value = mesh.Opacity,
                Minimum = 0.1, Maximum = 1.0, Increment = 0.1, Format = "0.0",
                Description = "1.0 で不透明",
            };
            opacity.PropertyChanged += OnValueChanged(opacity, () =>
            {
                if (opacity.Value is { } v)
                {
                    mesh.Opacity = v;
                }
            });

            var edges = new BoolPropertyItem
            {
                Name = "エッジ表示", Category = "表示", Value = mesh.ShowEdges,
            };
            edges.PropertyChanged += OnValueChanged(edges, () => mesh.ShowEdges = edges.Value);

            Props.ItemsSource = new PropertyItem[] { name, visible, color, opacity, edges };
        }

        /// <summary>選択なし: アクティブドキュメントのビューポート設定を表示。</summary>
        private void ShowViewportProperties(ShellDocument doc)
        {
            var viewport = doc.Viewport;

            var deformScale = new NumericPropertyItem
            {
                Name = "変形スケール", Category = "ビューポート", Value = viewport.DeformationScale,
                Minimum = 0.0, Maximum = 1000.0, Increment = 1.0, Format = "0.#",
                Description = "変位 × スケールを GPU 頂点シェーダで適用",
            };
            deformScale.PropertyChanged += OnValueChanged(deformScale, () =>
            {
                if (deformScale.Value is { } v)
                {
                    viewport.DeformationScale = v;
                }
            });

            var ortho = new BoolPropertyItem
            {
                Name = "平行投影", Category = "ビューポート",
                Value = viewport.Projection == ViewportProjection.Orthographic,
            };
            ortho.PropertyChanged += OnValueChanged(ortho, () => viewport.Projection =
                ortho.Value ? ViewportProjection.Orthographic : ViewportProjection.Perspective);

            var contours = new BoolPropertyItem
            {
                Name = "コンター", Category = "ビューポート", Value = viewport.ShowContours,
            };
            contours.PropertyChanged += OnValueChanged(contours, () => viewport.ShowContours = contours.Value);

            var hover = new BoolPropertyItem
            {
                Name = "ホバープリハイライト", Category = "ビューポート",
                Value = viewport.IsHoverHighlightEnabled,
            };
            hover.PropertyChanged += OnValueChanged(hover, () => viewport.IsHoverHighlightEnabled = hover.Value);

            Props.ItemsSource = new PropertyItem[] { deformScale, ortho, contours, hover };
        }

        /// <summary>Value 変更時だけ apply を呼ぶ PropertyChanged ハンドラを作る。</summary>
        private static PropertyChangedEventHandler OnValueChanged(PropertyItem item, Action apply) =>
            (_, e) =>
            {
                if (e.PropertyName == nameof(TextPropertyItem.Value))
                {
                    apply();
                }
            };

        // ================= アクティブドキュメント連動(spec 6.25.5) =================

        private void OnActiveContentChanged(object? sender, EventArgs e)
        {
            var doc = _documents.FirstOrDefault(d => ReferenceEquals(Dock.ActiveContent, d.Content));
            if (doc is not null && !ReferenceEquals(doc, _activeDocument))
            {
                SetActiveDocument(doc);
                UpdatePropertyTarget();
            }
        }

        private void SetActiveDocument(ShellDocument doc)
        {
            _activeDocument = doc;
            Legend.Scale = doc.Scale;
            Legend.Title = doc.LegendTitle;
            ScaleEditor.Scale = doc.Scale;
            UpdateStatsStatus();
        }

        private void OnGeometryBuildCompleted(ShellDocument doc)
        {
            if (ReferenceEquals(doc, _activeDocument))
            {
                UpdateStatsStatus();
            }
        }

        private void UpdateStatsStatus()
        {
            if (_activeDocument is not { } doc)
            {
                return;
            }

            var stats = doc.Viewport.GetStatistics();
            StatsStatus.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"{doc.Title}: 三角形 {stats.TriangleCount:N0} / 構築 {stats.LastGeometryBuildTime.TotalMilliseconds:N0} ms");
        }

        // ================= プローブ→ログ(spec 6.25.5) =================

        private void OnProbePicked(object? sender, ProbePickedEventArgs e)
        {
            // Handled にはしない(ライブラリ既定の注釈追加はそのまま活かす)
            var label = ViewportStatic.ProbeLabelFormatter?.Invoke(e.Result) ?? e.Result.ToString();
            _logBuffer.Append(LogLevel.Info, $"プローブ: {label} @ ({e.Result.X:0.0}, {e.Result.Y:0.0}, {e.Result.Z:0.0})");
        }

        // ================= ドキュメント内 UI =================

        private void OnPickModeChanged(object sender, RoutedEventArgs e)
        {
            if (ViewportStatic is null)
            {
                return;
            }

            ViewportStatic.PickMode = true switch
            {
                _ when PickPartRadio.IsChecked == true => ViewportPickMode.Part,
                _ when PickFaceRadio.IsChecked == true => ViewportPickMode.Face,
                _ when PickNodeRadio.IsChecked == true => ViewportPickMode.Node,
                _ when PickProbeRadio.IsChecked == true => ViewportPickMode.Probe,
                _ => ViewportPickMode.None,
            };
        }

        private void OnClearSelection(object sender, RoutedEventArgs e)
        {
            foreach (var doc in _documents)
            {
                doc.Viewport.Selection.Clear();
            }
        }

        private void OnFitStatic(object sender, RoutedEventArgs e) => ViewportStatic.FitToView();

        private void OnFitTransient(object sender, RoutedEventArgs e) => ViewportTransient.FitToView();

        private void OnPlaybackFrameChanged(object sender, RoutedPropertyChangedEventArgs<int> e)
        {
            if (_transientFrames is null || e.NewValue < 0 || e.NewValue >= _transientFrames.Length)
            {
                return;
            }

            var t = ShellScenes.TransientDuration * e.NewValue / (ShellScenes.TransientFrameCount - 1);
            Playback.FrameLabel = $"t = {t:0.00} s";

            // Displacements の差し替えは変位バッファのみの部分更新(軽い経路)
            _transientPlate.Displacements = _transientFrames[e.NewValue];
        }

        // ================= レイアウト/ツールウィンドウ(Phase 13 から継続) =================

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
    }
}
