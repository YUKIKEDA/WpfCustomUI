using CaeStudio.App.ViewModels;
using CaeStudio.Domain.Models;
using CaeStudio.Domain.Solving;

namespace CaeStudio.Tests.App;

/// <summary>ポスト処理変換(パスプロット/FRF 合成)の検証。</summary>
public class PostProcessingTests
{
    [Fact]
    public void KirschPath_FollowsExactSolutionAlongYAxis()
    {
        var project = ProjectTemplates.CreatePlateWithHole(tension: 100.0);
        var result = StaticAnalysis.Run(project);

        var path = PostProcessing.CreateKirschPath(project, result);

        Assert.NotNull(path);
        Assert.Equal(path.FemX.Length, path.FemY.Length);
        Assert.True(path.FemX.Length >= 10);

        // r/a は孔縁(=1)から単調増加
        Assert.Equal(1.0, path.FemX[0], tolerance: 1e-6);
        for (var i = 1; i < path.FemX.Length; i++)
        {
            Assert.True(path.FemX[i] > path.FemX[i - 1]);
        }

        // 孔縁近傍で応力集中(θ=90° の Kirsch 解 σθ = 3σ∞、vM もほぼ 3σ∞)
        Assert.InRange(path.FemY[0], 240.0, 330.0);

        // 厳密解曲線の始点は解析値と一致する
        var exactAtHole = ExactSolutions.KirschVonMises(10.0, 100.0, 10.0, Math.PI / 2.0);
        Assert.Equal(exactAtHole, path.ExactY[0], tolerance: 1e-9);

        // 中間域(r/a ≈ 2)でも FEM は厳密解の ±15% 以内(有限板の境界効果を許容)
        var midIndex = Array.FindIndex(path.FemX, x => x >= 2.0);
        Assert.True(midIndex > 0);
        var exactMid = ExactSolutions.KirschVonMises(
            10.0, 100.0, path.FemX[midIndex] * 10.0, Math.PI / 2.0);
        Assert.InRange(path.FemY[midIndex], exactMid * 0.85, exactMid * 1.15);
    }

    [Fact]
    public void KirschPath_ReturnsNullForCantilever()
    {
        var project = ProjectTemplates.CreateCantileverPlate(AnalysisType.Static);
        var result = StaticAnalysis.Run(project);

        Assert.Null(PostProcessing.CreateKirschPath(project, result));
    }

    [Fact]
    public void Frf_PeaksNearNaturalFrequencies()
    {
        var project = ProjectTemplates.CreateCantileverPlate(AnalysisType.Modal) with
        {
            Solver = new SolverSettings { ModeCount = 3 },
        };
        var result = ModalAnalysis.Run(project);

        var frf = PostProcessing.CreateFrf(result, dampingRatio: 0.02);

        Assert.NotNull(frf.Frequencies);
        Assert.NotNull(frf.Magnitudes);
        Assert.NotNull(frf.Phases);
        Assert.Equal(frf.Frequencies!.Length, frf.Magnitudes!.Length);

        // 周波数レンジは全モードをカバーする
        Assert.True(frf.Frequencies[0] < result.Modes[0].FrequencyHz);
        Assert.True(frf.Frequencies[^1] > result.Modes[^1].FrequencyHz);

        // |H| の最大点は 1 次固有振動数の近傍(グリッド解像度を許容)
        var peakIndex = 0;
        for (var i = 1; i < frf.Magnitudes.Length; i++)
        {
            if (frf.Magnitudes[i] > frf.Magnitudes[peakIndex])
            {
                peakIndex = i;
            }
        }

        var f1 = result.Modes[0].FrequencyHz;
        Assert.InRange(frf.Frequencies[peakIndex], f1 * 0.95, f1 * 1.05);

        // 共振通過で位相が遅れる(ピーク前後で位相差が大きい)
        var quarter = frf.Phases!.Length / 8;
        Assert.True(Math.Abs(frf.Phases[peakIndex + quarter] - frf.Phases[Math.Max(0, peakIndex - quarter)]) > 90.0);
    }

    [Fact]
    public void ModeShapeMesh_NormalizesDisplacementToUnity()
    {
        var project = ProjectTemplates.CreateCantileverPlate(AnalysisType.Modal) with
        {
            Solver = new SolverSettings { ModeCount = 1 },
        };
        var result = ModalAnalysis.Run(project);

        var mesh = CaeStudio.App.ViewportMeshFactory.CreateModeShape(result, result.Modes[0], "モード 1");

        Assert.NotNull(mesh.Displacements);
        Assert.NotNull(mesh.ScalarValues);

        var maxDisplacement = 0.0;
        var maxScalar = 0.0;
        for (var node = 0; node < mesh.ScalarValues!.Length; node++)
        {
            var (ux, uy, uz) = (mesh.Displacements![node * 3],
                mesh.Displacements[node * 3 + 1], mesh.Displacements[node * 3 + 2]);
            maxDisplacement = Math.Max(maxDisplacement, Math.Sqrt(ux * ux + uy * uy + uz * uz));
            maxScalar = Math.Max(maxScalar, mesh.ScalarValues[node]);
            Assert.Equal(0.0, uz);
        }

        Assert.Equal(1.0, maxDisplacement, tolerance: 1e-9);
        Assert.Equal(1.0, maxScalar, tolerance: 1e-9);
    }
}
