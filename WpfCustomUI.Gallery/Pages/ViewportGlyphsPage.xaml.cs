using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WpfCustomUI.Controls;
using WpfCustomUI.Viewport3D;

namespace WpfCustomUI.Gallery.Pages;

/// <summary>
/// ベクトルグリフのデモ(spec 6.21.6)。内圧を受ける厚肉円筒(Lamé の厳密解)の
/// 変位ベクトル場 u(r) = Ar + B/r を矢印で表示する。全て半径方向を向くため
/// 向き・長さ・色の正しさが一目で分かる。スケール/ストライド操作、
/// 変形追従(Displacements に同配列)、断面カット併用の統合デモを含む。
/// </summary>
public partial class ViewportGlyphsPage : UserControl
{
    private const double InnerRadius = 30.0;   // a [mm]
    private const double OuterRadius = 50.0;   // b [mm]
    private const double HalfLength = 60.0;    // 円筒 z∈[-HL, +HL]
    private const double Pressure = 100.0;     // 内圧 p [MPa]
    private const double YoungsModulus = 200000.0; // E [MPa]
    private const double PoissonRatio = 0.3;   // ν

    private readonly ObservableCollection<ViewportMesh> _meshes = [];
    private readonly SectionPlane _plane = new()
    {
        // 既定の等角視点からボアが見える向き(Phase 19 と同じ流儀で法線 −Y)
        NormalX = 0.0,
        NormalY = -1.0,
        NormalZ = 0.0,
    };

    private readonly ColorScale _glyphScale;
    private double _suggestedGlyphScale = 1.0;

    public ViewportGlyphsPage()
    {
        InitializeComponent();

        // グリフ専用カラースケール(|u| [mm]、コンター用とは独立。spec 6.21.5)
        _glyphScale = new ColorScale
        {
            ColorMap = ColorMap.Jet,
            Minimum = Math.Floor(RadialDisplacement(OuterRadius) * 1000.0) / 1000.0,
            Maximum = Math.Ceiling(RadialDisplacement(InnerRadius) * 1000.0) / 1000.0,
        };

        _meshes.Add(CreateThickCylinder());

        Viewport.MeshSource = _meshes;
        Viewport.GlyphColorScale = _glyphScale;
        Viewport.GlyphStride = 4; // StrideCombo の既定値と一致
        GlyphLegend.Scale = _glyphScale;

        Loaded += (_, _) =>
        {
            // 推奨スケール(最大矢印がモデル代表寸法の 5%)を基準にスライダーで倍率調整
            _suggestedGlyphScale = Viewport.GetSuggestedGlyphScale();
            ApplyGlyphScale();
            RendererInfo.Text = Viewport.IsSoftwareRendering
                ? "レンダリング経路: WARP(ソフトウェア)+ WriteableBitmap フォールバック"
                : "レンダリング経路: ハードウェア D3D11 + D3DImage(共有サーフェス)";
        };
    }

    // ================= Lamé 解(厚肉円筒・内圧)の半径方向変位 =================

    /// <summary>u(r) = (pa²/E(b²−a²))·((1−ν)r + (1+ν)b²/r)(平面応力)。</summary>
    private static double RadialDisplacement(double r)
    {
        var a2 = InnerRadius * InnerRadius;
        var b2 = OuterRadius * OuterRadius;
        var coef = Pressure * a2 / (YoungsModulus * (b2 - a2));
        return coef * ((1.0 - PoissonRatio) * r + (1.0 + PoissonRatio) * b2 / r);
    }

    // ================= ジオメトリ生成 =================

