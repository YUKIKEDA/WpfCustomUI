using System.Numerics;

namespace WpfCustomUI.Viewport3D;

/// <summary>
/// ピッキングの CPU 側数学(spec 6.17.2)。GPU に依存しない純粋関数で、単体テスト可能。
/// GPU ID パスが「どの三角形か」を特定し、節点ピックはその 3 頂点を
/// スクリーンに射影してカーソル最近傍を選ぶ(頂点用の別 ID パスが不要)。
/// </summary>
internal static class ViewportPicking
{
    /// <summary>
    /// ローカル座標(再センタリング後)の点をビューポートのピクセル座標へ射影する。
    /// カメラ背後(w ≤ 0)の点は null。
    /// </summary>
    public static Vector2? ProjectToPixel(
        Vector3 localPosition, in Matrix4x4 viewProj, double pixelWidth, double pixelHeight)
    {
        var clip = Vector4.Transform(new Vector4(localPosition, 1.0f), viewProj);
        if (clip.W <= 1e-9f)
        {
            return null;
        }

        var ndcX = clip.X / clip.W;
        var ndcY = clip.Y / clip.W;
        return new Vector2(
            (float)((ndcX + 1.0) * 0.5 * pixelWidth),
            (float)((1.0 - ndcY) * 0.5 * pixelHeight)); // スクリーン Y は下向き
    }

    /// <summary>
    /// ピックされた三角形の 3 頂点のうち、カーソルにスクリーン距離が最も近い節点インデックスを返す。
    /// </summary>
    /// <param name="mesh">対象メッシュ(節点座標は world=モデル座標)。</param>
    /// <param name="triangleIndex">三角形インデックス(TriangleIndices / 3 単位)。</param>
    /// <param name="origin">シーン再センタリングの原点。</param>
    public static int? FindNearestNodeOnTriangle(
        ViewportMesh mesh, int triangleIndex,
        double originX, double originY, double originZ,
        in Matrix4x4 viewProj, double pixelWidth, double pixelHeight,
        Vector2 cursorPixel, double deformationScale = 0.0)
    {
        var triangles = mesh.TriangleIndices;
        var positions = mesh.Positions;
        var baseIndex = triangleIndex * 3;
        if (baseIndex < 0 || baseIndex + 2 >= triangles.Length)
        {
            return null;
        }

        int? best = null;
        var bestDistance = double.MaxValue;
        for (var corner = 0; corner < 3; corner++)
        {
            var node = triangles[baseIndex + corner];
            if (node < 0 || node * 3 + 2 >= positions.Length)
            {
                continue;
            }

            var local = GetLocalPosition(
                positions, mesh.Displacements, deformationScale, node, originX, originY, originZ);
            var pixel = ProjectToPixel(local, in viewProj, pixelWidth, pixelHeight);
            if (pixel is null)
            {
                continue;
            }

            var distance = (pixel.Value - cursorPixel).LengthSquared();
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = node;
            }
        }

        return best;
    }

    /// <summary>
    /// 矩形選択の節点列挙: ヒットした三角形群のユニーク頂点のうち、
    /// スクリーン射影が矩形内に入る節点を返す(可視三角形経由なので隠面の節点は含まれない)。
    /// </summary>
    public static HashSet<int> FindNodesInRectangle(
        ViewportMesh mesh, IReadOnlyCollection<int> hitTriangles,
        double originX, double originY, double originZ,
        in Matrix4x4 viewProj, double pixelWidth, double pixelHeight,
        Vector2 rectMin, Vector2 rectMax, double deformationScale = 0.0)
    {
        var result = new HashSet<int>();
        var triangles = mesh.TriangleIndices;
        var positions = mesh.Positions;

        var candidates = new HashSet<int>();
        foreach (var tri in hitTriangles)
        {
            var baseIndex = tri * 3;
            if (baseIndex < 0 || baseIndex + 2 >= triangles.Length)
            {
                continue;
            }

            candidates.Add(triangles[baseIndex]);
            candidates.Add(triangles[baseIndex + 1]);
            candidates.Add(triangles[baseIndex + 2]);
        }

        foreach (var node in candidates)
        {
            if (node < 0 || node * 3 + 2 >= positions.Length)
            {
                continue;
            }

            var local = GetLocalPosition(
                positions, mesh.Displacements, deformationScale, node, originX, originY, originZ);
            var pixel = ProjectToPixel(local, in viewProj, pixelWidth, pixelHeight);
            if (pixel is null)
            {
                continue;
            }

            if (pixel.Value.X >= rectMin.X && pixel.Value.X <= rectMax.X
                && pixel.Value.Y >= rectMin.Y && pixel.Value.Y <= rectMax.Y)
            {
                result.Add(node);
            }
        }

        return result;
    }

    /// <summary>
    /// 再センタリング+変形適用後のローカル座標。GPU 頂点シェーダ(pos + disp × scale)と
    /// 同じ式にすることで、変形表示中の節点ピックが画面表示と一致する(spec 6.18.2)。
    /// </summary>
    private static Vector3 GetLocalPosition(
        double[] positions, double[]? displacements, double deformationScale,
        int node, double originX, double originY, double originZ)
    {
        double dx = 0.0, dy = 0.0, dz = 0.0;
        if (deformationScale != 0.0 && displacements is not null && node * 3 + 2 < displacements.Length)
        {
            dx = displacements[node * 3] * deformationScale;
            dy = displacements[node * 3 + 1] * deformationScale;
            dz = displacements[node * 3 + 2] * deformationScale;
        }

        return new(
            (float)(positions[node * 3] - originX + dx),
            (float)(positions[node * 3 + 1] - originY + dy),
            (float)(positions[node * 3 + 2] - originZ + dz));
    }
}
