using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfCustomUI.Controls;
using WpfCustomUI.Viewport3D;

namespace WpfCustomUI.Gallery.Pages;

public partial class Viewport3DPage : UserControl
{
    private const double PlateHalfWidth = 60.0;   // [mm]
    private const double PlateHalfHeight = 40.0;  // [mm]
    private const double HoleRadius = 10.0;       // [mm]
    private const double NominalStress = 100.0;   // [MPa]

    private readonly ObservableCollection<ViewportMesh> _meshes = [];
    private readonly ColorScale _scale;
    private readonly ViewportMesh _boss;

    public Viewport3DPage()
    {
        InitializeComponent();

        // 孔縁の応力集中係数は 3(Kirsch)。値域は 0〜3S に設定
        _scale = new ColorScale
        {
            ColorMap = ColorMap.Jet,
            Minimum = 0.0,
            Maximum = 3.0 * NominalStress,
        };

        _meshes.Add(CreatePlateWithHole());
        _boss = CreateBoss();
        _meshes.Add(_boss);

        Viewport.ColorScale = _scale;
        Viewport.MeshSource = _meshes;
        Viewport.SelectionChanged += OnSelectionChanged;
        Viewport.HoverChanged += OnHoverChanged;
        Legend.Scale = _scale;

        Loaded += (_, _) => RendererInfo.Text = Viewport.IsSoftwareRendering
            ? "レンダリング経路: WARP(ソフトウェア)+ WriteableBitmap フォールバック"
            : "レンダリング経路: ハードウェア D3D11 + D3DImage(共有サーフェス)";
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        var selection = Viewport.Selection;
        SelectionSummary.Text =
            $"Parts: {selection.PartCount}, Faces: {selection.FaceCount}, Nodes: {selection.NodeCount}";
    }

    /// <summary>ホバープリハイライトの対象表示(Phase 24。UIA での検証にも使う)。</summary>
    private void OnHoverChanged(object? sender, EventArgs e)
    {
        HoverText.Text = Viewport.HoverInfo switch
        {
            null => "Hover: -",
            { IsPart: true } h => $"Hover: Part {h.Mesh.Name}",
            { NodeIndex: >= 0 } h => $"Hover: Node {h.NodeIndex}",
            { } h => $"Hover: Face {h.TriangleIndex}",
        };
    }

    /// <summary>
    /// 円孔付き平板(構造格子)。孔から矩形境界へ放射状にブレンドした
    /// リング状の四角形格子を三角形分割し、Kirsch の厳密解で von Mises 応力を与える。
    /// </summary>
    private static ViewportMesh CreatePlateWithHole()
    {
        const int radialDivisions = 24;      // 孔→外周
        const int angularDivisions = 96;     // 周方向(閉じる)

        var vertexCount = (radialDivisions + 1) * angularDivisions;
        var positions = new double[vertexCount * 3];
        var scalars = new double[vertexCount];

        for (var a = 0; a < angularDivisions; a++)
        {
            var theta = 2.0 * Math.PI * a / angularDivisions;
            var (cos, sin) = (Math.Cos(theta), Math.Sin(theta));

            // 方向 (cosθ, sinθ) を矩形境界まで伸ばした距離
            var kx = Math.Abs(cos) < 1e-12 ? double.MaxValue : PlateHalfWidth / Math.Abs(cos);
            var ky = Math.Abs(sin) < 1e-12 ? double.MaxValue : PlateHalfHeight / Math.Abs(sin);
            var boundaryRadius = Math.Min(kx, ky);

            for (var r = 0; r <= radialDivisions; r++)
            {
                // 孔付近の応力勾配が急なため、二乗分布で孔側を細かく刻む
                var t = (double)r / radialDivisions;
                var radius = HoleRadius + (boundaryRadius - HoleRadius) * t * t;

                var index = (a * (radialDivisions + 1) + r) * 3;
                positions[index] = radius * cos;
                positions[index + 1] = radius * sin;
                positions[index + 2] = 0.0;

                scalars[a * (radialDivisions + 1) + r] = KirschVonMises(radius, theta);
            }
        }

        var triangles = new List<int>(radialDivisions * angularDivisions * 6);
        for (var a = 0; a < angularDivisions; a++)
        {
            var nextA = (a + 1) % angularDivisions; // 周方向に閉じる
            for (var r = 0; r < radialDivisions; r++)
            {
                var i00 = a * (radialDivisions + 1) + r;
                var i01 = a * (radialDivisions + 1) + r + 1;
                var i10 = nextA * (radialDivisions + 1) + r;
                var i11 = nextA * (radialDivisions + 1) + r + 1;

                triangles.Add(i00);
                triangles.Add(i10);
                triangles.Add(i11);
                triangles.Add(i00);
                triangles.Add(i11);
                triangles.Add(i01);
            }
        }

        return new ViewportMesh
        {
            Name = "円孔付き平板",
            Positions = positions,
            TriangleIndices = [.. triangles],
            ScalarValues = scalars,
        };
    }

