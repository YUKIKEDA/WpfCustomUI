using System.Numerics;

namespace WpfCustomUI.Viewport3D;

/// <summary>
/// 断面カットの純粋関数群(spec 6.19)。GPU 非依存で単体テスト可能。
/// </summary>
public static class ViewportSection
{
    /// <summary>クリップ無効を表すシェーダ係数(符号付き距離が常に +1 → 全て表示)。</summary>
    public static readonly Vector4 DisabledClip = new(0.0f, 0.0f, 0.0f, 1.0f);

    /// <summary>
    /// 平面をシェーダ用の係数 (nx, ny, nz, d) に変換する。
    /// 再センタリング後のローカル座標 p に対し符号付き距離 = dot(p, xyz) + w となり、
    /// 正なら表示・負ならクリップ。法線は正規化され、平面の通過点はモデル座標から
    /// シーン原点(<paramref name="sceneOriginX"/> 等)を引いてローカル化する
    /// (大座標対策: 減算を double で行ってから float 化する、spec 6.16.3 と同じ流儀)。
    /// 平面が null または法線がゼロのときは null(クリップ無効)。
    /// </summary>
    public static Vector4? ComputeClipCoefficients(
        SectionPlane? plane, double sceneOriginX, double sceneOriginY, double sceneOriginZ)
    {
        if (plane is null)
        {
            return null;
        }

        var nx = plane.NormalX;
        var ny = plane.NormalY;
        var nz = plane.NormalZ;
        var length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (length < 1e-300 || !double.IsFinite(length))
        {
            return null;
        }

        nx /= length;
        ny /= length;
        nz /= length;

        // ローカル化した通過点 o_l に対し d = -dot(n, o_l)
        var ox = plane.OriginX - sceneOriginX;
        var oy = plane.OriginY - sceneOriginY;
        var oz = plane.OriginZ - sceneOriginZ;
        var d = -(nx * ox + ny * oy + nz * oz);

        return new Vector4((float)nx, (float)ny, (float)nz, (float)d);
    }

    /// <summary>
    /// ローカル座標の点がクリップ平面で切り取られる側にあるか(spec 6.20.4)。
    /// GPU の SV_ClipDistance と同じ式(dot(p, xyz) + w &lt; 0 でクリップ)なので、
    /// 注釈の自動非表示が画面のクリップ表示と一致する。
    /// <see cref="DisabledClip"/> では常に false。
    /// </summary>
    public static bool IsClipped(Vector3 localPoint, Vector4 clipCoefficients) =>
        localPoint.X * clipCoefficients.X
        + localPoint.Y * clipCoefficients.Y
        + localPoint.Z * clipCoefficients.Z
        + clipCoefficients.W < 0.0f;

    /// <summary>
    /// 平面インジケータ(半透明クワッド+輪郭線)の頂点列を作る(spec 6.19.4)。
    /// <para>
    /// 戻り値は 14 頂点 × 6 float(position + displacement ゼロ埋め、ライン系シェーダの
    /// 2 スロットレイアウトを 1 本のバッファで満たすためのインターリーブ)。
    /// 先頭 6 頂点が三角形リストのクワッド、続く 8 頂点が輪郭のラインリスト。
    /// クワッドの中心はシーン中心(ローカル原点)を平面へ射影した点で、
    /// 半サイズは <paramref name="sceneRadius"/> × 1.15。
    /// </para>
    /// </summary>
    public static float[] BuildIndicatorVertices(Vector4 clipCoefficients, float sceneRadius)
    {
        var normal = new Vector3(clipCoefficients.X, clipCoefficients.Y, clipCoefficients.Z);
        if (normal.LengthSquared() < 1e-12f)
        {
            return [];
        }

        normal = Vector3.Normalize(normal);

        // ローカル原点の平面への射影 = -d·n̂
        var center = -clipCoefficients.W * normal;
        var reference = Math.Abs(normal.Z) > 0.9f ? Vector3.UnitY : Vector3.UnitZ;
        var u = Vector3.Normalize(Vector3.Cross(reference, normal));
        var v = Vector3.Cross(normal, u);
        var half = Math.Max(sceneRadius, 1e-3f) * 1.15f;

        var p00 = center - u * half - v * half;
        var p10 = center + u * half - v * half;
        var p11 = center + u * half + v * half;
        var p01 = center - u * half + v * half;

        // クワッド(三角形 ×2)+輪郭(ラインリスト ×4)
        Vector3[] points =
        [
            p00, p10, p11, p00, p11, p01,
            p00, p10, p10, p11, p11, p01, p01, p00,
        ];

        var result = new float[points.Length * 6];
        for (var i = 0; i < points.Length; i++)
        {
            result[i * 6] = points[i].X;
            result[i * 6 + 1] = points[i].Y;
            result[i * 6 + 2] = points[i].Z;
            // 変位(TEXCOORD1)はゼロのまま
        }

        return result;
    }
}
