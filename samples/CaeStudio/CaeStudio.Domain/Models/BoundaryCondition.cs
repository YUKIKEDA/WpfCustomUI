namespace CaeStudio.Domain.Models;

/// <summary>節点グループに与える拘束の種類。</summary>
public enum ConstraintKind
{
    /// <summary>拘束なし(荷重のみ)。</summary>
    None,

    /// <summary>完全固定(ux = uy = 0)。</summary>
    Fixed,

    /// <summary>x 方向拘束(ux = 0。y=0 対称面などに使用)。</summary>
    PinX,

    /// <summary>y 方向拘束(uy = 0。x=0 対称面などに使用)。</summary>
    PinY,
}

/// <summary>
/// 境界条件: メッシュの節点グループ(テンプレートが幾何条件で定義し再メッシュに追従)に
/// 拘束または表面力を与える。表面力はグループがポリライン(境界辺列)のときのみ有効。
/// </summary>
public sealed record BoundaryCondition
{
    /// <summary>対象の節点グループ名(<see cref="Meshing.Mesh2D.Groups"/> のキー)。</summary>
    public required string GroupName { get; init; }

    /// <summary>表示名(UI 用)。</summary>
    public required string DisplayName { get; init; }

    /// <summary>拘束の種類。</summary>
    public ConstraintKind Constraint { get; init; } = ConstraintKind.None;

    /// <summary>表面力の x 成分 [MPa](板厚と辺長で積分して節点力になる)。</summary>
    public double TractionX { get; init; }

    /// <summary>表面力の y 成分 [MPa]。</summary>
    public double TractionY { get; init; }

    /// <summary>UI で荷重値を編集可能にするか(テンプレートの荷重辺のみ true)。</summary>
    public bool IsLoadEditable { get; init; }
}
