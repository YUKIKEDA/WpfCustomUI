namespace WpfCustomUI.Viewport3D;

/// <summary>
/// メッシュ前処理の純粋関数群(spec 6.16.3 / 6.16.5)。GPU 非依存で単体テスト可能。
/// </summary>
public static class ViewportGeometry
{
    /// <summary>平坦な座標配列(x,y,z,...)の境界ボックスを求める。</summary>
    public static Bounds3D ComputeBounds(double[] positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        if (positions.Length < 3)
        {
            return Bounds3D.Empty;
        }

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

        for (var i = 0; i + 2 < positions.Length; i += 3)
        {
            var x = positions[i];
            var y = positions[i + 1];
            var z = positions[i + 2];
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z))
            {
                continue;
            }

            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            minZ = Math.Min(minZ, z);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
            maxZ = Math.Max(maxZ, z);
        }

        return minX > maxX
            ? Bounds3D.Empty
            : new Bounds3D(minX, minY, minZ, maxX, maxY, maxZ);
    }

    /// <summary>並列化の閾値(これ未満の要素数はスレッド起動コストの方が高い)。</summary>
    private const int ParallelThreshold = 65536;

    /// <summary>
    /// double 座標を指定原点(通常はシーン中心)で再センタリングして float 化する。
    /// 大座標値(例: 測地系 10^6 オーダー)を GPU の float に直接渡すと
    /// カメラ操作時に頂点がジッタするため、必ずこの経路を通す(spec 6.16.3)。
    /// 要素独立の変換のため並列化しても結果は逐次版と完全一致する(spec 6.22.2)。
    /// </summary>
    public static float[] ToLocalPositions(double[] positions, double originX, double originY, double originZ)
    {
        ArgumentNullException.ThrowIfNull(positions);
        var result = new float[positions.Length];
        var vertexCount = positions.Length / 3;

        if (vertexCount >= ParallelThreshold)
        {
            Parallel.For(0, vertexCount, v =>
            {
                var i = v * 3;
                result[i] = (float)(positions[i] - originX);
                result[i + 1] = (float)(positions[i + 1] - originY);
                result[i + 2] = (float)(positions[i + 2] - originZ);
            });
        }
        else
        {
            for (var i = 0; i + 2 < positions.Length; i += 3)
            {
                result[i] = (float)(positions[i] - originX);
                result[i + 1] = (float)(positions[i + 1] - originY);
                result[i + 2] = (float)(positions[i + 2] - originZ);
            }
        }

        return result;
    }

    /// <summary>
    /// 節点法線を計算する(面積重み付き平均)。
    /// 三角形の外積(=面積の 2 倍のベクトル)をそのまま累積することで、
    /// 大きい三角形ほど法線への寄与が大きくなる。
    /// <para>
    /// 累積は共有節点への書き込み競合を避けるため逐次のまま(順序依存の float 加算を
    /// 並列化すると結果が非決定になる)。正規化は要素独立なので並列化する(spec 6.22.2)。
    /// チャンク分割とは独立にメッシュ全体で計算するため、チャンク境界の陰影に継ぎ目が出ない。
    /// </para>
    /// </summary>
    public static float[] ComputeVertexNormals(float[] localPositions, int[] triangleIndices)
    {
        ArgumentNullException.ThrowIfNull(localPositions);
        ArgumentNullException.ThrowIfNull(triangleIndices);

        var normals = new float[localPositions.Length];
        for (var t = 0; t + 2 < triangleIndices.Length; t += 3)
        {
            var i0 = triangleIndices[t] * 3;
            var i1 = triangleIndices[t + 1] * 3;
            var i2 = triangleIndices[t + 2] * 3;

            var e1X = localPositions[i1] - localPositions[i0];
            var e1Y = localPositions[i1 + 1] - localPositions[i0 + 1];
            var e1Z = localPositions[i1 + 2] - localPositions[i0 + 2];
            var e2X = localPositions[i2] - localPositions[i0];
            var e2Y = localPositions[i2 + 1] - localPositions[i0 + 1];
            var e2Z = localPositions[i2 + 2] - localPositions[i0 + 2];

            var nx = e1Y * e2Z - e1Z * e2Y;
            var ny = e1Z * e2X - e1X * e2Z;
            var nz = e1X * e2Y - e1Y * e2X;

            normals[i0] += nx;
            normals[i0 + 1] += ny;
            normals[i0 + 2] += nz;
            normals[i1] += nx;
            normals[i1 + 1] += ny;
            normals[i1 + 2] += nz;
            normals[i2] += nx;
            normals[i2 + 1] += ny;
            normals[i2 + 2] += nz;
        }

        NormalizeAll(normals);
        return normals;
    }

    /// <summary>
    /// 節点法線を double 座標から直接計算する(spec 6.23.2 の構築中間ストリーミング化)。
    /// <see cref="ToLocalPositions"/> で作るフルサイズの float 中間配列を確保せず、
    /// 三角形毎にオンザフライで再センタリング+float 化する。float 化の式が
    /// <see cref="ToLocalPositions"/> と同一のため、結果は「float 化してから
    /// <see cref="ComputeVertexNormals"/>」と完全一致する。
    /// </summary>
    public static float[] ComputeVertexNormalsFromDouble(
        double[] positions, double originX, double originY, double originZ, int[] triangleIndices)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(triangleIndices);

        var normals = new float[positions.Length];
        for (var t = 0; t + 2 < triangleIndices.Length; t += 3)
        {
            var i0 = triangleIndices[t] * 3;
            var i1 = triangleIndices[t + 1] * 3;
            var i2 = triangleIndices[t + 2] * 3;

            var p0X = (float)(positions[i0] - originX);
            var p0Y = (float)(positions[i0 + 1] - originY);
            var p0Z = (float)(positions[i0 + 2] - originZ);

            var e1X = (float)(positions[i1] - originX) - p0X;
            var e1Y = (float)(positions[i1 + 1] - originY) - p0Y;
            var e1Z = (float)(positions[i1 + 2] - originZ) - p0Z;
            var e2X = (float)(positions[i2] - originX) - p0X;
            var e2Y = (float)(positions[i2 + 1] - originY) - p0Y;
            var e2Z = (float)(positions[i2 + 2] - originZ) - p0Z;

            var nx = e1Y * e2Z - e1Z * e2Y;
            var ny = e1Z * e2X - e1X * e2Z;
            var nz = e1X * e2Y - e1Y * e2X;

            normals[i0] += nx;
            normals[i0 + 1] += ny;
            normals[i0 + 2] += nz;
            normals[i1] += nx;
            normals[i1 + 1] += ny;
            normals[i1 + 2] += nz;
            normals[i2] += nx;
            normals[i2 + 1] += ny;
            normals[i2 + 2] += nz;
        }

        NormalizeAll(normals);
        return normals;
    }

    private static void NormalizeAll(float[] normals)
    {
        var vertexCount = normals.Length / 3;
        if (vertexCount >= ParallelThreshold)
        {
            Parallel.For(0, vertexCount, v => NormalizeAt(normals, v * 3));
        }
        else
        {
            for (var i = 0; i + 2 < normals.Length; i += 3)
            {
                NormalizeAt(normals, i);
            }
        }

        static void NormalizeAt(float[] normals, int i)
        {
            var len = MathF.Sqrt(
                normals[i] * normals[i] +
                normals[i + 1] * normals[i + 1] +
                normals[i + 2] * normals[i + 2]);
            if (len > 1e-20f)
            {
                normals[i] /= len;
                normals[i + 1] /= len;
                normals[i + 2] /= len;
            }
        }
    }

    // ================= Octahedral 法線圧縮(spec 6.23.2) =================
    // 単位法線を八面体パラメータ化して 16bit×2 の unorm に量子化する(12B→4B)。
    // 最大角度誤差は約 0.02°で、シェーディング上の視覚差はない(業界定番手法)。

    /// <summary>単位法線を octahedral 16bit×2 にエンコードする(下位 16bit=u, 上位 16bit=v)。</summary>
    public static uint EncodeOctahedralNormal(float x, float y, float z)
    {
        var l1 = MathF.Abs(x) + MathF.Abs(y) + MathF.Abs(z);
        float px, py;
        if (l1 < 1e-20f)
        {
            // ゼロ法線(未参照節点など)は +Z として扱う(視覚上は使われない)
            px = 0.0f;
            py = 0.0f;
        }
        else
        {
            px = x / l1;
            py = y / l1;
            if (z < 0.0f)
            {
                // 下半球は八面体の下面を上面へ折り返す
                var ox = px;
                px = (1.0f - MathF.Abs(py)) * SignNotZero(ox);
                py = (1.0f - MathF.Abs(ox)) * SignNotZero(py);
            }
        }

        var u = (uint)Math.Clamp((int)MathF.Round((px * 0.5f + 0.5f) * 65535.0f), 0, 65535);
        var v = (uint)Math.Clamp((int)MathF.Round((py * 0.5f + 0.5f) * 65535.0f), 0, 65535);
        return u | (v << 16);
    }

    /// <summary>octahedral 16bit×2 を単位法線へデコードする(HLSL の DecodeOctNormal と同式)。</summary>
    public static (float X, float Y, float Z) DecodeOctahedralNormal(uint packed)
    {
        var ex = (packed & 0xFFFFu) / 65535.0f * 2.0f - 1.0f;
        var ey = (packed >> 16) / 65535.0f * 2.0f - 1.0f;
        var vz = 1.0f - MathF.Abs(ex) - MathF.Abs(ey);
        var vx = ex;
        var vy = ey;
        if (vz < 0.0f)
        {
            vx = (1.0f - MathF.Abs(ey)) * SignNotZero(ex);
            vy = (1.0f - MathF.Abs(ex)) * SignNotZero(ey);
        }

        var len = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
        return len > 1e-20f ? (vx / len, vy / len, vz / len) : (0.0f, 0.0f, 1.0f);
    }

    /// <summary>float3 法線配列を octahedral uint 配列へ一括圧縮する(要素独立なので並列)。</summary>
    public static uint[] CompressNormals(float[] normals)
    {
        ArgumentNullException.ThrowIfNull(normals);
        var vertexCount = normals.Length / 3;
        var result = new uint[vertexCount];

        if (vertexCount >= ParallelThreshold)
        {
            Parallel.For(0, vertexCount, v =>
                result[v] = EncodeOctahedralNormal(normals[v * 3], normals[v * 3 + 1], normals[v * 3 + 2]));
        }
        else
        {
            for (var v = 0; v < vertexCount; v++)
            {
                result[v] = EncodeOctahedralNormal(normals[v * 3], normals[v * 3 + 1], normals[v * 3 + 2]);
            }
        }

        return result;
    }

    private static float SignNotZero(float v) => v >= 0.0f ? 1.0f : -1.0f;

    /// <summary>
    /// 三角形集合から重複のないエッジ(線分リスト)を抽出する。
    /// ワイヤフレーム重畳描画に使う。戻り値は 2 個で 1 線分のインデックス列。
    /// </summary>
    public static int[] ExtractEdges(int[] triangleIndices)
    {
        ArgumentNullException.ThrowIfNull(triangleIndices);

        var edges = new HashSet<(int A, int B)>();
        for (var t = 0; t + 2 < triangleIndices.Length; t += 3)
        {
            AddEdge(edges, triangleIndices[t], triangleIndices[t + 1]);
            AddEdge(edges, triangleIndices[t + 1], triangleIndices[t + 2]);
            AddEdge(edges, triangleIndices[t + 2], triangleIndices[t]);
        }

        var result = new int[edges.Count * 2];
        var i = 0;
        foreach (var (a, b) in edges)
        {
            result[i++] = a;
            result[i++] = b;
        }

        return result;

        static void AddEdge(HashSet<(int, int)> edges, int a, int b) =>
            edges.Add(a < b ? (a, b) : (b, a));
    }

    /// <summary>節点スカラーを float 化する(null 要素なし前提。NaN はそのまま渡し、シェーダ側で除外)。</summary>
    public static float[] ToScalarArray(double[]? scalarValues, int vertexCount)
    {
        var result = new float[vertexCount];
        if (scalarValues is null)
        {
            return result;
        }

        var count = Math.Min(scalarValues.Length, vertexCount);
        for (var i = 0; i < count; i++)
        {
            result[i] = (float)scalarValues[i];
        }

        return result;
    }
}
