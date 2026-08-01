using System.Numerics;
using WpfCustomUI.Viewport3D;

namespace WpfCustomUI.Viewport3D.Tests;

/// <summary>Octahedral 法線圧縮の純粋関数テスト(spec 6.23.2)。</summary>
public class OctahedralNormalTests
{
    [Theory]
    [InlineData(1f, 0f, 0f)]
    [InlineData(-1f, 0f, 0f)]
    [InlineData(0f, 1f, 0f)]
    [InlineData(0f, -1f, 0f)]
    [InlineData(0f, 0f, 1f)]
    [InlineData(0f, 0f, -1f)]
    public void EncodeDecode_Axes_Roundtrip(float x, float y, float z)
    {
        var (dx, dy, dz) = ViewportGeometry.DecodeOctahedralNormal(
            ViewportGeometry.EncodeOctahedralNormal(x, y, z));

        Assert.Equal(x, dx, 3);
        Assert.Equal(y, dy, 3);
        Assert.Equal(z, dz, 3);
    }

    [Fact]
    public void EncodeDecode_RandomUnitVectors_SmallAngleError()
    {
        // 16bit×2 量子化の最大角度誤差は約 0.02°(cos誤差 1e-7 級)。余裕を見て 0.1°で検証
        var random = new Random(12345);
        for (var i = 0; i < 1000; i++)
        {
            var v = RandomUnit(random);
            var (dx, dy, dz) = ViewportGeometry.DecodeOctahedralNormal(
                ViewportGeometry.EncodeOctahedralNormal(v.X, v.Y, v.Z));

            var dot = Math.Clamp(v.X * dx + v.Y * dy + v.Z * dz, -1.0f, 1.0f);
            var angleDeg = Math.Acos(dot) * 180.0 / Math.PI;
            Assert.True(angleDeg < 0.1, $"角度誤差 {angleDeg:F4}° (法線 {v})");
        }
    }

    [Fact]
    public void Encode_ZeroNormal_DecodesToUnitZ()
    {
        // ゼロ法線(未参照節点)は +Z 扱い(NaN を出さない)
        var (x, y, z) = ViewportGeometry.DecodeOctahedralNormal(
            ViewportGeometry.EncodeOctahedralNormal(0f, 0f, 0f));

        Assert.Equal(0f, x, 3);
        Assert.Equal(0f, y, 3);
        Assert.Equal(1f, z, 3);
    }

    [Fact]
    public void CompressNormals_MatchesElementwiseEncode()
    {
        float[] normals = [1f, 0f, 0f, 0f, 0.6f, 0.8f, -0.577f, -0.577f, -0.577f];
        var packed = ViewportGeometry.CompressNormals(normals);

        Assert.Equal(3, packed.Length);
        for (var v = 0; v < 3; v++)
        {
            Assert.Equal(
                ViewportGeometry.EncodeOctahedralNormal(normals[v * 3], normals[v * 3 + 1], normals[v * 3 + 2]),
                packed[v]);
        }
    }

    [Fact]
    public void ComputeVertexNormalsFromDouble_MatchesFloatPath()
    {
        // ストリーミング版(double 直読み)は「float 化 → ComputeVertexNormals」と完全一致する
        var random = new Random(7);
        var positions = new double[30 * 3];
        for (var i = 0; i < positions.Length; i++)
        {
            positions[i] = 1_000_000.0 + random.NextDouble() * 50.0;
        }

        var triangles = new int[20 * 3];
        for (var i = 0; i < triangles.Length; i++)
        {
            triangles[i] = random.Next(30);
        }

        const double ox = 1_000_025.0, oy = 1_000_012.0, oz = 1_000_037.0;
        var local = ViewportGeometry.ToLocalPositions(positions, ox, oy, oz);
        var expected = ViewportGeometry.ComputeVertexNormals(local, triangles);
        var actual = ViewportGeometry.ComputeVertexNormalsFromDouble(positions, ox, oy, oz, triangles);

        Assert.Equal(expected, actual);
    }

    private static Vector3 RandomUnit(Random random)
    {
        while (true)
        {
            var v = new Vector3(
                (float)(random.NextDouble() * 2.0 - 1.0),
                (float)(random.NextDouble() * 2.0 - 1.0),
                (float)(random.NextDouble() * 2.0 - 1.0));
            var len = v.Length();
            if (len is > 0.01f and < 1.0f)
            {
                return v / len;
            }
        }
    }
}