    /// <summary>
    /// 厚肉円筒(外面+内面+両端の環状キャップ)を 1 パーツで作り、
    /// 全節点に半径方向の変位ベクトルを与える。VectorValues(グリフ)と
    /// Displacements(変形追従)に同じ配列を代入する(spec 6.21.2)。
    /// </summary>
    private static ViewportMesh CreateThickCylinder()
    {
        const int thetaDivisions = 64;
        const int axialDivisions = 24;
        const int radialDivisions = 8;

        var positions = new List<double>();
        var triangles = new List<int>();

        void AppendGrid(int uDiv, int vDiv, Func<double, double, (double X, double Y, double Z)> sample)
        {
            var baseIndex = positions.Count / 3;
            for (var i = 0; i <= uDiv; i++)
            {
                for (var j = 0; j <= vDiv; j++)
                {
                    var (x, y, z) = sample((double)i / uDiv, (double)j / vDiv);
                    positions.Add(x);
                    positions.Add(y);
                    positions.Add(z);
                }
            }

            for (var i = 0; i < uDiv; i++)
            {
                for (var j = 0; j < vDiv; j++)
                {
                    var i00 = baseIndex + i * (vDiv + 1) + j;
                    var i01 = i00 + 1;
                    var i10 = i00 + (vDiv + 1);
                    var i11 = i10 + 1;
                    triangles.AddRange([i00, i10, i11, i00, i11, i01]);
                }
            }
        }

        // 外面・内面(θ × z の格子)
        foreach (var radius in (double[])[OuterRadius, InnerRadius])
        {
            AppendGrid(thetaDivisions, axialDivisions, (u, v) =>
            {
                var theta = 2.0 * Math.PI * u;
                var z = -HalfLength + 2.0 * HalfLength * v;
                return (radius * Math.Cos(theta), radius * Math.Sin(theta), z);
            });
        }

        // 両端の環状キャップ(θ × r の格子)
        foreach (var zEnd in (double[])[-HalfLength, HalfLength])
        {
            AppendGrid(thetaDivisions, radialDivisions, (u, v) =>
            {
                var theta = 2.0 * Math.PI * u;
                var r = InnerRadius + (OuterRadius - InnerRadius) * v;
                return (r * Math.Cos(theta), r * Math.Sin(theta), zEnd);
            });
        }

        // 全節点の半径方向変位ベクトル u(r)·(cosθ, sinθ, 0)
        var nodeCount = positions.Count / 3;
        var vectors = new double[nodeCount * 3];
        for (var node = 0; node < nodeCount; node++)
        {
            var x = positions[node * 3];
            var y = positions[node * 3 + 1];
            var r = Math.Sqrt(x * x + y * y);
            var u = RadialDisplacement(r);
            vectors[node * 3] = u * x / r;
            vectors[node * 3 + 1] = u * y / r;
            vectors[node * 3 + 2] = 0.0;
        }

        return new ViewportMesh
        {
            Name = "厚肉円筒",
            Positions = [.. positions],
            TriangleIndices = [.. triangles],
            VectorValues = vectors,      // グリフ(矢印)のベクトル場
            Displacements = vectors,     // 変形追従デモ用に同じ配列(spec 6.21.2)
            ShowEdges = false,           // 矢印を主役にする(細分メッシュのエッジは煩い)
        };
    }

    // ================= UI ハンドラ =================
    // 注意: XAML パース中(フィールド初期化前)にもイベントが発火しうるため null ガードが必要

    private void ApplyGlyphScale()
    {
        var factor = ScaleSlider.Value;
        Viewport.GlyphScale = _suggestedGlyphScale * factor;
        ScaleLabel.Text = string.Create(CultureInfo.InvariantCulture, $"×{factor:0.0}");
    }

    private void OnGlyphToggleChanged(object sender, RoutedEventArgs e)
    {
        if (Viewport is not null)
        {
            Viewport.ShowGlyphs = GlyphToggle.IsChecked == true;
        }
    }

    private void OnScaleChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Viewport is not null)
        {
            ApplyGlyphScale();
        }
    }

    private void OnStrideChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Viewport is not null
            && StrideCombo.SelectedItem is ComboBoxItem { Content: string text }
            && int.TryParse(text, out var stride))
        {
            Viewport.GlyphStride = stride;
        }
    }

    private void OnClipToggleChanged(object sender, RoutedEventArgs e)
    {
        if (Viewport is not null)
        {
            Viewport.SectionPlane = ClipToggle.IsChecked == true ? _plane : null;
        }
    }

    private void OnAnimToggleChanged(object sender, RoutedEventArgs e)
    {
        if (Viewport is null)
        {
            return;
        }

        if (AnimToggle.IsChecked == true)
        {
            Viewport.DeformationScale = Viewport.GetSuggestedDeformationScale();
            Viewport.IsDeformationAnimated = true;
        }
        else
        {
            Viewport.IsDeformationAnimated = false;
            Viewport.DeformationScale = 0.0;
        }
    }

    private void OnFitClick(object sender, RoutedEventArgs e) => Viewport.FitToView();
}
