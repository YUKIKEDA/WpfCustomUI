using System.Diagnostics;
using CaeStudio.Domain.Meshing;
using CaeStudio.Domain.Models;

namespace CaeStudio.Domain.Solving;

/// <summary>固有モード 1 つ分の結果。</summary>
public sealed record ModalMode
{
    /// <summary>モード番号(1 始まり)。</summary>
    public required int Index { get; init; }

    /// <summary>固有振動数 [Hz]。</summary>
    public required double FrequencyHz { get; init; }

    /// <summary>
    /// モード形状(全節点変位 [ux0, uy0, ...]、質量正規化 φᵀMφ = 1)。拘束 DOF は 0。
    /// モード質量 1 なので FRF のモード重ね合わせにそのまま使える。
    /// </summary>
    public required double[] Shape { get; init; }

    /// <summary>質量正規化形状の最大変位量(表示スケーリング用: Shape / MaxAmplitude で最大 1)。</summary>
    public required double MaxAmplitude { get; init; }
}

/// <summary>固有値解析の結果。</summary>
public sealed record ModalResult
{
    public required Mesh2D Mesh { get; init; }

    /// <summary>下位モード(振動数昇順)。</summary>
    public required IReadOnlyList<ModalMode> Modes { get; init; }

    /// <summary>内部 CG の総反復回数。</summary>
    public required int TotalCgIterations { get; init; }

    public required TimeSpan BuildTime { get; init; }

    public required TimeSpan SolveTime { get; init; }
}

/// <summary>
/// 固有値解析: 集中質量行列+逆反復法(CG 再利用+Gram-Schmidt 直交化)で
/// 一般化固有値問題 K φ = λ M φ の下位モードを求める(spec 6.26.4)。
/// </summary>
public static class ModalAnalysis
{
    /// <summary>
    /// 解析を実行する。内部 CG の残差は <paramref name="onIteration"/> に
    /// 通し番号で通知される(収束モニタのライブ表示用)。
    /// </summary>
    public static ModalResult Run(
        CaeProjectData project,
        Action<CgIteration>? onIteration = null,
        CancellationToken cancellationToken = default)
    {
        var buildWatch = Stopwatch.StartNew();
        var mesh = MeshGenerator.Generate(project.Geometry);
        var model = FemModel.Build(mesh, project.Material, project.Geometry.Thickness, project.BoundaryConditions);
        buildWatch.Stop();

        var solveWatch = Stopwatch.StartNew();
        var n = model.FreeDofCount;
        var mass = model.LumpedMass;
        var modeCount = Math.Clamp(project.Solver.ModeCount, 1, Math.Min(10, n));

        var modes = new List<ModalMode>(modeCount);
        var found = new List<double[]>(modeCount); // M-正規化済み固有ベクトル
        var totalCgIterations = 0;

        // 逆反復の内部 CG は最終判定より緩くてよい(外側で Rayleigh 商が収束するため)
        var innerTolerance = Math.Max(project.Solver.Tolerance, 1e-10);

        var x = new double[n];
        var kx = new double[n];
        var mx = new double[n];

        for (var mode = 0; mode < modeCount; mode++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 決定的な初期ベクトル(モードごとに位相をずらした擬似ランダム)
            var random = new Random(12345 + mode * 7919);
            for (var i = 0; i < n; i++)
            {
                x[i] = random.NextDouble() - 0.5;
            }

            OrthogonalizeAgainstFound(x, mass, found);
            NormalizeM(x, mass);

            var lambda = 0.0;
            for (var iteration = 0; iteration < 200; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // y = K⁻¹ (M x) を CG で解く(前回の反復解 x を初期推定に使う代わりに
                // 右辺のスケールが揃っているため零初期でも数百反復で収束する)
                for (var i = 0; i < n; i++)
                {
                    mx[i] = mass[i] * x[i];
                }

                var cg = ConjugateGradient.Solve(
                    model.Stiffness, mx, innerTolerance, project.Solver.MaxIterations,
                    it => onIteration?.Invoke(it with { Iteration = totalCgIterations + it.Iteration }),
                    cancellationToken);
                totalCgIterations += cg.Iterations;

                Array.Copy(cg.Solution, x, n);
                OrthogonalizeAgainstFound(x, mass, found);
                NormalizeM(x, mass);

                // Rayleigh 商 λ = xᵀKx / xᵀMx(x は M-正規化済みなので分母 1)
                model.Stiffness.Multiply(x, kx);
                var newLambda = Dot(x, kx);

                var converged = lambda > 0 && Math.Abs(newLambda - lambda) <= 1e-10 * newLambda;
                lambda = newLambda;
                if (converged)
                {
                    break;
                }
            }

            found.Add((double[])x.Clone());

            var frequency = Math.Sqrt(Math.Max(lambda, 0.0)) / (2.0 * Math.PI);
            var shape = model.ExpandToFullDisplacements(x);
            modes.Add(new ModalMode
            {
                Index = mode + 1,
                FrequencyHz = frequency,
                Shape = shape,
                MaxAmplitude = MaxNodalAmplitude(shape),
            });
        }

        solveWatch.Stop();

        return new ModalResult
        {
            Mesh = mesh,
            Modes = [.. modes.OrderBy(m => m.FrequencyHz).Select((m, i) => m with { Index = i + 1 })],
            TotalCgIterations = totalCgIterations,
            BuildTime = buildWatch.Elapsed,
            SolveTime = solveWatch.Elapsed,
        };
    }

    /// <summary>既出モードとの M-直交化(修正 Gram-Schmidt を 2 回通して丸め誤差を抑える)。</summary>
    private static void OrthogonalizeAgainstFound(double[] x, double[] mass, List<double[]> found)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var phi in found)
            {
                var projection = 0.0;
                for (var i = 0; i < x.Length; i++)
                {
                    projection += phi[i] * mass[i] * x[i];
                }

                for (var i = 0; i < x.Length; i++)
                {
                    x[i] -= projection * phi[i];
                }
            }
        }
    }

    /// <summary>xᵀMx = 1 に正規化する。</summary>
    private static void NormalizeM(double[] x, double[] mass)
    {
        var norm = 0.0;
        for (var i = 0; i < x.Length; i++)
        {
            norm += x[i] * mass[i] * x[i];
        }

        norm = Math.Sqrt(norm);
        if (norm <= 0)
        {
            throw new InvalidOperationException("逆反復の反復ベクトルが退化しました。");
        }

        for (var i = 0; i < x.Length; i++)
        {
            x[i] /= norm;
        }
    }

    /// <summary>最大節点変位量。</summary>
    private static double MaxNodalAmplitude(double[] fullDisplacements)
    {
        var max = 0.0;
        for (var node = 0; node < fullDisplacements.Length / 2; node++)
        {
            var (ux, uy) = (fullDisplacements[node * 2], fullDisplacements[node * 2 + 1]);
            max = Math.Max(max, Math.Sqrt(ux * ux + uy * uy));
        }

        return max;
    }

    private static double Dot(double[] a, double[] b)
    {
        var sum = 0.0;
        for (var i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }
}