/// <summary>グリッドクラスタリング LOD の純粋関数テスト(spec 6.23.3)。</summary>
public class ViewportLodTests
{
    [Fact]
    public void ComputeGridResolution_FollowsSquareRootLaw()
    {
        // 表面メッシュでは占有セル数 ∝ 解像度² のため √(V/20) が基準
        Assert.Equal(2, ViewportLod.ComputeGridResolution(1));
        Assert.Equal(23, ViewportLod.ComputeGridResolution(10_000)); // √500 = 22.36 → 23
        Assert.True(ViewportLod.ComputeGridResolution(100_000_000) <= ViewportLod.MaxGridResolution);

        // 頂点数に対して単調非減少
        var prev = 0;
        foreach (var v in (int[])[100, 10_000, 1_000_000, 100_000_000])
        {
            var r = ViewportLod.ComputeGridResolution(v);
            Assert.True(r >= prev);
            prev = r;
        }
    }

    [Fact]
    public void ComputeCellKey_DistinctCells_DistinctKeys()
    {
        var bounds = new Bounds3D(0, 0, 0, 10, 10, 10);
        var k000 = ViewportLod.ComputeCellKey(0.5, 0.5, 0.5, bounds, 10);
        var k100 = ViewportLod.ComputeCellKey(1.5, 0.5, 0.5, bounds, 10);
        var k010 = ViewportLod.ComputeCellKey(0.5, 1.5, 0.5, bounds, 10);
        var k001 = ViewportLod.ComputeCellKey(0.5, 0.5, 1.5, bounds, 10);

        Assert.NotEqual(k000, k100);
        Assert.NotEqual(k000, k010);
        Assert.NotEqual(k000, k001);
        Assert.NotEqual(k100, k010);

        // 同一セル内は同一キー、境界値(最大)はクランプされる
        Assert.Equal(k000, ViewportLod.ComputeCellKey(0.9, 0.9, 0.9, bounds, 10));
        Assert.Equal(
            ViewportLod.ComputeCellKey(10.0, 10.0, 10.0, bounds, 10),
            ViewportLod.ComputeCellKey(9.95, 9.95, 9.95, bounds, 10));
    }

    [Fact]
    public void Build_ClustersVerticesAndDropsDegenerateTriangles()
    {
        // 2 セルに分かれる 4 頂点: (0,1) は左セル、(2,3) は右セル
        // 三角形 0-1-2 は左左右 → 退化(左セルの代表 1 点に潰れるため)ではなく 2 点になり退化。
        // 三角形 0-2-3 と 1-2-3 は同じ (左, 右...) → 重複除去の確認も兼ねる
        double[] positions =
        [
            0.0, 0.0, 0.0,   // v0: 左セル(代表 = 最小インデックス)
            0.1, 0.1, 0.0,   // v1: 左セル
            9.0, 0.0, 0.0,   // v2: 右セル
            9.1, 0.1, 0.0,   // v3: 右セル
        ];
        int[] triangles =
        [
            0, 1, 2, // 左セル内 2 頂点 → 退化して除去
            0, 2, 3, // 左-右-右 → 右セル 2 頂点で退化して除去
            1, 2, 3, // 同上(重複でもある)
        ];
        var bounds = new Bounds3D(0, 0, 0, 10, 10, 10);

        // 解像度 2 → セル幅 5: v0/v1 が同セル、v2/v3 が同セル
        var lod = ViewportLod.Build(positions, triangles, null, bounds, reductionFactor: 2);

        // 全三角形が退化 → null
        Assert.Null(lod);
    }