    /// <summary>
    /// Kirsch の厳密解(x 方向一軸引張・無限板の円孔まわり)による平面応力 von Mises。
    /// </summary>
    private static double KirschVonMises(double r, double theta)
    {
        var s = NominalStress;
        var a2 = HoleRadius * HoleRadius / (r * r);
        var a4 = a2 * a2;
        var cos2T = Math.Cos(2.0 * theta);
        var sin2T = Math.Sin(2.0 * theta);

        var sigmaR = s / 2.0 * (1.0 - a2) + s / 2.0 * (1.0 - 4.0 * a2 + 3.0 * a4) * cos2T;
        var sigmaT = s / 2.0 * (1.0 + a2) - s / 2.0 * (1.0 + 3.0 * a4) * cos2T;
        var tauRT = -s / 2.0 * (1.0 + 2.0 * a2 - 3.0 * a4) * sin2T;

        return Math.Sqrt(sigmaR * sigmaR + sigmaT * sigmaT - sigmaR * sigmaT + 3.0 * tauRT * tauRT);
    }

    /// <summary>孔に通した円筒ボス(スカラーなしの単色パーツ。マルチパーツ表示のデモ)。</summary>
    private static ViewportMesh CreateBoss()
    {
        const int segments = 48;
        const double radius = HoleRadius * 0.72;
        const double halfLength = 18.0;

        // 側面 + 上下の円盤(中心点扇形)
        var vertexCount = segments * 2 + 2;
        var positions = new double[vertexCount * 3];
        for (var i = 0; i < segments; i++)
        {
            var theta = 2.0 * Math.PI * i / segments;
            var (x, y) = (radius * Math.Cos(theta), radius * Math.Sin(theta));
            positions[i * 3] = x;
            positions[i * 3 + 1] = y;
            positions[i * 3 + 2] = halfLength;
            positions[(segments + i) * 3] = x;
            positions[(segments + i) * 3 + 1] = y;
            positions[(segments + i) * 3 + 2] = -halfLength;
        }

        var topCenter = segments * 2;
        var bottomCenter = segments * 2 + 1;
        positions[topCenter * 3 + 2] = halfLength;
        positions[bottomCenter * 3 + 2] = -halfLength;

        var triangles = new List<int>(segments * 12);
        for (var i = 0; i < segments; i++)
        {
            var next = (i + 1) % segments;

            // 側面
            triangles.Add(i);
            triangles.Add(segments + i);
            triangles.Add(segments + next);
            triangles.Add(i);
            triangles.Add(segments + next);
            triangles.Add(next);

            // 上面 / 下面
            triangles.Add(topCenter);
            triangles.Add(i);
            triangles.Add(next);
            triangles.Add(bottomCenter);
            triangles.Add(segments + next);
            triangles.Add(segments + i);
        }

        return new ViewportMesh
        {
            Name = "円筒ボス",
            Positions = positions,
            TriangleIndices = [.. triangles],
            Color = Color.FromRgb(0x8A, 0x9B, 0xB0),
            ShowEdges = false,
        };
    }

    // 注意: IsChecked="True" 指定のトグルは XAML パース中(フィールド初期化前)にも
    // Checked を発火させるため、各ハンドラは null ガードが必要

    private void OnOrthoChanged(object sender, RoutedEventArgs e)
    {
        if (Viewport is not null)
        {
            Viewport.Projection = OrthoToggle.IsChecked == true
                ? ViewportProjection.Orthographic
                : ViewportProjection.Perspective;
        }
    }

    private void OnDiscreteChanged(object sender, RoutedEventArgs e)
    {
        if (_scale is not null)
        {
            _scale.LevelCount = DiscreteToggle.IsChecked == true ? 10 : null;
        }
    }

    private void OnBossChanged(object sender, RoutedEventArgs e)
    {
        if (_boss is not null)
        {
            _boss.IsVisible = BossToggle.IsChecked == true;
        }
    }

    private void OnFitClick(object sender, RoutedEventArgs e) => Viewport.FitToView();

    private void OnPickModeChanged(object sender, RoutedEventArgs e)
    {
        if (Viewport is null)
        {
            return;
        }

        Viewport.PickMode = true switch
        {
            _ when PickPartRadio.IsChecked == true => ViewportPickMode.Part,
            _ when PickFaceRadio.IsChecked == true => ViewportPickMode.Face,
            _ when PickNodeRadio.IsChecked == true => ViewportPickMode.Node,
            _ => ViewportPickMode.None,
        };
    }

    private void OnClearSelectionClick(object sender, RoutedEventArgs e) => Viewport.Selection.Clear();

    private void OnHoverToggleChanged(object sender, RoutedEventArgs e)
    {
        if (Viewport is not null)
        {
            Viewport.IsHoverHighlightEnabled = HoverToggle.IsChecked == true;
        }
    }

    private void OnThroughToggleChanged(object sender, RoutedEventArgs e)
    {
        if (Viewport is not null)
        {
            Viewport.RubberBandSelectionMode = ThroughToggle.IsChecked == true
                ? ViewportRubberBandSelectionMode.Through
                : ViewportRubberBandSelectionMode.VisibleOnly;
        }
    }

    private void OnStandardViewClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag }
            && Enum.TryParse<ViewportStandardView>(tag, out var view))
        {
            Viewport.SetStandardView(view);
        }
    }
}
