using WpfCustomUI.Viewport3D;

namespace WpfCustomUI.Viewport3D.Tests;

public class ViewportGeometryTests
{
    [Fact]
    public void ComputeBounds_ReturnsMinMax()
    {
        double[] positions = [1.0, 2.0, 3.0, -1.0, 5.0, 0.0, 4.0, -2.0, 1.0];
        var bounds = ViewportGeometry.ComputeBounds(positions);

        Assert.Equal(-1.0, bounds.MinX);
        Assert.Equal(-2.0, bounds.MinY);
        Assert.Equal(0.0, bounds.MinZ);
        Assert.Equal(4.0, bounds.MaxX);
        Assert.Equal(5.0, bounds.MaxY);
        Assert.Equal(3.0, bounds.MaxZ);
    }

    [Fact]
    public void ComputeBounds_SkipsNaN()
    {
        double[] positions = [1.0, 1.0, 1.0, double.NaN, 100.0, 100.0, 2.0, 2.0, 2.0];
        var bounds = ViewportGeometry.ComputeBounds(positions);

        Assert.Equal(1.0, bounds.MinX);
        Assert.Equal(2.0, bounds.MaxX);
        Assert.Equal(2.0, bounds.MaxY);
    }

    [Fact]
    public void ComputeBounds_Empty_ReturnsEmpty()
    {
        Assert.True(ViewportGeometry.ComputeBounds([]).IsEmpty);
    }

    [Fact]
    public void ToLocalPositions_SubtractsOrigin()
    {
        // 測地系スケールの大座標でも、再センタリング後は float で精度が保てる
        double[] positions = [1_000_000.5, 2_000_000.25, 3_000_000.125];
        var local = ViewportGeometry.ToLocalPositions(positions, 1_000_000.0, 2_000_000.0, 3_000_000.0);

        Assert.Equal(0.5f, local[0]);
        Assert.Equal(0.25f, local[1]);
        Assert.Equal(0.125f, local[2]);
    }

    [Fact]
    public void ComputeVertexNormals_FlatTriangle_ReturnsPlaneNormal()
    {
        // XY 平面上の反時計回り三角形 → 法線は +Z
        float[] positions = [0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f];
        int[] triangles = [0, 1, 2];

        var normals = ViewportGeometry.ComputeVertexNormals(positions, triangles);

        for (var v = 0; v < 3; v++)
        {
            Assert.Equal(0.0f, normals[v * 3], 1e-6f);
            Assert.Equal(0.0f, normals[v * 3 + 1], 1e-6f);
            Assert.Equal(1.0f, normals[v * 3 + 2], 1e-6f);
        }
    }

    [Fact]
    public void ComputeVertexNormals_SharedVertex_IsAreaWeightedAverage()
    {
        // 大きな XY 三角形(+Z)と小さな XZ 三角形(-Y)が頂点 0 を共有する。
        // 面積重み付きなので共有頂点の法線は +Z 側に大きく傾く。
        float[] positions =
        [
            0f, 0f, 0f,   // 0: 共有
            10f, 0f, 0f,  // 1
            0f, 10f, 0f,  // 2
            1f, 0f, 0f,   // 3
            0f, 0f, 1f,   // 4
        ];
        int[] triangles = [0, 1, 2, 0, 3, 4];

        var normals = ViewportGeometry.ComputeVertexNormals(positions, triangles);

        Assert.True(normals[2] > 0.9f, "共有頂点の法線は大きい三角形の +Z が支配的");
        Assert.True(normals[1] < 0.0f, "-Y 成分も残る");

        var length = MathF.Sqrt(
            normals[0] * normals[0] + normals[1] * normals[1] + normals[2] * normals[2]);
        Assert.Equal(1.0f, length, 1e-5f);
    }

    [Fact]
    public void ExtractEdges_SharedEdgeIsNotDuplicated()
    {
        // 2 三角形が 1 エッジを共有 → ユニークエッジは 5 本
        int[] triangles = [0, 1, 2, 1, 3, 2];
        var edges = ViewportGeometry.ExtractEdges(triangles);

        Assert.Equal(5 * 2, edges.Length);

        var set = new HashSet<(int, int)>();
        for (var i = 0; i < edges.Length; i += 2)
        {
            var a = Math.Min(edges[i], edges[i + 1]);
            var b = Math.Max(edges[i], edges[i + 1]);
            Assert.True(set.Add((a, b)), "エッジが重複しない");
        }

        Assert.Contains((1, 2), set); // 共有エッジも 1 本だけ含まれる
    }

    [Fact]
    public void ToScalarArray_Null_ReturnsZeros()
    {
        var scalars = ViewportGeometry.ToScalarArray(null, 3);
        Assert.Equal([0f, 0f, 0f], scalars);
    }

    [Fact]
    public void ToScalarArray_ConvertsAndTruncatesToVertexCount()
    {
        var scalars = ViewportGeometry.ToScalarArray([1.5, 2.5, 3.5, 9.9], 3);
        Assert.Equal([1.5f, 2.5f, 3.5f], scalars);
    }
}

public class Bounds3DTests
{
    [Fact]
    public void Radius_IsHalfDiagonal()
    {
        var bounds = new Bounds3D(0.0, 0.0, 0.0, 2.0, 2.0, 1.0);
        Assert.Equal(Math.Sqrt(4.0 + 4.0 + 1.0) / 2.0, bounds.Radius, 1e-12);
    }

    [Fact]
    public void Union_CombinesBothBounds()
    {
        var a = new Bounds3D(0.0, 0.0, 0.0, 1.0, 1.0, 1.0);
        var b = new Bounds3D(-1.0, 0.5, 0.5, 0.5, 2.0, 0.75);
        var union = a.Union(b);

        Assert.Equal(-1.0, union.MinX);
        Assert.Equal(1.0, union.MaxX);
        Assert.Equal(2.0, union.MaxY);
        Assert.Equal(1.0, union.MaxZ);
    }

    [Fact]
    public void Union_WithEmpty_ReturnsOther()
    {
        var a = new Bounds3D(0.0, 0.0, 0.0, 1.0, 1.0, 1.0);

        var union1 = Bounds3D.Empty.Union(a);
        var union2 = a.Union(Bounds3D.Empty);

        Assert.False(union1.IsEmpty);
        Assert.Equal(a.MinX, union1.MinX);
        Assert.False(union2.IsEmpty);
        Assert.Equal(a.MaxX, union2.MaxX);
    }

    [Fact]
    public void Empty_HasZeroRadius()
    {
        Assert.True(Bounds3D.Empty.IsEmpty);
        Assert.Equal(0.0, Bounds3D.Empty.Radius);
    }
}
