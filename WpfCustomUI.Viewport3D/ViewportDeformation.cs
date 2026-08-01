namespace WpfCustomUI.Viewport3D;

/// <summary>
/// 変形表示の純粋関数群(spec 6.18)。GPU 非依存で単体テスト可能。
/// </summary>
public static class ViewportDeformation
{
    /// <summary>自動スケールの既定目標: 最大変位をモデル代表寸法の 5% に見せる。</summary>
    public const double DefaultTargetFraction = 0.05;

    /// <summary>
    /// 変位配列(3N)の最大変位ベクトル長を返す。null / 空 / 長さ不正の余り要素は無視する。
    /// </summary>
    public static double GetMaxDisplacementMagnitude(double[]? displacements)
    {
        if (displacements is null)
        {
            return 0.0;
        }

        var max = 0.0;
        for (var i = 0; i + 2 < displacements.Length; i += 3)
        {
            var x = displacements[i];
            var y = displacements[i + 1];
            var z = displacements[i + 2];
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z))
            {
                continue;
            }

            var magnitude = Math.Sqrt(x * x + y * y + z * z);
            if (magnitude > max)
            {
                max = magnitude;
            }
        }

        return max;
    }

    /// <summary>
    /// 「最大変位 × スケール ≒ モデル代表寸法 × targetFraction」となる推奨スケールを返す
    /// (Ansys の Auto Scale 相当、spec 6.18.4)。
    /// 変位ゼロ・寸法ゼロのときは 1.0(適用しても無害な恒等値)。
    /// </summary>
    public static double ComputeSuggestedScale(
        double maxDisplacement, double modelSize, double targetFraction = DefaultTargetFraction)
    {
        if (maxDisplacement <= 0.0 || modelSize <= 0.0 || targetFraction <= 0.0)
        {
            return 1.0;
        }

        return modelSize * targetFraction / maxDisplacement;
    }

    /// <summary>
    /// モード振動アニメーションの時間→スケール係数(±1 の正弦、spec 6.18.3)。
    /// t=0 で 0、t=T/4 で +1、t=3T/4 で -1。period が正でないときは 1(静止)。
    /// </summary>
    public static double GetAnimationFactor(double elapsedSeconds, double periodSeconds)
    {
        if (periodSeconds <= 0.0 || !double.IsFinite(periodSeconds))
        {
            return 1.0;
        }

        return Math.Sin(2.0 * Math.PI * elapsedSeconds / periodSeconds);
    }

    /// <summary>
    /// 変位配列を GPU 用 float 配列(3×vertexCount、ゼロ初期化)へ変換する。
    /// null または長さが 3×節点数に満たない部分はゼロのまま(=変位なし)。
    /// </summary>
    public static float[] ToDisplacementArray(double[]? displacements, int vertexCount)
    {
        var result = new float[vertexCount * 3];
        if (displacements is null)
        {
            return result;
        }

        var count = Math.Min(displacements.Length, result.Length);
        for (var i = 0; i < count; i++)
        {
            result[i] = (float)displacements[i];
        }

        return result;
    }
}