    [Fact]
    public void Build_RepresentativeIsLowestIndex_AndScalarIsAveraged()
    {
        // 3 つの離れたセルに 1〜2 頂点ずつ配置した三角形メッシュ
        double[] positions =
        [
            0.0, 0.0, 0.0,   // v0: セル A
            0.2, 0.0, 0.0,   // v1: セル A(v0 と同セル)
            9.0, 0.0, 0.0,   // v2: セル B
            0.0, 9.0, 0.0,   // v3: セル C
        ];
        int[] triangles = [0, 2, 3, 1, 2, 3];
        double[] scalars = [1.0, 3.0, 5.0, 7.0];
        var bounds = new Bounds3D(0, 0, 0, 10, 10, 10);

        var lod = ViewportLod.Build(positions, triangles, scalars, bounds, reductionFactor: 8);

        Assert.NotNull(lod);
        Assert.Equal(3, lod.VertexCount);

        // 代表節点 = セル内最小インデックス(v0/v1 → v0)、発見順で番号付け
        Assert.Equal([0, 2, 3], lod.RepresentativeNodes);

        // スカラーはセル内平均: セル A = (1+3)/2 = 2
        Assert.NotNull(lod.ScalarValues);
        Assert.Equal(2.0, lod.ScalarValues[0], 12);
        Assert.Equal(5.0, lod.ScalarValues[1], 12);
        Assert.Equal(7.0, lod.ScalarValues[2], 12);

        // 2 つの三角形は同じクラスタ三角形に写像 → 重複除去で 1 枚(最初の巻き方向)
        Assert.Equal([0, 1, 2], lod.TriangleIndices);
    }

    [Fact]
    public void Build_LargeMesh_ReducesTriangles_Deterministic()
    {
        // 100×100 格子(19,602 三角形) → 1/20 目標でおおよそ 1/10〜1/40 に減る
        const int side = 100;
        const int n = side + 1;
        var positions = new double[n * n * 3];
        var scalars = new double[n * n];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                var v = i * n + j;
                positions[v * 3] = i;
                positions[v * 3 + 1] = j;
                positions[v * 3 + 2] = Math.Sin(i * 0.1) * 3.0;
                scalars[v] = i + j;
            }
        }

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

        var bounds = ViewportGeometry.ComputeBounds(positions);
        var lod = ViewportLod.Build(positions, triangles, scalars, bounds);

        Assert.NotNull(lod);
        var sourceTriangles = triangles.Length / 3;
        Assert.InRange(lod.TriangleCount, sourceTriangles / 40, sourceTriangles / 10);
        Assert.InRange(lod.VertexCount, n * n / 40, n * n / 10);

        // 全 LOD 三角形が有効な頂点を参照し、退化・重複がない
        var seen = new HashSet<(int, int, int)>();
        for (var t = 0; t < lod.TriangleCount; t++)
        {
            int a = lod.TriangleIndices[t * 3], b = lod.TriangleIndices[t * 3 + 1], c = lod.TriangleIndices[t * 3 + 2];
            Assert.InRange(a, 0, lod.VertexCount - 1);
            Assert.InRange(b, 0, lod.VertexCount - 1);
            Assert.InRange(c, 0, lod.VertexCount - 1);
            Assert.True(a != b && b != c && a != c);

            Span<int> sorted = [a, b, c];
            sorted.Sort();
            Assert.True(seen.Add((sorted[0], sorted[1], sorted[2])), "重複三角形が残っている");
        }

        // 代表節点は昇順に発見される(ブロック順+走査順の決定性)
        for (var i = 1; i < lod.RepresentativeNodes.Length; i++)
        {
            Assert.True(lod.RepresentativeNodes[i] > lod.RepresentativeNodes[i - 1]);
        }

        // 決定性: 2 回ビルドして完全一致(ピクセル差分検証の前提)
        var lod2 = ViewportLod.Build(positions, triangles, scalars, bounds);
        Assert.NotNull(lod2);
        Assert.Equal(lod.RepresentativeNodes, lod2.RepresentativeNodes);
        Assert.Equal(lod.TriangleIndices, lod2.TriangleIndices);
        Assert.Equal(lod.ScalarValues, lod2.ScalarValues);
    }

    [Fact]
    public void Build_EmptyOrTiny_ReturnsNull()
    {
        var bounds = new Bounds3D(0, 0, 0, 1, 1, 1);
        Assert.Null(ViewportLod.Build([], [], null, bounds));
        Assert.Null(ViewportLod.Build([0.0, 0.0, 0.0], [], null, bounds));
        Assert.Null(ViewportLod.Build([0.0, 0.0, 0.0], [0, 0, 0], null, Bounds3D.Empty));
    }
}

