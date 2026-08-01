using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using WpfCustomUI.Controls;
using WpfCustomUI.Viewport3D;

namespace WpfCustomUI.Gallery.Pages;

/// <summary>
/// 断面カット(クリッピング平面)のデモ(spec 6.19.5)。
/// 内圧を受ける厚肉円筒(Lamé の解析解)の von Mises 応力コンターを、
/// 軸プリセット+オフセット+反転で定義した SectionPlane でカットする。
/// 切り口は開放のため、断面位置のスライスメッシュ(IsClippable=false)を
/// アプリ側で計算して重ねる参照実装を含む。
/// </summary>
public partial class ViewportSectionPage : UserControl
{
    private const double InnerRadius = 30.0;  // a [mm]
    private const double OuterRadius = 50.0;  // b [mm]
    private const double HalfLength = 60.0;   // 円筒 z∈[-HL, +HL]
    private const double Pressure = 100.0;    // 内圧 p [MPa]
    private const int ThetaDivisions = 64;
    private const int AxialDivisions = 24;
    private const int RadialDivisions = 8;

    private readonly ObservableCollection<ViewportMesh> _meshes = [];
    private readonly SectionPlane _plane = new();
    private readonly ColorScale _scale;
    private ViewportMesh? _slice;

    public ViewportSectionPage()
    {
        InitializeComponent();

        _scale = new ColorScale
        {
            ColorMap = ColorMap.Jet,
            Minimum = Math.Floor(VonMises(OuterRadius)),
            Maximum = Math.Ceiling(VonMises(InnerRadius)),
        };

        _meshes.Add(CreateThickCylinder());

        Viewport.ColorScale = _scale;
        Viewport.MeshSource = _meshes;
        Viewport.SelectionChanged += OnSelectionChanged;
        Legend.Scale = _scale;

        UpdateSection();

        Loaded += (_, _) => RendererInfo.Text = Viewport.IsSoftwareRendering
            ? "レンダリング経路: WARP(ソフトウェア)+ WriteableBitmap フォールバック"
            : "レンダリング経路: ハードウェア D3D11 + D3DImage(共有サーフェス)";
    }

    // ================= Lamé 解(厚肉円筒・内圧・閉端) =================

    /// <summary>半径 r の von Mises 応力。σr = A − B/r²、σθ = A + B/r²、σz = A(閉端)。</summary>
    private static double VonMises(double r)
    {
        var a2 = InnerRadius * InnerRadius;
        var b2 = OuterRadius * OuterRadius;
        var coefA = Pressure * a2 / (b2 - a2);
        var coefB = coefA * b2;

        var sigmaR = coefA - coefB / (r * r);
        var sigmaT = coefA + coefB / (r * r);
        var sigmaZ = coefA;

        var d1 = sigmaR - sigmaT;
        var d2 = sigmaT - sigmaZ;
        var d3 = sigmaZ - sigmaR;
        return Math.Sqrt(0.5 * (d1 * d1 + d2 * d2 + d3 * d3));
    }

    // ================= ジオメトリ生成 =================

    /// <summary>厚肉円筒(外面+内面+両端の環状キャップ)を 1 パーツで作る。</summary>
    private static ViewportMesh CreateThickCylinder()
    {
        var builder = new MeshBuilder();

        // 外面・内面(θ × z の格子)
        builder.AppendGrid(ThetaDivisions, AxialDivisions, (u, v) =>
        {
            var theta = 2.0 * Math.PI * u;
            var z = -HalfLength + 2.0 * HalfLength * v;
            return (OuterRadius * Math.Cos(theta), OuterRadius * Math.Sin(theta), z, VonMises(OuterRadius));
        });
        builder.AppendGrid(ThetaDivisions, AxialDivisions, (u, v) =>
        {
            var theta = 2.0 * Math.PI * u;
            var z = -HalfLength + 2.0 * HalfLength * v;
            return (InnerRadius * Math.Cos(theta), InnerRadius * Math.Sin(theta), z, VonMises(InnerRadius));
        });

        // 両端の環状キャップ(θ × r の格子)
        foreach (var zEnd in (double[])[-HalfLength, HalfLength])
        {
            builder.AppendGrid(ThetaDivisions, RadialDivisions, (u, v) =>
            {
                var theta = 2.0 * Math.PI * u;
                var r = InnerRadius + (OuterRadius - InnerRadius) * v;
                return (r * Math.Cos(theta), r * Math.Sin(theta), zEnd, VonMises(r));
            });
        }

        var mesh = builder.ToMesh();
        mesh.Name = "厚肉円筒";
        return mesh;
    }

