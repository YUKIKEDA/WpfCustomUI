using CaeStudio.Domain.Meshing;
using CaeStudio.Domain.Models;
using CaeStudio.Domain.Solving;

namespace CaeStudio.Tests.Domain;

/// <summary>
/// 静解析ソルバの厳密解検証(spec 6.26.7)。
/// 円孔付き平板は Kirsch 解、片持ち板は Euler-Bernoulli 梁理論と突き合わせる。
/// </summary>
public class StaticAnalysisTests
{
    // ================= 円孔付き平板(Kirsch) =================

    /// <summary>
    /// 孔縁 θ=±90°の応力集中: 無限板の理論値は σθ = 3σ∞。
    /// 有限板(W/d=24, H/d=16)+メッシュ離散化の分を許容幅にみる。
    /// </summary>
    [Fact]
    public void PlateWithHole_StressConcentration_MatchesKirsch()
    {
        var project = ProjectTemplates.CreatePlateWithHole(tension: 100.0) with
        {
            Geometry = new PlateWithHoleGeometry
            {
                Width = 480.0, Height = 320.0, HoleDiameter = 20.0,
                RadialDivisions = 32, AngularDivisions = 128,
            },
        };

        var result = StaticAnalysis.Run(project);

        Assert.True(result.Converged, $"CG が収束しませんでした(残差 {result.FinalResidual:E2})");

        // 孔縁 θ=90°(y 軸上の最内リング節点)の von Mises を取得
        var mesh = result.Mesh;
        var holeTop = FindNode(mesh, x: 0.0, y: 10.0);
        var kirsch = ExactSolutions.KirschVonMises(10.0, 100.0, 10.0, Math.PI / 2.0);

        Assert.Equal(300.0, kirsch, tolerance: 1e-9); // 理論値の自己検算(σθ=3σ∞)
        Assert.InRange(result.NodalVonMises[holeTop], kirsch * 0.93, kirsch * 1.07);
        Assert.InRange(result.MaxVonMises, kirsch * 0.90, kirsch * 1.10);
    }

    /// <summary>孔から十分離れた点は遠方場 σxx = σ∞ に漸近する。</summary>
    [Fact]
    public void PlateWithHole_FarField_ApproachesUniaxialTension()
    {
        var project = ProjectTemplates.CreatePlateWithHole(tension: 100.0) with
        {
            Geometry = new PlateWithHoleGeometry
            {
                Width = 480.0, Height = 320.0, HoleDiameter = 20.0,
                RadialDivisions = 32, AngularDivisions = 128,
            },
        };

        var result = StaticAnalysis.Run(project);

        // x 軸上 r=10d の点(境界からも孔からも離れた位置)
        var node = FindNode(result.Mesh, x: 200.0, y: 0.0, tolerance: 15.0);
        Assert.InRange(result.NodalStress[node * 3], 95.0, 105.0);      // σxx ≈ σ∞
        Assert.InRange(Math.Abs(result.NodalStress[node * 3 + 1]), 0.0, 8.0); // σyy ≈ 0
    }

    /// <summary>メッシュ細分化で孔縁応力が Kirsch 解に単調接近する(収束性)。</summary>
    [Fact]
    public void PlateWithHole_Refinement_ConvergesTowardExact()
    {
        var errors = new List<double>();
        foreach (var (radial, angular) in ((int, int)[])[(8, 32), (16, 64), (32, 128)])
        {
            var project = ProjectTemplates.CreatePlateWithHole(tension: 100.0) with
            {
                Geometry = new PlateWithHoleGeometry
                {
                    Width = 480.0, Height = 320.0, HoleDiameter = 20.0,
                    RadialDivisions = radial, AngularDivisions = angular,
                },
            };

            var result = StaticAnalysis.Run(project);
            var holeTop = FindNode(result.Mesh, x: 0.0, y: 10.0);
            errors.Add(Math.Abs(result.NodalVonMises[holeTop] - 300.0));
        }

        Assert.True(errors[2] < errors[1] && errors[1] < errors[0],
            $"細分化で誤差が減っていません: {string.Join(", ", errors.Select(e => e.ToString("F2")))}");
    }

    // ================= 片持ち板(Euler-Bernoulli) =================

    /// <summary>先端せん断荷重の先端たわみ: δ = PL³/3EI(細長比 L/H=10、せん断変形分を許容)。</summary>
    [Fact]
    public void Cantilever_TipDeflection_MatchesBeamTheory()
    {
        const double tipShear = 10.0; // [MPa]
        var geometry = new CantileverPlateGeometry
        {
            Length = 200.0, Height = 20.0, DivisionsX = 160, DivisionsY = 16, Thickness = 5.0,
        };
        var project = ProjectTemplates.CreateCantileverPlate(AnalysisType.Static, tipShear) with
        {
            Geometry = geometry,
        };

        var result = StaticAnalysis.Run(project);
        Assert.True(result.Converged);

        // 先端辺の合力 P = 表面力 × 辺長 × 板厚
        var load = tipShear * geometry.Height * geometry.Thickness;
        var exact = ExactSolutions.CantileverTipDeflection(
            load, project.Material.YoungsModulus, geometry.Length, geometry.Height, geometry.Thickness);

        // 先端中央節点の uy
        var tipCenter = FindNode(result.Mesh, x: 200.0, y: 0.0);
        var actual = Math.Abs(result.Displacements[tipCenter * 2 + 1]);

        // CST 要素は曲げに剛(下から収束)、一方せん断変形分は梁理論よりたわむ。両者相殺で数 % 以内
        Assert.InRange(actual, exact * 0.95, exact * 1.05);
    }

    /// <summary>固定端で変位がゼロ(拘束の検証)。</summary>
    [Fact]
    public void Cantilever_FixedEdge_HasZeroDisplacement()
    {
        var project = ProjectTemplates.CreateCantileverPlate(AnalysisType.Static);
        var result = StaticAnalysis.Run(project);

        var fixedGroup = result.Mesh.Groups[ProjectTemplates.Groups.FixedEdge];
        foreach (var node in fixedGroup.Nodes)
        {
            Assert.Equal(0.0, result.Displacements[node * 2], tolerance: 1e-12);
            Assert.Equal(0.0, result.Displacements[node * 2 + 1], tolerance: 1e-12);
        }
    }

    // ================= 共通ヘルパ =================

    private static int FindNode(Mesh2D mesh, double x, double y, double tolerance = 1e-6)
    {
        var best = -1;
        var bestDistance = double.MaxValue;
        for (var node = 0; node < mesh.NodeCount; node++)
        {
            var dx = mesh.Positions[node * 2] - x;
            var dy = mesh.Positions[node * 2 + 1] - y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < bestDistance)
            {
                (best, bestDistance) = (node, distance);
            }
        }

        Assert.True(bestDistance <= Math.Max(tolerance, 1e-6),
            $"({x}, {y}) 近傍の節点が見つかりません(最近傍距離 {bestDistance:F3})");
        return best;
    }
}
