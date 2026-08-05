using CaeStudio.Domain.Models;
using CaeStudio.Domain.Solving;

namespace CaeStudio.Application;

/// <summary>スタディ 1 点の結果(x = 自由度数、y = 評価値)。</summary>
public sealed record StudyPoint(int Dofs, double Metric, TimeSpan Elapsed);

/// <summary>
/// メッシュ細分化スタディ: 分割数を段階的に変えて解析し、
/// 評価値(静解析 = 最大 von Mises、固有値解析 = 1 次固有振動数)の
/// メッシュ収束履歴を得る(HistoryChart の題材。spec 6.26.5)。
/// </summary>
public static class RefinementStudy
{
    /// <summary>既定の細分化倍率(現在の分割数に対する比)。</summary>
    public static readonly double[] DefaultFactors = [0.5, 0.75, 1.0, 1.5, 2.0];

    /// <summary>評価値の軸ラベル。</summary>
    public static string MetricLabel(AnalysisType type) =>
        type == AnalysisType.Modal ? "f1 [Hz]" : "Max von Mises [MPa]";

    /// <summary>
    /// スタディを実行する(同期・呼び出しスレッド上。UI からはスレッドプールで呼ぶこと)。
    /// 1 点完了するごとに <paramref name="onPoint"/> が呼ばれる。
    /// </summary>
    public static void Run(
        CaeProjectData project,
        IReadOnlyList<double> factors,
        Action<StudyPoint> onPoint,
        CancellationToken cancellationToken = default)
    {
        foreach (var factor in factors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var scaled = project with { Geometry = ScaleDivisions(project.Geometry, factor) };
            var watch = System.Diagnostics.Stopwatch.StartNew();

            if (project.AnalysisType == AnalysisType.Modal)
            {
                var solver = scaled.Solver with { ModeCount = 1 };
                var result = ModalAnalysis.Run(
                    scaled with { Solver = solver }, cancellationToken: cancellationToken);
                onPoint(new StudyPoint(
                    CountFreeDofs(scaled), result.Modes[0].FrequencyHz, watch.Elapsed));
            }
            else
            {
                var result = StaticAnalysis.Run(scaled, cancellationToken: cancellationToken);
                onPoint(new StudyPoint(
                    CountFreeDofs(scaled), result.MaxVonMises, watch.Elapsed));
            }
        }
    }

    /// <summary>分割数を倍率でスケールした形状を返す(下限は各テンプレートの最小分割)。</summary>
    public static GeometryDefinition ScaleDivisions(GeometryDefinition geometry, double factor) =>
        geometry switch
        {
            PlateWithHoleGeometry p => p with
            {
                RadialDivisions = Math.Max(4, (int)Math.Round(p.RadialDivisions * factor)),
                AngularDivisions = Math.Max(8, (int)Math.Round(p.AngularDivisions * factor)),
            },
            CantileverPlateGeometry b => b with
            {
                DivisionsX = Math.Max(2, (int)Math.Round(b.DivisionsX * factor)),
                DivisionsY = Math.Max(2, (int)Math.Round(b.DivisionsY * factor)),
            },
            _ => geometry,
        };

    private static int CountFreeDofs(CaeProjectData project)
    {
        var mesh = Domain.Meshing.MeshGenerator.Generate(project.Geometry);
        var model = Domain.Solving.FemModel.Build(
            mesh, project.Material, project.Geometry.Thickness, project.BoundaryConditions);
        return model.FreeDofCount;
    }
}