    /// <summary>
    /// 断面スライス(平面と円筒の交差領域)を作る。ライブラリの切り口は開放なので、
    /// 断面コンターが欲しい場合はこのようにアプリが交差領域をメッシュ化し、
    /// IsClippable=false でカット対象から除外して重ねる(spec 6.19.2)。
    /// </summary>
    private static ViewportMesh? CreateSlice(int axis, double offset)
    {
        var builder = new MeshBuilder();

        if (axis == 2)
        {
            // 軸直交カット: 交差領域は環(アニュラス)。円筒の外なら無し
            if (Math.Abs(offset) >= HalfLength)
            {
                return null;
            }

            builder.AppendGrid(ThetaDivisions, RadialDivisions, (u, v) =>
            {
                var theta = 2.0 * Math.PI * u;
                var r = InnerRadius + (OuterRadius - InnerRadius) * v;
                return (r * Math.Cos(theta), r * Math.Sin(theta), offset, VonMises(r));
            });
        }
        else
        {
            // 軸平行カット(法線 X または Y): 交差領域は肉厚を貫く帯。
            // ボア内(|c| < a)は 2 本の帯、肉厚内(a ≤ |c| < b)は 1 本
            if (Math.Abs(offset) >= OuterRadius)
            {
                return null;
            }

            var tOuter = Math.Sqrt(OuterRadius * OuterRadius - offset * offset);
            var strips = Math.Abs(offset) < InnerRadius
                ? new (double Min, double Max)[]
                {
                    (Math.Sqrt(InnerRadius * InnerRadius - offset * offset), tOuter),
                    (-tOuter, -Math.Sqrt(InnerRadius * InnerRadius - offset * offset)),
                }
                : [(-tOuter, tOuter)];

            foreach (var (tMin, tMax) in strips)
            {
                builder.AppendGrid(12, 16, (u, v) =>
                {
                    var t = tMin + (tMax - tMin) * u;
                    var z = -HalfLength + 2.0 * HalfLength * v;
                    var scalar = VonMises(Math.Sqrt(offset * offset + t * t));
                    return axis == 0 ? (offset, t, z, scalar) : (t, offset, z, scalar);
                });
            }
        }

        var mesh = builder.ToMesh();
        mesh.Name = "断面スライス";
        mesh.IsClippable = false; // カット対象から除外(これが無いとスライス自体も消える)
        mesh.ShowEdges = false;
        return mesh;
    }

    /// <summary>複数の四角形格子パッチを 1 つの ViewportMesh に合成する小さなヘルパ。</summary>
    private sealed class MeshBuilder
    {
        private readonly List<double> _positions = [];
        private readonly List<int> _triangles = [];
        private readonly List<double> _scalars = [];

        public void AppendGrid(int uDivisions, int vDivisions, Func<double, double, (double X, double Y, double Z, double Scalar)> sample)
        {
            var baseIndex = _positions.Count / 3;

            for (var i = 0; i <= uDivisions; i++)
            {
                for (var j = 0; j <= vDivisions; j++)
                {
                    var (x, y, z, scalar) = sample((double)i / uDivisions, (double)j / vDivisions);
                    _positions.Add(x);
                    _positions.Add(y);
                    _positions.Add(z);
                    _scalars.Add(scalar);
                }
            }

            for (var i = 0; i < uDivisions; i++)
            {
                for (var j = 0; j < vDivisions; j++)
                {
                    var i00 = baseIndex + i * (vDivisions + 1) + j;
                    var i01 = i00 + 1;
                    var i10 = i00 + (vDivisions + 1);
                    var i11 = i10 + 1;
                    _triangles.AddRange([i00, i10, i11, i00, i11, i01]);
                }
            }
        }

        public ViewportMesh ToMesh() => new()
        {
            Positions = [.. _positions],
            TriangleIndices = [.. _triangles],
            ScalarValues = [.. _scalars],
        };
    }

    // ================= 断面の更新 =================

    /// <summary>UI の状態から SectionPlane とスライスメッシュを組み立て直す。</summary>
    private void UpdateSection()
    {
        var axis = AxisCombo.SelectedIndex; // 0=X, 1=Y, 2=Z
        var offset = OffsetSlider.Value;

        // 既定は法線 = −軸方向。既定の等角視点(+X+Y 象限から見る)で切り口が
        // カメラ側を向き、ボアと断面スライスが見える。反転で +軸方向に切り替わる
        var sign = FlipToggle.IsChecked == true ? 1.0 : -1.0;

        _plane.OriginX = axis == 0 ? offset : 0.0;
        _plane.OriginY = axis == 1 ? offset : 0.0;
        _plane.OriginZ = axis == 2 ? offset : 0.0;
        _plane.NormalX = axis == 0 ? sign : 0.0;
        _plane.NormalY = axis == 1 ? sign : 0.0;
        _plane.NormalZ = axis == 2 ? sign : 0.0;

        var clipEnabled = ClipToggle.IsChecked == true;
        Viewport.SectionPlane = clipEnabled ? _plane : null;

        // スライスは平面と一体なので、位置・軸が変わるたびに作り直して差し替える
        if (_slice is not null)
        {
            _meshes.Remove(_slice);
            _slice = null;
        }

        if (clipEnabled && SliceToggle.IsChecked == true)
        {
            _slice = CreateSlice(axis, offset);
            if (_slice is not null)
            {
                _meshes.Add(_slice);
            }
        }
    }

    // 注意: XAML パース中(フィールド初期化前)にもイベントが発火しうるため null ガードが必要

    private void OnSectionOptionChanged(object sender, RoutedEventArgs e)
    {
        if (_meshes.Count > 0)
        {
            UpdateSection();
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        var selection = Viewport.Selection;
        SelectionSummary.Text =
            $"Parts: {selection.PartCount}, Faces: {selection.FaceCount}, Nodes: {selection.NodeCount}";
    }

    private void OnFitClick(object sender, RoutedEventArgs e) => Viewport.FitToView();

    private void OnClearSelectionClick(object sender, RoutedEventArgs e) => Viewport.Selection.Clear();
}
