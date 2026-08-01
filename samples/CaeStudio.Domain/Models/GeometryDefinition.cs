namespace CaeStudio.Domain.Models;

/// <summary>形状テンプレートのパラメトリック定義(不変レコード)。</summary>
public abstract record GeometryDefinition
{
    /// <summary>板厚 t [mm](平面応力の厚み)。</summary>
    public double Thickness { get; init; } = 5.0;
}

/// <summary>
/// 円孔付き平板(原点中心、x 方向一軸引張の応力集中問題)。
/// 孔から矩形境界へ放射ブレンドしたリング構造格子でメッシュ化される。
/// </summary>
public sealed record PlateWithHoleGeometry : GeometryDefinition
{
    /// <summary>板幅 W [mm](x 方向全長)。</summary>
    public double Width { get; init; } = 240.0;

    /// <summary>板高さ H [mm](y 方向全長)。</summary>
    public double Height { get; init; } = 160.0;

    /// <summary>孔直径 d [mm]。</summary>
    public double HoleDiameter { get; init; } = 20.0;

    /// <summary>半径方向分割数(孔→外周)。</summary>
    public int RadialDivisions { get; init; } = 24;

    /// <summary>周方向分割数(4 の倍数に丸めて使用。対称軸上に節点を確保するため)。</summary>
    public int AngularDivisions { get; init; } = 96;
}

/// <summary>
/// 片持ち板(x=0 固定端、面内曲げ)。x ∈ [0, L]、y ∈ [-H/2, +H/2] の構造格子。
/// 細長比 L/H が大きいほど Euler-Bernoulli 梁理論に漸近する。
/// </summary>
public sealed record CantileverPlateGeometry : GeometryDefinition
{
    /// <summary>梁長さ L [mm]。</summary>
    public double Length { get; init; } = 200.0;

    /// <summary>梁せい H [mm](面内曲げの断面高さ)。</summary>
    public double Height { get; init; } = 20.0;

    /// <summary>x 方向分割数。</summary>
    public int DivisionsX { get; init; } = 80;

    /// <summary>y 方向分割数。</summary>
    public int DivisionsY { get; init; } = 8;
}
