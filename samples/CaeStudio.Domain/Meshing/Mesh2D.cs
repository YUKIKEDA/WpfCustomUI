namespace CaeStudio.Domain.Meshing;

/// <summary>
/// 節点グループ: 境界条件の適用対象。表面力の積分にはポリライン(境界に沿った
/// 順序付き節点列)が必要なため、辺列かどうかを区別する。
/// </summary>
public sealed record NodeGroup
{
    /// <summary>節点インデックス列(ポリラインの場合は境界に沿った順)。</summary>
    public required int[] Nodes { get; init; }

    /// <summary>境界に沿った連続辺列かどうか(表面力を適用できるのはこの場合のみ)。</summary>
    public bool IsPolyline { get; init; }
}

/// <summary>2D 三角形メッシュ(平面応力 FEM の入力)。座標は xy 平面 [mm]。</summary>
public sealed class Mesh2D
{
    /// <summary>節点座標 [x0, y0, x1, y1, ...]。</summary>
    public required double[] Positions { get; init; }

    /// <summary>三角形の節点インデックス(反時計回り、3 個ずつ)。</summary>
    public required int[] Triangles { get; init; }

    /// <summary>名前付き節点グループ(境界条件の解決先)。</summary>
    public required IReadOnlyDictionary<string, NodeGroup> Groups { get; init; }

    /// <summary>節点数。</summary>
    public int NodeCount => Positions.Length / 2;

    /// <summary>三角形要素数。</summary>
    public int TriangleCount => Triangles.Length / 3;
}
