using System.Numerics;

namespace WpfCustomUI.Viewport3D;

/// <summary>
/// 貫通矩形選択の CPU 側純粋関数(spec 6.24.4)。GPU 非依存で単体テスト可能。
/// <para>
/// 可視のみの矩形選択(GPU ID 領域読み出し)と違い、隠面も含めて
/// 「スクリーン射影が矩形内に入るか」で判定する。判定仕様:
/// 節点=射影点が矩形内 / 面=3 頂点全てが矩形内(厳密内包) /
/// パーツ=構成三角形が 1 つでも面判定を満たせば選択。
/// 変形表示中は変位適用後の座標(GPU 頂点シェーダと同式)で判定し、
/// 断面クリップは節点=クリップ点を除外、面=3 頂点全てクリップの三角形を除外
/// (部分クリップの三角形は画面に残っているため選択対象のまま)。
/// </para>
/// </summary>
internal static class ViewportRectSelect
{
    /// <summary>
    /// 節点が矩形内か。射影不能(カメラ背後)と断面クリップされた節点は false。
    /// </summary>
    public static bool IsNodeInRect(
        double[] positions, double[]? displacements, double deformationScale, int node,
        double originX, double originY, double originZ,
        in Matrix4x4 viewProj, double pixelWidth, double pixelHeight,
        Vector2 rectMin, Vector2 rectMax, Vector4 clipPlane)
    {
        if (node < 0 || node * 3 + 2 >= positions.Length)
        {
            return false;
        }

        var local = ViewportPicking.GetLocalPosition(
            positions, displacements, deformationScale, node, originX, originY, originZ);
        if (ViewportSection.IsClipped(local, clipPlane))
        {
            return false;
        }

        return ViewportPicking.ProjectToPixel(local, in viewProj, pixelWidth, pixelHeight) is { } pixel
            && pixel.X >= rectMin.X && pixel.X <= rectMax.X
            && pixel.Y >= rectMin.Y && pixel.Y <= rectMax.Y;
    }

    /// <summary>
    /// 三角形が矩形内か(3 頂点全てが矩形内、厳密内包)。
    /// 3 頂点全てが断面クリップされた三角形は除外する(部分クリップは対象のまま)。
    /// </summary>
    public static bool IsTriangleInRect(
        double[] positions, int[] triangleIndices, double[]? displacements, double deformationScale,
        int triangleIndex,
        double originX, double originY, double originZ,
        in Matrix4x4 viewProj, double pixelWidth, double pixelHeight,
        Vector2 rectMin, Vector2 rectMax, Vector4 clipPlane)
    {
        var baseIndex = triangleIndex * 3;
        if (baseIndex < 0 || baseIndex + 2 >= triangleIndices.Length)
        {
            return false;
        }

        var clippedCorners = 0;
        for (var corner = 0; corner < 3; corner++)
        {
            var node = triangleIndices[baseIndex + corner];
            if (node < 0 || node * 3 + 2 >= positions.Length)
            {
                return false;
            }

            var local = ViewportPicking.GetLocalPosition(
                positions, displacements, deformationScale, node, originX, originY, originZ);
            if (ViewportSection.IsClipped(local, clipPlane))
            {
                clippedCorners++;
            }

            if (ViewportPicking.ProjectToPixel(local, in viewProj, pixelWidth, pixelHeight) is not { } pixel
                || pixel.X < rectMin.X || pixel.X > rectMax.X
                || pixel.Y < rectMin.Y || pixel.Y > rectMax.Y)
            {
                return false;
            }
        }

        return clippedCorners < 3;
    }

    /// <summary>
    /// チャンク AABB の粗篩: スクリーン射影の境界ボックスが矩形と重なる可能性があるか。
    /// カメラ背後にかかるコーナーがある場合は保守的に true(細かい判定は頂点側で行う)。
    /// <paramref name="expand"/> は変形余白(最大変位 × |スケール|)。
    /// </summary>
    public static bool ScreenBoundsMayOverlapRect(
        Vector3 boundsMin, Vector3 boundsMax, float expand,
        in Matrix4x4 viewProj, double pixelWidth, double pixelHeight,
        Vector2 rectMin, Vector2 rectMax)
    {
        var min = boundsMin - new Vector3(expand);
        var max = boundsMax + new Vector3(expand);

        var screenMin = new Vector2(float.PositiveInfinity);
        var screenMax = new Vector2(float.NegativeInfinity);
        for (var i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? min.X : max.X,
                (i & 2) == 0 ? min.Y : max.Y,
                (i & 4) == 0 ? min.Z : max.Z);
            if (ViewportPicking.ProjectToPixel(corner, in viewProj, pixelWidth, pixelHeight) is not { } pixel)
            {
                return true; // カメラ背後を跨ぐ AABB は射影境界が定義できないため保守的に通す
            }

            screenMin = Vector2.Min(screenMin, pixel);
            screenMax = Vector2.Max(screenMax, pixel);
        }

        return screenMin.X <= rectMax.X && screenMax.X >= rectMin.X
            && screenMin.Y <= rectMax.Y && screenMax.Y >= rectMin.Y;
    }

    /// <summary>
    /// 指定範囲のグローバル三角形を判定して結果に集める(チャンク 1 個分の逐次処理。
    /// 並列化は呼び出し側がチャンク単位で行う)。
    /// </summary>
    public static void CollectTrianglesInRect(
        double[] positions, int[] triangleIndices, double[]? displacements, double deformationScale,
        double originX, double originY, double originZ,
        in Matrix4x4 viewProj, double pixelWidth, double pixelHeight,
        Vector2 rectMin, Vector2 rectMax, Vector4 clipPlane,
        int triangleStart, int triangleCount, ICollection<int> results)
    {
        for (var t = triangleStart; t < triangleStart + triangleCount; t++)
        {
            if (IsTriangleInRect(
                positions, triangleIndices, displacements, deformationScale, t,
                originX, originY, originZ, in viewProj, pixelWidth, pixelHeight,
                rectMin, rectMax, clipPlane))
            {
                results.Add(t);
            }
        }
    }
}
