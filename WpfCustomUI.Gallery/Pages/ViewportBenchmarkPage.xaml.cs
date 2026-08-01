using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WpfCustomUI.Controls;
using WpfCustomUI.Viewport3D;

namespace WpfCustomUI.Gallery.Pages;

/// <summary>
/// 大規模メッシュ性能のベンチマークデモ(spec 6.22.6 / 6.23.6)。合成波面メッシュを
/// 10万〜2億三角形で生成し、構築時間/描画時間/LOD・カリング統計/実測 FPS を統計 API
/// (<see cref="WcuViewport.GetStatistics"/>)で表示する。ピック(面選択)は
/// チャンク基点オフセットの整合確認、回転アニメは操作中 LOD の動作確認を兼ねる。
/// </summary>
public partial class ViewportBenchmarkPage : UserControl
{
    private readonly ObservableCollection<ViewportMesh> _meshes = [];
    private readonly ColorScale _colorScale;
    private readonly DispatcherTimer _statsTimer;

    // 回転アニメ+実測 FPS(CompositionTarget.Rendering の実フレーム数を数える)
    private bool _isRotating;
    private DateTime _fpsWindowStart;
    private int _fpsFrameCount;

    public ViewportBenchmarkPage()
    {
        InitializeComponent();

        _colorScale = new ColorScale
        {
            ColorMap = ColorMap.Jet,
            Minimum = -1.0,
            Maximum = 1.0,
        };

        Viewport.MeshSource = _meshes;
        Viewport.ColorScale = _colorScale;
        Viewport.Selection.Changed += OnSelectionChanged;

        // 非同期構築の進捗表示(spec 6.24.2)。構築中も旧シーンの表示・操作は継続する
        Viewport.GeometryBuildProgressChanged += OnBuildProgressChanged;
        Viewport.GeometryBuildCompleted += OnBuildCompleted;

        // 構築は次の描画フレームで走る(遅延構築)ため、統計はタイマーで追従させる。
        // 回転アニメ中は CompositionTarget.Rendering が毎フレーム発火し Background だと
        // 飢餓するため、Normal 優先度で LOD 中も統計が更新されるようにする
        _statsTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _statsTimer.Tick += (_, _) => UpdateStats();

        Loaded += (_, _) =>
        {
            if (_meshes.Count == 0)
            {
                RebuildMesh();
            }

            _statsTimer.Start();
            RendererInfo.Text = Viewport.IsSoftwareRendering
                ? "レンダリング経路: WARP(ソフトウェア)+ WriteableBitmap フォールバック"
                : "レンダリング経路: ハードウェア D3D11 + D3DImage(共有サーフェス)";
        };
        Unloaded += (_, _) =>
        {
            _statsTimer.Stop();
            SetRotating(false);
        };
    }

    // ================= 合成波面メッシュ =================

    /// <summary>
    /// n×n 格子の波面(重ね合わせ正弦波)を目標三角形数で生成する。
    /// セル数 = (n−1)² × 2 三角形。座標・スカラー生成は行単位で並列化。
    /// </summary>
    private static ViewportMesh CreateWaveMesh(int targetTriangles)
    {
        var side = Math.Max((int)Math.Round(Math.Sqrt(targetTriangles / 2.0)), 8);
        var n = side + 1;

        var positions = new double[n * n * 3];
        var scalars = new double[n * n];
        const double extent = 100.0;
        var step = extent / side;

        Parallel.For(0, n, i =>
        {
            var x = -extent / 2.0 + i * step;
            for (var j = 0; j < n; j++)
            {
                var y = -extent / 2.0 + j * step;
                // 波数の異なる正弦波の重ね合わせ(視覚的な情報量と法線変化を作る)
                var z = 4.0 * Math.Sin(x * 0.12) * Math.Cos(y * 0.12)
                        + 1.5 * Math.Sin(x * 0.55 + y * 0.35);
                var v = i * n + j;
                positions[v * 3] = x;
                positions[v * 3 + 1] = y;
                positions[v * 3 + 2] = z;
                scalars[v] = z / 5.5; // おおよそ [-1, 1]
            }
        });

        var triangles = new int[side * side * 6];
        Parallel.For(0, side, i =>
        {
            for (var j = 0; j < side; j++)
            {
                var i00 = i * n + j;
                var i01 = i00 + 1;
                var i10 = i00 + n;
                var i11 = i10 + 1;
                var dst = (i * side + j) * 6;
                triangles[dst] = i00;
                triangles[dst + 1] = i10;
                triangles[dst + 2] = i11;
                triangles[dst + 3] = i00;
                triangles[dst + 4] = i11;
                triangles[dst + 5] = i01;
            }
        });

        return new ViewportMesh
        {
            Name = "合成波面",
            Positions = positions,
            TriangleIndices = triangles,
            ScalarValues = scalars,
            ShowEdges = false, // 大規模ではエッジ描画自体が支配的になるため既定 OFF
        };
    }

