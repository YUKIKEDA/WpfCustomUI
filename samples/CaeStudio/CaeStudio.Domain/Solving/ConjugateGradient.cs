namespace CaeStudio.Domain.Solving;

/// <summary>CG の 1 反復の進捗(残差ストリームの要素)。</summary>
public sealed record CgIteration(int Iteration, double RelativeResidual);

/// <summary>CG の解と収束情報。</summary>
public sealed record CgResult(double[] Solution, int Iterations, double RelativeResidual, bool Converged);

/// <summary>
/// Jacobi 前処理付き共役勾配法。対称正定値な剛性方程式 K u = f を解く。
/// 反復ごとに <paramref name="onIteration"/> で相対残差を通知する
/// (Application 層が R3 ストリームに変換して収束モニタへ流す。Domain は R3 非依存)。
/// </summary>
public static class ConjugateGradient
{
    public static CgResult Solve(
        SparseMatrixCsr matrix,
        double[] rhs,
        double tolerance,
        int maxIterations,
        Action<CgIteration>? onIteration = null,
        CancellationToken cancellationToken = default)
    {
        var n = matrix.Size;
        if (rhs.Length != n)
        {
            throw new ArgumentException("右辺ベクトルの長さが行列サイズと一致しません。");
        }

        var x = new double[n];
        var r = (double[])rhs.Clone();     // r = b - A·0 = b
        var z = new double[n];
        var p = new double[n];
        var ap = new double[n];

        var inverseDiagonal = matrix.GetDiagonal();
        for (var i = 0; i < n; i++)
        {
            inverseDiagonal[i] = inverseDiagonal[i] > 0 ? 1.0 / inverseDiagonal[i] : 1.0;
        }

        var rhsNorm = Math.Sqrt(Dot(rhs, rhs));
        if (rhsNorm <= 0)
        {
            return new CgResult(x, 0, 0.0, Converged: true);
        }

        ApplyPreconditioner(inverseDiagonal, r, z);
        Array.Copy(z, p, n);
        var rz = Dot(r, z);

        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            matrix.Multiply(p, ap);
            var alpha = rz / Dot(p, ap);

            for (var i = 0; i < n; i++)
            {
                x[i] += alpha * p[i];
                r[i] -= alpha * ap[i];
            }

            var residual = Math.Sqrt(Dot(r, r)) / rhsNorm;
            onIteration?.Invoke(new CgIteration(iteration, residual));

            if (residual < tolerance)
            {
                return new CgResult(x, iteration, residual, Converged: true);
            }

            ApplyPreconditioner(inverseDiagonal, r, z);
            var rzNext = Dot(r, z);
            var beta = rzNext / rz;
            rz = rzNext;

            for (var i = 0; i < n; i++)
            {
                p[i] = z[i] + beta * p[i];
            }
        }

        return new CgResult(x, maxIterations, Math.Sqrt(Dot(r, r)) / rhsNorm, Converged: false);
    }

    private static void ApplyPreconditioner(double[] inverseDiagonal, double[] r, double[] z)
    {
        for (var i = 0; i < r.Length; i++)
        {
            z[i] = inverseDiagonal[i] * r[i];
        }
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
