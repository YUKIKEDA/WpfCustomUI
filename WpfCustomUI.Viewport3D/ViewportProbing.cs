using System.Numerics;

namespace WpfCustomUI.Viewport3D;

/// <summary>
/// プローブの CPU 側数学(spec 6.20.2)。GPU に依存しない純粋関数で、単体テスト可能。
/// GPU ID パスが「どの三角形か」を特定し、ここでレイ→三角形交差と重心座標補間を行って
/// ヒット 3D 点と補間スカラー値を求める。
/// </summary>
internal static class ViewportProbing
{
    /// <summary>交差判定で三角形の縁をわずかに許容する(ピクセル中心とサンプル位置のずれ対策)。</summary>
    private const float BarycentricTolerance = 0.02f;

    /// <summary>
    /// ピクセル座標からローカル座標(再センタリング後)のピックレイを作る。
    /// viewProj の逆行列で NDC の近平面(z=0)/遠平面(z=1)の 2 点を逆射影する(D3D 深度規約)。
    /// 逆行列が作れない(特異な行列)場合は null。
    /// </summary>
    public static (Vector3 Origin, Vector3 Direction)? ComputePickRay(
        Vector2 pixel, in Matrix4x4 viewProj, double pixelWidth, double pixelHeight)
    {
        if (!Matrix4x4.Invert(viewProj, out var inverse))
        {
            return null;
        }

        var ndcX = (float)(pixel.X / pixelWidth * 2.0 - 1.0);
        var ndcY = (float)(1.0 - pixel.Y / pixelHeight * 2.0); // スクリーン Y は下向き

        var near = Unproject(new Vector4(ndcX, ndcY, 0.0f, 1.0f), in inverse);
        var far = Unproject(new Vector4(ndcX, ndcY, 1.0f, 1.0f), in inverse);
        if (near is null || far is null)
        {
            return null;
        }

        var direction = far.Value - near.Value;
        if (direction.LengthSquared() < 1e-20f)
        {
            return null;
        }

        return (near.Value, Vector3.Normalize(direction));
    }

    private static Vector3? Unproject(Vector4 ndc, in Matrix4x4 inverse)
    {
        var h = Vector4.Transform(ndc, inverse);
        if (Math.Abs(h.W) < 1e-12f)
        {
            return null;
        }

        return new Vector3(h.X / h.W, h.Y / h.W, h.Z / h.W);
    }

    /// <summary>
    /// レイ→三角形交差(Möller–Trumbore、両面)。交差したら t(レイ係数)と
    /// 重心座標 (w0, w1, w2)(v0/v1/v2 の重み、合計 1)を返す。
    /// 縁は <see cref="BarycentricTolerance"/> だけ許容し、重みは [0,1] にクランプして正規化する。
    /// </summary>
    public static bool TryIntersectTriangle(
        Vector3 origin, Vector3 direction, Vector3 v0, Vector3 v1, Vector3 v2,
        out float t, out Vector3 barycentric)
    {
        t = 0.0f;
        barycentric = default;

        var edge1 = v1 - v0;
        var edge2 = v2 - v0;
        var p = Vector3.Cross(direction, edge2);
        var determinant = Vector3.Dot(edge1, p);
        if (Math.Abs(determinant) < 1e-12f)
        {
            return false; // レイと三角形が平行
        }

        var invDet = 1.0f / determinant;
        var s = origin - v0;
        var u = Vector3.Dot(s, p) * invDet;
        if (u < -BarycentricTolerance || u > 1.0f + BarycentricTolerance)
        {
            return false;
        }

        var q = Vector3.Cross(s, edge1);
        var v = Vector3.Dot(direction, q) * invDet;
        if (v < -BarycentricTolerance || u + v > 1.0f + BarycentricTolerance)
        {
            return false;
        }

        t = Vector3.Dot(edge2, q) * invDet;
        if (t < 0.0f)
        {
            return false; // 視点の後ろ
        }

        // クランプ+正規化(縁の許容分を吸収)
        u = Math.Clamp(u, 0.0f, 1.0f);
        v = Math.Clamp(v, 0.0f, 1.0f);
        var w0 = Math.Clamp(1.0f - u - v, 0.0f, 1.0f);
        var sum = w0 + u + v;
        barycentric = new Vector3(w0 / sum, u / sum, v / sum);
        return true;
    }

