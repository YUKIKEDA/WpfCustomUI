namespace CaeStudio.Domain.Models;

/// <summary>
/// 等方性弾性材料。単位系は mm-N-MPa-t-s
/// (ヤング率 MPa、密度 t/mm³、板厚 mm。固有振動数は Hz で得られる)。
/// </summary>
public sealed record Material
{
    /// <summary>材料名(表示用)。</summary>
    public required string Name { get; init; }

    /// <summary>ヤング率 E [MPa]。</summary>
    public required double YoungsModulus { get; init; }

    /// <summary>ポアソン比 ν [-]。</summary>
    public required double PoissonsRatio { get; init; }

    /// <summary>密度 ρ [t/mm³](鋼 = 7.85e-9)。</summary>
    public required double Density { get; init; }

    /// <summary>構造用鋼(SS400 相当)。</summary>
    public static Material Steel { get; } = new()
    {
        Name = "構造用鋼",
        YoungsModulus = 205_000.0,
        PoissonsRatio = 0.3,
        Density = 7.85e-9,
    };

    /// <summary>アルミニウム合金(A5052 相当)。</summary>
    public static Material Aluminum { get; } = new()
    {
        Name = "アルミニウム合金",
        YoungsModulus = 70_600.0,
        PoissonsRatio = 0.33,
        Density = 2.68e-9,
    };
}
