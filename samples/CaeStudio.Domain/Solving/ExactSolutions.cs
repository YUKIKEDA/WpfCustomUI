namespace CaeStudio.Domain.Solving;

/// <summary>
/// 検証用の厳密解。xUnit のソルバ検証と、アプリのパスプロット重ね描きの両方で使う。
/// </summary>
public static class ExactSolutions
{
    /// <summary>
    /// Kirsch の厳密解: x 方向一軸引張 σ∞ を受ける無限板の円孔(半径 a)まわりの
    /// 極座標応力成分 (σr, σθ, τrθ)。
    /// </summary>
    public static (double SigmaR, double SigmaTheta, double TauRTheta) KirschStress(
        double holeRadius, double farStress, double r, double theta)
    {
        var a2 = holeRadius * holeRadius / (r * r);
        var a4 = a2 * a2;
        var cos2T = Math.Cos(2.0 * theta);
        var sin2T = Math.Sin(2.0 * theta);
        var s = farStress;

        var sigmaR = s / 2.0 * (1.0 - a2) + s / 2.0 * (1.0 - 4.0 * a2 + 3.0 * a4) * cos2T;
        var sigmaTheta = s / 2.0 * (1.0 + a2) - s / 2.0 * (1.0 + 3.0 * a4) * cos2T;
        var tauRTheta = -s / 2.0 * (1.0 + 2.0 * a2 - 3.0 * a4) * sin2T;
        return (sigmaR, sigmaTheta, tauRTheta);
    }

    /// <summary>Kirsch 解の平面応力 von Mises 相当応力。</summary>
    public static double KirschVonMises(double holeRadius, double farStress, double r, double theta)
    {
        var (sr, st, trt) = KirschStress(holeRadius, farStress, r, theta);
        return Math.Sqrt(sr * sr + st * st - sr * st + 3.0 * trt * trt);
    }

    /// <summary>片持ち梁の曲げ固有値 βL(cosh·cos = -1 の根、下位から)。</summary>
    public static readonly double[] CantileverBetaL =
    [
        1.8751040687119611,
        4.6940911329741746,
        7.8547574382376126,
        10.995540734875467,
        14.137168391046471,
    ];

    /// <summary>
    /// Euler-Bernoulli 片持ち梁の面内曲げ固有振動数 [Hz]。
    /// 矩形断面(厚さ t × せい h)の面内曲げ: I = t·h³/12, A = t·h。
    /// </summary>
    public static double CantileverFrequency(
        int mode, double youngsModulus, double density, double length, double height)
    {
        var betaL = CantileverBetaL[mode];
        var inertiaPerArea = height * height / 12.0; // I/A = h²/12(厚さは相殺)
        var omega = betaL * betaL / (length * length)
            * Math.Sqrt(youngsModulus * inertiaPerArea / density);
        return omega / (2.0 * Math.PI);
    }

    /// <summary>
    /// 先端集中荷重 P を受ける片持ち梁の先端たわみ δ = PL³/(3EI)。
    /// 矩形断面(厚さ t × せい h)の面内曲げ。
    /// </summary>
    public static double CantileverTipDeflection(
        double load, double youngsModulus, double length, double height, double thickness)
    {
        var inertia = thickness * height * height * height / 12.0;
        return load * length * length * length / (3.0 * youngsModulus * inertia);
    }
}
