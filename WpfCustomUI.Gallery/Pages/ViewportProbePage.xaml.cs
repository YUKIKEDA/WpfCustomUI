using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using WpfCustomUI.Controls;
using WpfCustomUI.Viewport3D;

namespace WpfCustomUI.Gallery.Pages;

/// <summary>
/// プローブ+注釈のデモ(spec 6.20.5)。Kirsch の円孔付き平板(一軸引張の厳密解)を
/// von Mises コンターで表示し、孔縁のプローブで応力集中係数 3 を「発見」する。
/// ProbeLabelFormatter による単位付き書式と、注釈一覧の削除/全削除の参照実装を含む。
/// </summary>
public partial class ViewportProbePage : UserControl
{
    private const double PlateHalfWidth = 60.0;   // [mm]
    private const double PlateHalfHeight = 40.0;  // [mm]
    private const double HoleRadius = 10.0;       // [mm]
    private const double NominalStress = 100.0;   // [MPa]

    private readonly ObservableCollection<ViewportMesh> _meshes = [];
    private readonly ColorScale _scale;

    public ViewportProbePage()
    {
        InitializeComponent();

        _scale = new ColorScale
        {
            ColorMap = ColorMap.Jet,
            Minimum = 0.0,
            Maximum = 3.0 * NominalStress, // 孔縁の応力集中係数 3(Kirsch)
        };

        _meshes.Add(CreateKirschPlate());

        Viewport.ColorScale = _scale;
        Viewport.MeshSource = _meshes;
        Legend.Scale = _scale;

        // 単位付きラベル書式の参照実装(物理量の意味づけ=アプリ責務)。
        // UIA テストが値をパースするためカルチャ非依存にする
        Viewport.ProbeLabelFormatter = r =>
            FormattableString.Invariant($"σ_vM = {r.ScalarValue:0.0} MPa (N{r.NodeIndex})");

        AnnotationList.ItemsSource = Viewport.Annotations;
        Viewport.Annotations.CollectionChanged += (_, _) =>
            AnnotationSummary.Text = $"注釈: {Viewport.Annotations.Count} 件";

        Loaded += (_, _) =>
        {
            // 平板が正対する上面視点で開始(プローブしやすい)
            Viewport.SetStandardView(ViewportStandardView.Top, animate: false);
            RendererInfo.Text = Viewport.IsSoftwareRendering
                ? "レンダリング経路: WARP(ソフトウェア)+ WriteableBitmap フォールバック"
                : "レンダリング経路: ハードウェア D3D11 + D3DImage(共有サーフェス)";
        };
    }

    /// <summary>
    /// 円孔付き平板(構造格子)。孔から矩形境界へ放射状にブレンドしたリング格子を三角形分割し、
    /// Kirsch の厳密解で von Mises 応力を、面外たわみ形状を模した Z 変位(追従デモ用)を与える。
    /// </summary>
    private static ViewportMesh CreateKirschPlate()
    {
        const int radialDivisions = 24;   // 孔→外周
        const int angularDivisions = 96;  // 周方向(閉じる)

        var vertexCount = (radialDivisions + 1) * angularDivisions;
        var positions = new double[vertexCount * 3];
        var scalars = new double[vertexCount];
        var displacements = new double[vertexCount * 3];

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
                var (x, y) = (radius * cos, radius * sin);

                var node = a * (radialDivisions + 1) + r;
                positions[node * 3] = x;
                positions[node * 3 + 1] = y;
                positions[node * 3 + 2] = 0.0;

                scalars[node] = KirschVonMises(radius, theta);

                // 面外たわみ形状を模した Z 変位(注釈の変形追従デモ用、物理解ではない)
                displacements[node * 3 + 2] =
                    Math.Cos(0.5 * Math.PI * x / PlateHalfWidth)
                    * Math.Cos(0.5 * Math.PI * y / PlateHalfHeight);
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
            Name = "Kirsch 円孔板",
            Positions = positions,
            TriangleIndices = [.. triangles],
            ScalarValues = scalars,
            Displacements = displacements,
        };
    }

    /// <summary>Kirsch の厳密解(x 方向一軸引張・無限板の円孔まわり)による平面応力 von Mises。</summary>
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

    // 注意: IsChecked="True" 指定のトグルは XAML パース中(フィールド初期化前)にも
    // Checked を発火させるため、各ハンドラは null ガードが必要

    private void OnProbeToggleChanged(object sender, RoutedEventArgs e)
    {
        if (Viewport is not null)
        {
            Viewport.PickMode = ProbeToggle.IsChecked == true
                ? ViewportPickMode.Probe
                : ViewportPickMode.None;
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
            Viewport.DeformationScale = 0.0; // プローブ精度のため非変形へ戻す
        }
    }

    private void OnTopViewClick(object sender, RoutedEventArgs e) =>
        Viewport.SetStandardView(ViewportStandardView.Top);

    private void OnFitClick(object sender, RoutedEventArgs e) => Viewport.FitToView();

    private void OnRemoveAnnotationClick(object sender, RoutedEventArgs e)
    {
        if (AnnotationList.SelectedItem is ViewportAnnotation annotation)
        {
            Viewport.Annotations.Remove(annotation);
        }
    }

    private void OnClearAnnotationsClick(object sender, RoutedEventArgs e) =>
        Viewport.Annotations.Clear();
}