    private void RebuildMesh()
    {
        if (SizeCombo.SelectedItem is not ComboBoxItem { Tag: string tag }
            || !int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var target))
        {
            return;
        }

        StatsText.Text = $"生成中... (目標 {target:N0} 三角形)";
        Viewport.Selection.Clear();

        // メッシュ生成(CPU 側、並列)。GPU 構築は次の描画フレームで走る
        var mesh = CreateWaveMesh(target);
        _meshes.Clear();
        _meshes.Add(mesh);
        Viewport.FitToView();
    }

    // ================= 非同期構築の進捗(spec 6.24.2) =================

    private void OnBuildProgressChanged(object? sender, ViewportBuildProgressEventArgs e)
    {
        BuildProgress.Value = e.Progress;
        BuildText.Text = string.Create(
            CultureInfo.InvariantCulture, $"構築中: {e.Stage} {e.Progress:P0}");
    }

    private void OnBuildCompleted(object? sender, EventArgs e)
    {
        BuildProgress.Value = 1.0;
        var stats = Viewport.GetStatistics();
        BuildText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"構築完了: {stats.LastGeometryBuildTime.TotalMilliseconds:N0} ms(バックグラウンド構築、旧シーン表示継続)");
        UpdateStats();
    }

    // ================= 統計 / FPS =================

    private void UpdateStats()
    {
        var stats = Viewport.GetStatistics();
        if (stats.TriangleCount == 0)
        {
            return;
        }

        StatsText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"三角形 {stats.TriangleCount:N0} / 節点 {stats.VertexCount:N0} / チャンク {stats.ChunkCount} / "
            + $"エッジスキップ {stats.EdgeSkippedMeshCount} / "
            + $"構築 {stats.LastGeometryBuildTime.TotalMilliseconds:N0} ms / "
            + $"描画 {stats.LastRenderTime.TotalMilliseconds:N1} ms / "
            + $"LOD 三角形 {stats.LodTriangleCount:N0} / 描画チャンク {stats.LastDrawnChunkCount}"
            + $"{(stats.IsLodActive ? " / LOD描画中" : string.Empty)}");
    }

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        // 毎フレーム僅かに回して連続描画させ、実フレーム数から FPS を求める
        Viewport.Camera.Orbit(0.6, 0.0);

        _fpsFrameCount++;
        var elapsed = DateTime.UtcNow - _fpsWindowStart;
        if (elapsed >= TimeSpan.FromSeconds(1.0))
        {
            FpsText.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"FPS: {_fpsFrameCount / elapsed.TotalSeconds:N1}(回転アニメ中の実測)");
            _fpsWindowStart = DateTime.UtcNow;
            _fpsFrameCount = 0;
        }
    }

    private void SetRotating(bool rotate)
    {
        if (_isRotating == rotate)
        {
            return;
        }

        _isRotating = rotate;
        if (rotate)
        {
            _fpsWindowStart = DateTime.UtcNow;
            _fpsFrameCount = 0;
            CompositionTarget.Rendering += OnCompositionRendering;
        }
        else
        {
            CompositionTarget.Rendering -= OnCompositionRendering;
            FpsText.Text = "FPS: -";
        }
    }

    // ================= UI ハンドラ =================
    // 注意: XAML パース中(フィールド初期化前)にもイベントが発火しうるため null ガードが必要

    private void OnSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Viewport is not null && IsLoaded)
        {
            RebuildMesh();
        }
    }

    private void OnRotateToggleChanged(object sender, RoutedEventArgs e)
    {
        if (Viewport is not null)
        {
            SetRotating(RotateToggle.IsChecked == true);
        }
    }

    /// <summary>操作中 LOD の有効/無効(閾値の切替、変更でジオメトリ再構築が走る)。</summary>
    private void OnLodToggleChanged(object sender, RoutedEventArgs e)
    {
        if (Viewport is not null)
        {
            Viewport.InteractiveLodThreshold = LodToggle.IsChecked == true ? 5_000_000 : int.MaxValue;
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        var faces = _meshes.Count > 0 ? Viewport.Selection.GetSelectedFaces(_meshes[0]) : [];
        PickText.Text = faces.Count == 0
            ? "ピック: クリックで面を選択(大規模でもチャンク基点オフセットで三角形 ID が一致することの確認)"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"ピック: 選択面 {faces.Count} 件、三角形 ID [{string.Join(", ", faces.Take(5))}{(faces.Count > 5 ? ", ..." : string.Empty)}]");
    }

    private void OnFitClick(object sender, RoutedEventArgs e) => Viewport.FitToView();

    /// <summary>真上視点(UIA ではチャンク跨ぎピックの決定的な検証に使う)。</summary>
    private void OnTopViewClick(object sender, RoutedEventArgs e) =>
        Viewport.SetStandardView(ViewportStandardView.Top, animate: false);
}
