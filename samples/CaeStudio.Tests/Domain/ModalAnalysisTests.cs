using CaeStudio.Domain.Models;
using CaeStudio.Domain.Solving;

namespace CaeStudio.Tests.Domain;

/// <summary>
/// 固有値解析(逆反復法)の厳密解検証(spec 6.26.7)。
/// 片持ち板の面内曲げモードを Euler-Bernoulli 梁理論と突き合わせる。
/// 細長比 L/H = 20 とし、せん断変形・回転慣性の影響(高次ほど大)を許容幅にみる。
/// </summary>
public class ModalAnalysisTests
{
    private static CaeProjectData SlenderCantilever(int modeCount) =>
        ProjectTemplates.CreateCantileverPlate(AnalysisType.Modal) with
        {
            Geometry = new CantileverPlateGeometry
            {
                Length = 200.0, Height = 10.0, DivisionsX = 160, DivisionsY = 8, Thickness = 5.0,
            },
            Solver = new SolverSettings { ModeCount = modeCount },
        };

    [Fact]
    public void Cantilever_BendingFrequencies_MatchEulerBernoulli()
    {
        var project = SlenderCantilever(modeCount: 3);
        var result = ModalAnalysis.Run(project);

        Assert.Equal(3, result.Modes.Count);

        var material = project.Material;
        double Exact(int mode) => ExactSolutions.CantileverFrequency(
            mode, material.YoungsModulus, material.Density, 200.0, 10.0);

        // CST は曲げに硬く FEM がやや高め(静解析の たわみ -4.6% ⇔ 振動数 +2.3% と整合)。
        // せん断変形(振動数を下げる、高次ほど大)との合算を許容幅にみる。
        // 1 次: 約 206 Hz
        Assert.InRange(result.Modes[0].FrequencyHz, Exact(0) * 0.97, Exact(0) * 1.05);
        // 2 次: 約 1293 Hz
        Assert.InRange(result.Modes[1].FrequencyHz, Exact(1) * 0.94, Exact(1) * 1.06);
        // 3 次: 約 3620 Hz
        Assert.InRange(result.Modes[2].FrequencyHz, Exact(2) * 0.90, Exact(2) * 1.08);
    }

    [Fact]
    public void Cantilever_Frequencies_AreStrictlyIncreasing()
    {
        var result = ModalAnalysis.Run(SlenderCantilever(modeCount: 4));

        for (var i = 1; i < result.Modes.Count; i++)
        {
            Assert.True(result.Modes[i].FrequencyHz > result.Modes[i - 1].FrequencyHz * 1.01,
                $"モード {i} と {i + 1} の振動数が分離していません");
        }
    }

    [Fact]
    public void Cantilever_ModeShapes_HaveConsistentAmplitudeAndFixedRoot()
    {
        var project = SlenderCantilever(modeCount: 2);
        var result = ModalAnalysis.Run(project);

        foreach (var mode in result.Modes)
        {
            // MaxAmplitude は形状の実最大変位量と一致する(表示は Shape / MaxAmplitude)
            var max = 0.0;
            for (var node = 0; node < mode.Shape.Length / 2; node++)
            {
                var (ux, uy) = (mode.Shape[node * 2], mode.Shape[node * 2 + 1]);
                max = Math.Max(max, Math.Sqrt(ux * ux + uy * uy));
            }

            Assert.True(max > 0);
            Assert.Equal(max, mode.MaxAmplitude, tolerance: max * 1e-12);

            // 固定端はゼロ
            var fixedGroup = result.Mesh.Groups[ProjectTemplates.Groups.FixedEdge];
            foreach (var node in fixedGroup.Nodes)
            {
                Assert.Equal(0.0, mode.Shape[node * 2], tolerance: 1e-12);
                Assert.Equal(0.0, mode.Shape[node * 2 + 1], tolerance: 1e-12);
            }
        }
    }

    [Fact]
    public async Task AnalysisRunner_ModalProject_PublishesModalResult()
    {
        using var runner = new CaeStudio.Application.AnalysisRunner();
        var project = ProjectTemplates.CreateCantileverPlate(AnalysisType.Modal) with
        {
            Geometry = new CantileverPlateGeometry { DivisionsX = 40, DivisionsY = 4 },
            Solver = new SolverSettings { ModeCount = 2 },
        };

        await runner.RunAsync(project);

        Assert.Equal(CaeStudio.Application.AnalysisState.Completed, runner.State.CurrentValue);
        Assert.Null(runner.StaticResult.CurrentValue);
        Assert.NotNull(runner.ModalResult.CurrentValue);
        Assert.Equal(2, runner.ModalResult.CurrentValue!.Modes.Count);
    }
}
