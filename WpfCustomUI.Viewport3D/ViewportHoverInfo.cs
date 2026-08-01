namespace WpfCustomUI.Viewport3D;

/// <summary>
/// ホバープリハイライトの現在対象(spec 6.24.3)。<see cref="WcuViewport.HoverInfo"/> から取得できる。
/// 粒度は <see cref="WcuViewport.PickMode"/> に追従する:
/// Part はパーツ全体(<see cref="TriangleIndex"/>/<see cref="NodeIndex"/> は -1)、
/// Face は三角形、Node / Probe は最近傍節点。
/// </summary>
/// <param name="Mesh">ホバー中のメッシュ。</param>
/// <param name="TriangleIndex">ホバー中の三角形インデックス(対象外は -1)。</param>
/// <param name="NodeIndex">ホバー中の節点インデックス(対象外は -1)。</param>
/// <param name="IsPart">パーツ全体のホバーか。</param>
public sealed record ViewportHoverInfo(ViewportMesh Mesh, int TriangleIndex, int NodeIndex, bool IsPart);
