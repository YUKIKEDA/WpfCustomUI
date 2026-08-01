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

    /// <summary>
    /// double 座標を指定原点(通常はシーン中心)で再センタリングして float 化する。
    /// 大座標値(例: 測地系 10^6 オーダー)を GPU の float に直接渡すと
    /// カメラ操作時に頂点がジッタするため、必ずこの経路を通す(spec 6.16.3)。
    /// </summary>
    public static float[] ToLocalPositions(double[] positions, double originX, double originY, double originZ)
    {
        ArgumentNullException.ThrowIfNull(positions);
        var result = new float[positions.Length];
        for (var i = 0; i + 2 < positions.Length; i += 3)
        {
            result[i] = (float)(positions[i] - originX);
            result[i + 1] = (float)(positions[i + 1] - originY);
            result[i + 2] = (float)(positions[i + 2] - originZ);
        }

        return result;
    }

    /// <summary>
    /// 節点法線を計算する(面積重み付き平均)。
    /// 三角形の外積(=面積の 2 倍のベクトル)をそのまま累積することで、
    /// 大きい三角形ほど法線への寄与が大きくなる。
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

        for (var i = 0; i + 2 < normals.Length; i += 3)
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

        return normals;
    }

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
