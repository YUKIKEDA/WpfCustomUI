using System.Diagnostics;
using CaeStudio.Domain.Meshing;
using CaeStudio.Domain.Models;

namespace CaeStudio.Domain.Solving;

/// <summary>線形静解析の結果。</summary>
public sealed record StaticResult
{
    /// <summary>解析に使用したメッシュ。</summary>
    public required Mesh2D Mesh { get; init; }

    /// <summary>全節点変位 [ux0, uy0, ux1, ...] [mm]。</summary>
    public required double[] Displacements { get; init; }

    /// <summary>節点平均応力 [σxx0, σyy0, τxy0, σxx1, ...] [MPa]。</summary>
    public required double[] NodalStress { get; init; }

    /// <summary>節点 von Mises 応力 [MPa]。</summary>
    public required double[] NodalVonMises { get; init; }

    /// <summary>最大 von Mises 応力 [MPa]。</summary>
    public required double MaxVonMises { get; init; }

    /// <summary>最大変位量 [mm]。</summary>
    public required double MaxDisplacement { get; init; }

    /// <summary>CG の反復回数。</summary>
    public required int Iterations { get; init; }

    /// <summary>最終相対残差。</summary>
    public required double FinalResidual { get; init; }

    /// <summary>収束したか。</summary>
    public required bool Converged { get; init; }

    /// <summary>メッシュ生成+組み立て時間。</summary>
    public required TimeSpan BuildTime { get; init; }

    /// <summary>CG 求解時間。</summary>
    public required TimeSpan SolveTime { get; init; }
}

/// <summary>線形静解析: メッシュ生成→組み立て→CG 求解→応力回復のパイプライン。</summary>
public static class StaticAnalysis
{
    /// <summary>
    /// 解析を実行する。CG の反復ごとに <paramref name="onIteration"/> が呼ばれる
    /// (ソルバスレッド上。UI への転送は呼び出し側の責務)。
    /// </summary>
    public static StaticResult Run(
        CaeProjectData project,
        Action<CgIteration>? onIteration = null,
        CancellationToken cancellationToken = default)
    {
        var buildWatch = Stopwatch.StartNew();
        var mesh = MeshGenerator.Generate(project.Geometry);
        var model = FemModel.Build(mesh, project.Material, project.Geometry.Thickness, project.BoundaryConditions);
        buildWatch.Stop();

        var solveWatch = Stopwatch.StartNew();
        var cg = ConjugateGradient.Solve(
            model.Stiffness, model.Loads,
            project.Solver.Tolerance, project.Solver.MaxIterations,
            onIteration, cancellationToken);
        solveWatch.Stop();

        var displacements = model.ExpandToFullDisplacements(cg.Solution);
        var (nodalStress, vonMises) = StressRecovery.Compute(
            mesh, project.Material, displacements);

        var maxDisplacement = 0.0;
        for (var node = 0; node < mesh.NodeCount; node++)
        {
            var (ux, uy) = (displacements[node * 2], displacements[node * 2 + 1]);
            maxDisplacement = Math.Max(maxDisplacement, Math.Sqrt(ux * ux + uy * uy));
        }

        return new StaticResult
        {
            Mesh = mesh,
            Displacements = displacements,
            NodalStress = nodalStress,
            NodalVonMises = vonMises,
            MaxVonMises = vonMises.Length > 0 ? vonMises.Max() : 0.0,
            MaxDisplacement = maxDisplacement,
            Iterations = cg.Iterations,
            FinalResidual = cg.RelativeResidual,
            Converged = cg.Converged,
            BuildTime = buildWatch.Elapsed,
            SolveTime = solveWatch.Elapsed,
        };
    }
}

/// <summary>
/// 応力回復: CST 要素の定応力 σ = D·B·ue を要素面積重みで節点平均する。
/// </summary>
public static class StressRecovery
{
    public static (double[] NodalStress, double[] VonMises) Compute(
        Mesh2D mesh, Material material, double[] displacements)
    {
        var d = FemModel.ElasticityMatrix(material);
        var positions = mesh.Positions;
        var triangles = mesh.Triangles;

        var accumulated = new double[mesh.NodeCount * 3];
        var weights = new double[mesh.NodeCount];

        Span<double> b = stackalloc double[18];
        Span<double> ue = stackalloc double[6];
        Span<double> strain = stackalloc double[3];
        Span<double> stress = stackalloc double[3];

        for (var t = 0; t < triangles.Length; t += 3)
        {
            var (n0, n1, n2) = (triangles[t], triangles[t + 1], triangles[t + 2]);
            var area = FemModel.StrainDisplacementMatrix(positions, n0, n1, n2, b);

            ue[0] = displacements[n0 * 2];
            ue[1] = displacements[n0 * 2 + 1];
            ue[2] = displacements[n1 * 2];
            ue[3] = displacements[n1 * 2 + 1];
            ue[4] = displacements[n2 * 2];
            ue[5] = displacements[n2 * 2 + 1];

            for (var i = 0; i < 3; i++)
            {
                strain[i] = 0.0;
                for (var j = 0; j < 6; j++)
                {
                    strain[i] += b[i * 6 + j] * ue[j];
                }
            }

            for (var i = 0; i < 3; i++)
            {
                stress[i] = d[i * 3] * strain[0] + d[i * 3 + 1] * strain[1] + d[i * 3 + 2] * strain[2];
            }

            foreach (var node in (ReadOnlySpan<int>)[n0, n1, n2])
            {
                accumulated[node * 3] += stress[0] * area;
                accumulated[node * 3 + 1] += stress[1] * area;
                accumulated[node * 3 + 2] += stress[2] * area;
                weights[node] += area;
            }
        }

        var nodalStress = new double[mesh.NodeCount * 3];
        var vonMises = new double[mesh.NodeCount];
        for (var node = 0; node < mesh.NodeCount; node++)
        {
            var w = weights[node];
            if (w <= 0)
            {
                continue;
            }

            var sx = accumulated[node * 3] / w;
            var sy = accumulated[node * 3 + 1] / w;
            var txy = accumulated[node * 3 + 2] / w;
            nodalStress[node * 3] = sx;
            nodalStress[node * 3 + 1] = sy;
            nodalStress[node * 3 + 2] = txy;
            vonMises[node] = Math.Sqrt(sx * sx + sy * sy - sx * sy + 3.0 * txy * txy);
        }

        return (nodalStress, vonMises);
    }
}