    /// <summary>
    /// GPU ID ピックで特定済みの三角形に対し、クリックピクセルのヒット情報を計算する。
    /// 交差は変形適用後の頂点(画面表示と同じ)で行い、ヒット点のモデル座標は
    /// 同じ重心座標で**変形前**の節点座標を補間して返す(モデル上の位置として意味を持つ)。
    /// レイが数値誤差で交差しない場合はスクリーン最近傍節点へのスナップにフォールバックする。
    /// </summary>
    public static ProbeResult? Probe(
        ViewportMesh mesh, int triangleIndex, Vector2 pixel, in Matrix4x4 viewProj,
        double pixelWidth, double pixelHeight,
        double originX, double originY, double originZ, double deformationScale)
    {
        var triangles = mesh.TriangleIndices;
        var positions = mesh.Positions;
        var baseIndex = triangleIndex * 3;
        if (baseIndex < 0 || baseIndex + 2 >= triangles.Length)
        {
            return null;
        }

        var n0 = triangles[baseIndex];
        var n1 = triangles[baseIndex + 1];
        var n2 = triangles[baseIndex + 2];
        var maxNode = Math.Max(n0, Math.Max(n1, n2));
        if (n0 < 0 || n1 < 0 || n2 < 0 || maxNode * 3 + 2 >= positions.Length)
        {
            return null;
        }

        var v0 = ViewportPicking.GetLocalPosition(positions, mesh.Displacements, deformationScale, n0, originX, originY, originZ);
        var v1 = ViewportPicking.GetLocalPosition(positions, mesh.Displacements, deformationScale, n1, originX, originY, originZ);
        var v2 = ViewportPicking.GetLocalPosition(positions, mesh.Displacements, deformationScale, n2, originX, originY, originZ);

        Vector3 barycentric;
        var ray = ComputePickRay(pixel, in viewProj, pixelWidth, pixelHeight);
        if (ray is { } r && TryIntersectTriangle(r.Origin, r.Direction, v0, v1, v2, out _, out var bary))
        {
            barycentric = bary;
        }
        else
        {
            // フォールバック: スクリーン最近傍節点にスナップ(重み 1)
            var nearest = ViewportPicking.FindNearestNodeOnTriangle(
                mesh, triangleIndex, originX, originY, originZ,
                in viewProj, pixelWidth, pixelHeight, pixel, deformationScale);
            if (nearest is not { } snap)
            {
                return null;
            }

            barycentric = new Vector3(snap == n0 ? 1.0f : 0.0f, snap == n1 ? 1.0f : 0.0f, snap == n2 ? 1.0f : 0.0f);
        }

        // 重心座標が最大の頂点 = ヒット点に最も寄与する節点(注釈アンカー)
        var node = barycentric.X >= barycentric.Y && barycentric.X >= barycentric.Z
            ? n0
            : barycentric.Y >= barycentric.Z ? n1 : n2;

        // モデル座標のヒット点(変形前の節点座標を同じ重心座標で補間)
        var x = Interpolate(positions, n0, n1, n2, 0, barycentric);
        var y = Interpolate(positions, n0, n1, n2, 1, barycentric);
        var z = Interpolate(positions, n0, n1, n2, 2, barycentric);

        double? scalar = null;
        double? nodeScalar = null;
        var scalars = mesh.ScalarValues;
        if (scalars is not null && maxNode < scalars.Length)
        {
            scalar = scalars[n0] * barycentric.X + scalars[n1] * barycentric.Y + scalars[n2] * barycentric.Z;
            nodeScalar = scalars[node];
        }

        return new ProbeResult(mesh, triangleIndex, node, x, y, z, scalar, nodeScalar);
    }

    private static double Interpolate(double[] positions, int n0, int n1, int n2, int component, Vector3 bary) =>
        positions[n0 * 3 + component] * bary.X
        + positions[n1 * 3 + component] * bary.Y
        + positions[n2 * 3 + component] * bary.Z;
}
