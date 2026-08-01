using System.Numerics;

namespace WpfCustomUI.Viewport3D;

/// <summary>
/// チャンク AABB のフラスタムカリング数学(spec 6.23.4)。GPU 非依存の純粋関数で単体テスト可能。
/// <para>
/// ビュー射影行列(row_major・行ベクトル規約、mul(v, M))から Gribb-Hartmann 法で
/// 6 平面を抽出し、チャンクの境界ボックスが完全に外側の平面が 1 つでもあれば描画をスキップする。
/// 平面は正規化するため、変形表示の最大変位量ぶんの余白(expand)を世界単位で加算できる。
/// </para>
/// </summary>
public static class ViewportCulling
{
    /// <summary>フラスタム平面数(left/right/bottom/top/near/far)。</summary>
    public const int PlaneCount = 6;

    /// <summary>
    /// ビュー射影行列からフラスタム 6 平面を抽出する。各平面は (a,b,c,d) で、
    /// 内側の点は a·x + b·y + c·z + d ≥ 0 を満たす。D3D のクリップ空間(z ∈ [0,1])前提。
    /// </summary>
    public static void ExtractFrustumPlanes(in Matrix4x4 viewProj, Span<Vector4> planes)
    {
        if (planes.Length < PlaneCount)
        {
            throw new ArgumentException($"平面バッファは {PlaneCount} 要素必要です。", nameof(planes));
        }

        // 行ベクトル規約(clip = v · M)では clip.x = dot(v, 列0) となるため列を使う
        var col0 = new Vector4(viewProj.M11, viewProj.M21, viewProj.M31, viewProj.M41);
        var col1 = new Vector4(viewProj.M12, viewProj.M22, viewProj.M32, viewProj.M42);
        var col2 = new Vector4(viewProj.M13, viewProj.M23, viewProj.M33, viewProj.M43);
        var col3 = new Vector4(viewProj.M14, viewProj.M24, viewProj.M34, viewProj.M44);

        planes[0] = Normalize(col3 + col0); // left   (x ≥ -w)
        planes[1] = Normalize(col3 - col0); // right  (x ≤ +w)
        planes[2] = Normalize(col3 + col1); // bottom (y ≥ -w)
        planes[3] = Normalize(col3 - col1); // top    (y ≤ +w)
        planes[4] = Normalize(col2);        // near   (z ≥ 0)
        planes[5] = Normalize(col3 - col2); // far    (z ≤ w)
    }

    /// <summary>
    /// AABB(expand ぶん全方向に拡大)がフラスタムと交差する可能性があるか。
    /// false なら確実に見えない(スキップ安全)。true は保守的(見えない場合もある)。
    /// </summary>
    public static bool IntersectsFrustum(
        ReadOnlySpan<Vector4> planes, Vector3 min, Vector3 max, float expand = 0.0f)
    {
        for (var i = 0; i < PlaneCount; i++)
        {
            if (IsOutsidePlane(planes[i], min, max, expand))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// AABB が平面の負側(外側)に完全にあるか(positive-vertex 法)。
    /// 平面法線に最も沿った角が負側なら全体が負側にある。
    /// </summary>
    public static bool IsOutsidePlane(in Vector4 plane, Vector3 min, Vector3 max, float expand = 0.0f)
    {
        var px = plane.X >= 0.0f ? max.X : min.X;
        var py = plane.Y >= 0.0f ? max.Y : min.Y;
        var pz = plane.Z >= 0.0f ? max.Z : min.Z;
        return plane.X * px + plane.Y * py + plane.Z * pz + plane.W + expand < 0.0f;
    }

    private static Vector4 Normalize(Vector4 plane)
    {
        var len = MathF.Sqrt(plane.X * plane.X + plane.Y * plane.Y + plane.Z * plane.Z);
        return len > 1e-20f ? plane / len : plane;
    }
}