/// <summary>フラスタムカリング数学のテスト(spec 6.23.4)。</summary>
public class ViewportCullingTests
{
    [Fact]
    public void ExtractFrustumPlanes_Orthographic_UnitCube()
    {
        // [-1,1]³ を [-1,1]²×[0,1] に写す正射影(D3D 規約: z' = z*0.5+0.5)
        var ortho = Matrix4x4.CreateOrthographic(2.0f, 2.0f, -1.0f, 1.0f);
        Span<Vector4> planes = stackalloc Vector4[ViewportCulling.PlaneCount];
        ViewportCulling.ExtractFrustumPlanes(in ortho, planes);

        // 原点は全平面の内側
        for (var i = 0; i < ViewportCulling.PlaneCount; i++)
        {
            Assert.True(planes[i].W >= 0.0f, $"平面 {i} が原点を外側と判定");
        }

        // 内側 AABB は可視、x 方向に外れた AABB は不可視
        Assert.True(ViewportCulling.IntersectsFrustum(
            planes, new Vector3(-0.5f), new Vector3(0.5f)));
        Assert.False(ViewportCulling.IntersectsFrustum(
            planes, new Vector3(2.0f, -0.5f, -0.5f), new Vector3(3.0f, 0.5f, 0.5f)));
        Assert.False(ViewportCulling.IntersectsFrustum(
            planes, new Vector3(-0.5f, 2.0f, -0.5f), new Vector3(0.5f, 3.0f, 0.5f)));
    }

    [Fact]
    public void IntersectsFrustum_Perspective_BehindCameraIsCulled()
    {
        // 視点 (0,0,5) から -Z を見るビュー+透視射影(行ベクトル規約: view * proj)
        var view = Matrix4x4.CreateLookAt(new Vector3(0, 0, 5), Vector3.Zero, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4.0f, 1.0f, 0.1f, 100.0f);
        var viewProj = view * proj;

        Span<Vector4> planes = stackalloc Vector4[ViewportCulling.PlaneCount];
        ViewportCulling.ExtractFrustumPlanes(in viewProj, planes);

        // 注視点付近は可視
        Assert.True(ViewportCulling.IntersectsFrustum(
            planes, new Vector3(-0.5f), new Vector3(0.5f)));

        // カメラの後方は不可視
        Assert.False(ViewportCulling.IntersectsFrustum(
            planes, new Vector3(-0.5f, -0.5f, 9.0f), new Vector3(0.5f, 0.5f, 10.0f)));

        // 視野角の外(真横)は不可視
        Assert.False(ViewportCulling.IntersectsFrustum(
            planes, new Vector3(50.0f, -0.5f, -0.5f), new Vector3(51.0f, 0.5f, 0.5f)));

        // far より遠くは不可視
        Assert.False(ViewportCulling.IntersectsFrustum(
            planes, new Vector3(-0.5f, -0.5f, -200.0f), new Vector3(0.5f, 0.5f, -150.0f)));
    }

    [Fact]
    public void IntersectsFrustum_ExpandMakesBorderlineVisible()
    {
        var ortho = Matrix4x4.CreateOrthographic(2.0f, 2.0f, -1.0f, 1.0f);
        Span<Vector4> planes = stackalloc Vector4[ViewportCulling.PlaneCount];
        ViewportCulling.ExtractFrustumPlanes(in ortho, planes);

        // 右端の少し外(x ∈ [1.2, 2.0])は expand=0 で不可視、expand=0.5(最大変位相当)で可視
        var min = new Vector3(1.2f, -0.5f, -0.5f);
        var max = new Vector3(2.0f, 0.5f, 0.5f);
        Assert.False(ViewportCulling.IntersectsFrustum(planes, min, max));
        Assert.True(ViewportCulling.IntersectsFrustum(planes, min, max, expand: 0.5f));
    }

    [Fact]
    public void IsOutsidePlane_PositiveVertexTest()
    {
        // 平面 x = 0(法線 +X): x < 0 側が外側
        var plane = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
        Assert.True(ViewportCulling.IsOutsidePlane(plane, new Vector3(-2f), new Vector3(-1f)));
        Assert.False(ViewportCulling.IsOutsidePlane(plane, new Vector3(-1f), new Vector3(1f))); // 跨ぐ
        Assert.False(ViewportCulling.IsOutsidePlane(plane, new Vector3(1f), new Vector3(2f)));
    }
}
