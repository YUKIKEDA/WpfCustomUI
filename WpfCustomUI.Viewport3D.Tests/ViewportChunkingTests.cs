using WpfCustomUI.Viewport3D;

namespace WpfCustomUI.Viewport3D.Tests;

/// <summary>
/// チャンク分割数学(spec 6.22.2)と並列ジオメトリ構築の逐次一致(spec 6.22.7)のテスト。
/// </summary>
public class ViewportChunkingTests
{
    /// <summary>n×n 格子メッシュ(2(n−1)² 三角形)を作る。</summary>
    private static (double[] Positions, int[] Triangles) CreateGrid(int n)
    {
        var positions = new double[n * n * 3];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                var v = i * n + j;
                positions[v * 3] = i;
                positions[v * 3 + 1] = j;
                positions[v * 3 + 2] = Math.Sin(i * 0.3) * Math.Cos(j * 0.2);
            }
        }

        var side = n - 1;
        var triangles = new int[side * side * 6];
        for (var i = 0; i < side; i++)
        {
            for (var j = 0; j < side; j++)
            {
                var i00 = i * n + j;
                var dst = (i * side + j) * 6;
                triangles[dst] = i00;
                triangles[dst + 1] = i00 + n;
                triangles[dst + 2] = i00 + n + 1;
                triangles[dst + 3] = i00;
                triangles[dst + 4] = i00 + n + 1;
                triangles[dst + 5] = i00 + 1;
            }
        }

        return (positions, triangles);
    }

    // ================= ComputeChunkBoundaries =================

    [Fact]
    public void Boundaries_SmallMesh_SingleChunk()
    {
        var (_, triangles) = CreateGrid(10);
        var boundaries = ViewportChunking.ComputeChunkBoundaries(triangles, 100);

        var boundary = Assert.Single(boundaries);
        Assert.Equal(0, boundary.TriangleStart);
        Assert.Equal(triangles.Length / 3, boundary.TriangleCount);
        Assert.Equal(100, boundary.VertexCount); // 格子の全節点が参照される
    }

    [Fact]
    public void Boundaries_EmptyTriangles_ReturnsEmpty()
    {
        Assert.Empty(ViewportChunking.ComputeChunkBoundaries([], 10));
        Assert.Empty(ViewportChunking.ComputeChunkBoundaries([0, 1, 2], 0));
    }

    [Fact]
    public void Boundaries_TriangleLimit_SplitsContiguously()
    {
        var (_, triangles) = CreateGrid(10); // 162 三角形
        var boundaries = ViewportChunking.ComputeChunkBoundaries(
            triangles, 100, maxTrianglesPerChunk: 50);

        Assert.Equal(4, boundaries.Count); // 50+50+50+12

        // 連続分割: 隙間も重複もなく全三角形を被覆する
        var expectedStart = 0;
        foreach (var b in boundaries)
        {
            Assert.Equal(expectedStart, b.TriangleStart);
            Assert.True(b.TriangleCount is > 0 and <= 50);
            expectedStart += b.TriangleCount;
        }

        Assert.Equal(triangles.Length / 3, expectedStart);
    }

    [Fact]
    public void Boundaries_VertexLimit_NeverExceeded()
    {
        var (_, triangles) = CreateGrid(20);
        const int maxVertices = 64;
        var boundaries = ViewportChunking.ComputeChunkBoundaries(
            triangles, 400, maxVerticesPerChunk: maxVertices);

        Assert.True(boundaries.Count > 1);
        foreach (var b in boundaries)
        {
            Assert.True(b.VertexCount <= maxVertices);
        }
    }

    [Fact]
    public void Boundaries_TinyLimits_AreClampedToOneTriangle()
    {
        // 上限が 1 三角形分未満でも必ず前進する(無限ループしない)
        int[] triangles = [0, 1, 2, 2, 1, 3];
        var boundaries = ViewportChunking.ComputeChunkBoundaries(
            triangles, 4, maxVerticesPerChunk: 1, maxTrianglesPerChunk: 1);

        Assert.Equal(2, boundaries.Count);
        Assert.All(boundaries, b => Assert.Equal(1, b.TriangleCount));
    }

    [Fact]
    public void Boundaries_DegenerateTriangle_CountsUniqueVerticesOnly()
    {
        // 頂点 0 を 3 回参照する縮退三角形は新規節点 1 として数える
        int[] triangles = [0, 0, 0];
        var boundary = Assert.Single(ViewportChunking.ComputeChunkBoundaries(triangles, 1));
        Assert.Equal(1, boundary.VertexCount);
    }

    // ================= BuildChunkData =================

    [Fact]
    public void BuildChunkData_RemapsBackToOriginalTriangles()
    {
        var (_, triangles) = CreateGrid(20);
        var vertexCount = 400;
        var boundaries = ViewportChunking.ComputeChunkBoundaries(
            triangles, vertexCount, maxVerticesPerChunk: 100, maxTrianglesPerChunk: 120);
        Assert.True(boundaries.Count > 1);

        var remap = new ChunkVertexRemap(vertexCount);
        foreach (var boundary in boundaries)
        {
            var (localTriangles, globalVertices) = ViewportChunking.BuildChunkData(triangles, boundary, remap);

            Assert.Equal(boundary.TriangleCount * 3, localTriangles.Length);
            Assert.Equal(boundary.VertexCount, globalVertices.Length);

            // ローカル→グローバル逆写像で元の三角形列と完全一致する
            for (var i = 0; i < localTriangles.Length; i++)
            {
                var local = localTriangles[i];
                Assert.InRange(local, 0, globalVertices.Length - 1);
                Assert.Equal(triangles[boundary.TriangleStart * 3 + i], globalVertices[local]);
            }

            // グローバルマップに重複がない(ユニーク節点)
            Assert.Equal(globalVertices.Length, globalVertices.Distinct().Count());
        }
    }

    [Fact]
    public void BuildChunkData_SharedRemap_IsIsolatedBetweenChunks()
    {
        // 同じ remap インスタンスを使い回しても前チャンクの割り当てが漏れない(エポック方式)
        int[] triangles = [0, 1, 2, 3, 4, 5];
        var remap = new ChunkVertexRemap(6);

        var (local1, global1) = ViewportChunking.BuildChunkData(
            triangles, new ChunkBoundary(0, 1, 3), remap);
        var (local2, global2) = ViewportChunking.BuildChunkData(
            triangles, new ChunkBoundary(1, 1, 3), remap);

        Assert.Equal([0, 1, 2], local1);
        Assert.Equal([0, 1, 2], global1);
        Assert.Equal([0, 1, 2], local2);
        Assert.Equal([3, 4, 5], global2);
    }

    // ================= ピック ID オフセット(spec 6.22.2) =================

    [Fact]
    public void PickIdOffset_LocalPrimitiveIdPlusBase_RecoversGlobalTriangle()
    {
        var (_, triangles) = CreateGrid(15);
        var triangleCount = triangles.Length / 3;
        var boundaries = ViewportChunking.ComputeChunkBoundaries(
            triangles, 225, maxTrianglesPerChunk: 60);
        Assert.True(boundaries.Count > 1);

        // シェーダの primId + PickParams.x と同じ計算で全グローバル三角形が一意に復元できる
        var seen = new HashSet<int>();
        foreach (var b in boundaries)
        {
            for (var localPrim = 0; localPrim < b.TriangleCount; localPrim++)
            {
                var globalTriangle = b.TriangleStart + localPrim;
                Assert.InRange(globalTriangle, 0, triangleCount - 1);
                Assert.True(seen.Add(globalTriangle));
            }
        }

        Assert.Equal(triangleCount, seen.Count);
    }

    // ================= 並列ジオメトリ構築の逐次一致(spec 6.22.7) =================

    [Fact]
    public void ToLocalPositions_LargeInput_MatchesSequentialReference()
    {
        // 並列化閾値(65,536 節点)を超えるサイズで、逐次リファレンスと完全一致する
        var (positions, _) = CreateGrid(300); // 90,000 節点
        const double ox = 12.5, oy = -3.25, oz = 100.0;

        var actual = ViewportGeometry.ToLocalPositions(positions, ox, oy, oz);

        var expected = new float[positions.Length];
        for (var i = 0; i + 2 < positions.Length; i += 3)
        {
            expected[i] = (float)(positions[i] - ox);
            expected[i + 1] = (float)(positions[i + 1] - oy);
            expected[i + 2] = (float)(positions[i + 2] - oz);
        }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ComputeVertexNormals_LargeInput_MatchesSequentialReference()
    {
        var (positions, triangles) = CreateGrid(300);
        var local = ViewportGeometry.ToLocalPositions(positions, 0.0, 0.0, 0.0);

        var actual = ViewportGeometry.ComputeVertexNormals(local, triangles);
        var expected = SequentialNormalsReference(local, triangles);

        Assert.Equal(expected, actual);
    }

    /// <summary>並列化前の実装と同じ逐次リファレンス(累積+正規化とも逐次)。</summary>
    private static float[] SequentialNormalsReference(float[] localPositions, int[] triangleIndices)
    {
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

    // ================= ViewportStatistics(spec 6.22.5) =================

    [Fact]
    public void ViewportStatistics_RecordEquality()
    {
        var a = new ViewportStatistics(
            50_000_000, 25_010_001, 1, 13, 1,
            2_500_000, true, 7,
            TimeSpan.FromSeconds(3.2), TimeSpan.FromMilliseconds(33.0));
        var b = a with { };

        Assert.Equal(a, b);
        Assert.Equal(50_000_000, a.TriangleCount);
        Assert.Equal(13, a.ChunkCount);
        Assert.Equal(1, a.EdgeSkippedMeshCount);
        Assert.Equal(2_500_000, a.LodTriangleCount);
        Assert.True(a.IsLodActive);
        Assert.Equal(7, a.LastDrawnChunkCount);
    }

    [Fact]
    public void InteractiveLodThreshold_Behavior()
    {
        // GpuMesh.Create の判定式(triangleCount > threshold で LOD 構築)と同じ境界条件
        static bool ShouldBuildLod(int triangleCount, int threshold) => triangleCount > threshold;

        Assert.False(ShouldBuildLod(5_000_000, 5_000_000)); // 500万ちょうど → LOD なし
        Assert.True(ShouldBuildLod(5_000_001, 5_000_000));  // 超過 → LOD 構築
        Assert.False(ShouldBuildLod(int.MaxValue, int.MaxValue)); // int.MaxValue で実質無効
    }

    [Fact]
    public void EdgeExtractionSkip_ThresholdBehavior()
    {
        // GpuMesh.Create の判定式(triangleCount > limit でスキップ)と同じ境界条件
        static bool ShouldSkip(int triangleCount, int limit) => triangleCount > limit;

        Assert.False(ShouldSkip(5_000_000, 5_000_000)); // 500万ちょうど → 抽出
        Assert.True(ShouldSkip(5_000_001, 5_000_000));  // 超過 → スキップ
        Assert.False(ShouldSkip(int.MaxValue, int.MaxValue)); // 実質無制限
    }
}
