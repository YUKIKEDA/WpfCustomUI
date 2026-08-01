using System.Numerics;

namespace WpfCustomUI.Viewport3D.Tests;

/// <summary>
/// 貫通矩形選択の判定数学(spec 6.24.4)・ホバー ID 解決(spec 6.24.3)・
/// 非同期構築の世代管理(spec 6.24.2)のテスト。
/// </summary>
public class ViewportRectSelectTests
{
    private const double PixelWidth = 100.0;
    private const double PixelHeight = 100.0;

    /// <summary>カメラ (0,0,10) → 原点、平行投影(幅・高さ 10)。原点はピクセル (50,50) に映る。</summary>
    private static Matrix4x4 CreateOrthoViewProj() =>
        Matrix4x4.CreateLookAt(new Vector3(0, 0, 10), Vector3.Zero, Vector3.UnitY)
        * Matrix4x4.CreateOrthographic(10, 10, 0.1f, 100.0f);

    /// <summary>カメラ (0,0,10) → 原点、透視投影(カメラ背後判定のテスト用)。</summary>
    private static Matrix4x4 CreatePerspectiveViewProj() =>
        Matrix4x4.CreateLookAt(new Vector3(0, 0, 10), Vector3.Zero, Vector3.UnitY)
        * Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1.0f, 0.1f, 100.0f);

    private static readonly Vector2 CenterRectMin = new(30, 30);
    private static readonly Vector2 CenterRectMax = new(70, 70);

    // ================= IsNodeInRect =================

    [Fact]
    public void IsNodeInRect_CenterNode_Inside()
    {
        var viewProj = CreateOrthoViewProj();
        double[] positions = [0.0, 0.0, 0.0];

        Assert.True(ViewportRectSelect.IsNodeInRect(
            positions, null, 0.0, 0, 0.0, 0.0, 0.0, in viewProj, PixelWidth, PixelHeight,
            CenterRectMin, CenterRectMax, ViewportSection.DisabledClip));
    }

    [Fact]
    public void IsNodeInRect_OffsetNode_Outside()
    {
        var viewProj = CreateOrthoViewProj();
        // x=4 → NDC 0.8 → ピクセル 90(矩形 30〜70 の外)
        double[] positions = [4.0, 0.0, 0.0];

        Assert.False(ViewportRectSelect.IsNodeInRect(
            positions, null, 0.0, 0, 0.0, 0.0, 0.0, in viewProj, PixelWidth, PixelHeight,
            CenterRectMin, CenterRectMax, ViewportSection.DisabledClip));
    }

    [Fact]
    public void IsNodeInRect_BehindCamera_Excluded()
    {
        var viewProj = CreatePerspectiveViewProj();
        double[] positions = [0.0, 0.0, 20.0]; // カメラ (z=10) の背後

        Assert.False(ViewportRectSelect.IsNodeInRect(
            positions, null, 0.0, 0, 0.0, 0.0, 0.0, in viewProj, PixelWidth, PixelHeight,
            CenterRectMin, CenterRectMax, ViewportSection.DisabledClip));
    }

    [Fact]
    public void IsNodeInRect_ClippedNode_Excluded()
    {
        var viewProj = CreateOrthoViewProj();
        double[] positions = [-1.0, 0.0, 0.0]; // x<0 はクリップ側

        var clip = new Vector4(1, 0, 0, 0); // x≥0 のみ表示
        Assert.False(ViewportRectSelect.IsNodeInRect(
            positions, null, 0.0, 0, 0.0, 0.0, 0.0, in viewProj, PixelWidth, PixelHeight,
            CenterRectMin, CenterRectMax, clip));

        // 同じ節点でもクリップ無効なら矩形内(x=-1 → ピクセル 40)
        Assert.True(ViewportRectSelect.IsNodeInRect(
            positions, null, 0.0, 0, 0.0, 0.0, 0.0, in viewProj, PixelWidth, PixelHeight,
            CenterRectMin, CenterRectMax, ViewportSection.DisabledClip));
    }

    [Fact]
    public void IsNodeInRect_UsesDeformedCoordinates()
    {
        var viewProj = CreateOrthoViewProj();
        double[] positions = [100.0, 0.0, 0.0];      // 非変形位置は画面外
        double[] displacements = [-100.0, 0.0, 0.0]; // 変形後は原点

        Assert.False(ViewportRectSelect.IsNodeInRect(
            positions, displacements, 0.0, 0, 0.0, 0.0, 0.0, in viewProj, PixelWidth, PixelHeight,
            CenterRectMin, CenterRectMax, ViewportSection.DisabledClip));

        Assert.True(ViewportRectSelect.IsNodeInRect(
            positions, displacements, 1.0, 0, 0.0, 0.0, 0.0, in viewProj, PixelWidth, PixelHeight,
            CenterRectMin, CenterRectMax, ViewportSection.DisabledClip));
    }

    [Fact]
    public void IsNodeInRect_InvalidIndex_False()
    {
        var viewProj = CreateOrthoViewProj();
        double[] positions = [0.0, 0.0, 0.0];

        Assert.False(ViewportRectSelect.IsNodeInRect(
            positions, null, 0.0, 5, 0.0, 0.0, 0.0, in viewProj, PixelWidth, PixelHeight,
            CenterRectMin, CenterRectMax, ViewportSection.DisabledClip));
    }

    // ================= IsTriangleInRect =================

    /// <summary>原点近傍の小三角形(全頂点が中央矩形の射影内)。</summary>
    private static (double[] Positions, int[] Triangles) CreateCenterTriangle() =>
        ([0.0, 0.0, 0.0, 0.5, 0.0, 0.0, 0.0, 0.5, 0.0], [0, 1, 2]);

    [Fact]
    public void IsTriangleInRect_AllVerticesInside_True()
    {
        var viewProj = CreateOrthoViewProj();
        var (positions, triangles) = CreateCenterTriangle();

        Assert.True(ViewportRectSelect.IsTriangleInRect(
            positions, triangles, null, 0.0, 0, 0.0, 0.0, 0.0, in viewProj, PixelWidth, PixelHeight,
            CenterRectMin, CenterRectMax, ViewportSection.DisabledClip));
    }

    [Fact]
    public void IsTriangleInRect_OneVertexOutside_False()
    {
        var viewProj = CreateOrthoViewProj();
        // 頂点 1 が x=4(ピクセル 90、矩形外)→ 厳密内包を満たさない
        double[] positions = [0.0, 0.0, 0.0, 4.0, 0.0, 0.0, 0.0, 0.5, 0.0];
        int[] triangles = [0, 1, 2];

        Assert.False(ViewportRectSelect.IsTriangleInRect(
            positions, triangles, null, 0.0, 0, 0.0, 0.0, 0.0, in viewProj, PixelWidth, PixelHeight,
            CenterRectMin, CenterRectMax, ViewportSection.DisabledClip));
    }

    [Fact]
    public void IsTriangleInRect_FullyClipped_Excluded()
    {
        var viewProj = CreateOrthoViewProj();
        // 全頂点 x<0(クリップ側)だが射影は矩形内
        double[] positions = [-0.5, 0.0, 0.0, -0.2, 0.0, 0.0, -0.5, 0.5, 0.0];
        int[] triangles = [0, 1, 2];
        var clip = new Vector4(1, 0, 0, 0);

        Assert.False(ViewportRectSelect.IsTriangleInRect(
            positions, triangles, null, 0.0, 0, 0.0, 0.0, 0.0, in viewProj, PixelWidth, PixelHeight,
            CenterRectMin, CenterRectMax, clip));
    }

    [Fact]
    public void IsTriangleInRect_PartiallyClipped_StillSelected()
    {
        var viewProj = CreateOrthoViewProj();
        // 1 頂点だけクリップ側 → 画面には部分的に残っているので選択対象
        double[] positions = [-0.5, 0.0, 0.0, 0.5, 0.0, 0.0, 0.0, 0.5, 0.0];
        int[] triangles = [0, 1, 2];
        var clip = new Vector4(1, 0, 0, 0);

        Assert.True(ViewportRectSelect.IsTriangleInRect(
            positions, triangles, null, 0.0, 0, 0.0, 0.0, 0.0, in viewProj, PixelWidth, PixelHeight,
            CenterRectMin, CenterRectMax, clip));
    }

    // ================= ScreenBoundsMayOverlapRect(チャンク AABB 粗篩) =================

    [Fact]
    public void ScreenBounds_OverlappingBox_True()
    {
        var viewProj = CreateOrthoViewProj();

        Assert.True(ViewportRectSelect.ScreenBoundsMayOverlapRect(
            new Vector3(-0.5f), new Vector3(0.5f), 0.0f,
            in viewProj, PixelWidth, PixelHeight, CenterRectMin, CenterRectMax));
    }

    [Fact]
    public void ScreenBounds_DisjointBox_False()
    {
        var viewProj = CreateOrthoViewProj();

        // x=3〜4 → ピクセル 80〜90(矩形 30〜70 と重ならない)
        Assert.False(ViewportRectSelect.ScreenBoundsMayOverlapRect(
            new Vector3(3.0f, -0.5f, -0.5f), new Vector3(4.0f, 0.5f, 0.5f), 0.0f,
            in viewProj, PixelWidth, PixelHeight, CenterRectMin, CenterRectMax));
    }

    [Fact]
    public void ScreenBounds_ExpandBringsIntoRect()
    {
        var viewProj = CreateOrthoViewProj();
        var min = new Vector3(3.0f, -0.5f, -0.5f);
        var max = new Vector3(4.0f, 0.5f, 0.5f);

        // 変形余白 2.5(x=0.5 まで拡大)で矩形にかかるようになる
        Assert.True(ViewportRectSelect.ScreenBoundsMayOverlapRect(
            min, max, 2.5f, in viewProj, PixelWidth, PixelHeight, CenterRectMin, CenterRectMax));
    }

    [Fact]
    public void ScreenBounds_SpansBehindCamera_ConservativeTrue()
    {
        var viewProj = CreatePerspectiveViewProj();

        // カメラ (z=10) を跨ぐ AABB は射影境界が定義できない → 保守的に true
        Assert.True(ViewportRectSelect.ScreenBoundsMayOverlapRect(
            new Vector3(50.0f, 50.0f, 5.0f), new Vector3(51.0f, 51.0f, 15.0f), 0.0f,
            in viewProj, PixelWidth, PixelHeight, CenterRectMin, CenterRectMax));
    }

    // ================= CollectTrianglesInRect =================

    [Fact]
    public void CollectTriangles_RangeRespected()
    {
        var viewProj = CreateOrthoViewProj();

        // 三角形 0: 中央(ヒット)、三角形 1: x=4 付近(矩形外)
        double[] positions =
        [
            0.0, 0.0, 0.0, 0.5, 0.0, 0.0, 0.0, 0.5, 0.0,
            4.0, 0.0, 0.0, 4.5, 0.0, 0.0, 4.0, 0.5, 0.0,
        ];
        int[] triangles = [0, 1, 2, 3, 4, 5];

        var hits = new List<int>();
        ViewportRectSelect.CollectTrianglesInRect(
            positions, triangles, null, 0.0, 0.0, 0.0, 0.0, in viewProj, PixelWidth, PixelHeight,
            CenterRectMin, CenterRectMax, ViewportSection.DisabledClip,
            triangleStart: 0, triangleCount: 2, hits);
        Assert.Equal([0], hits);

        // 範囲を三角形 1 だけに絞るとヒットなし
        hits.Clear();
        ViewportRectSelect.CollectTrianglesInRect(
            positions, triangles, null, 0.0, 0.0, 0.0, 0.0, in viewProj, PixelWidth, PixelHeight,
            CenterRectMin, CenterRectMax, ViewportSection.DisabledClip,
            triangleStart: 1, triangleCount: 1, hits);
        Assert.Empty(hits);
    }
}

/// <summary>ホバー ID バッファの読み出し(spec 6.24.3)のテスト。</summary>
public class ViewportHoverTests
{
    [Fact]
    public void ReadId_HitPixel_ReturnsMeshAndTriangle()
    {
        // 2×2 ピクセル、(1,0) にパーツ ID 3(メッシュインデックス 2)・三角形 42
        var buffer = new uint[2 * 2 * 2];
        buffer[(0 * 2 + 1) * 2] = 3;
        buffer[(0 * 2 + 1) * 2 + 1] = 42;

        var hit = ViewportHover.ReadId(buffer, 2, 2, 1, 0);
        Assert.Equal((2, 42), hit);
    }

    [Fact]
    public void ReadId_Background_ReturnsNull()
    {
        var buffer = new uint[2 * 2 * 2];
        Assert.Null(ViewportHover.ReadId(buffer, 2, 2, 0, 0));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(2, 0)]
    [InlineData(0, 2)]
    public void ReadId_OutOfBounds_ReturnsNull(int x, int y)
    {
        var buffer = new uint[2 * 2 * 2];
        buffer[0] = 1;
        Assert.Null(ViewportHover.ReadId(buffer, 2, 2, x, y));
    }
}

/// <summary>非同期ジオメトリ構築の世代管理(spec 6.24.2)のテスト。</summary>
public class GeometryBuildCoordinatorTests
{
    [Fact]
    public void Begin_ReturnsIncreasingGenerations()
    {
        var coordinator = new GeometryBuildCoordinator();
        var (g1, _) = coordinator.Begin();
        var (g2, _) = coordinator.Begin();

        Assert.True(g2 > g1);
        Assert.Equal(g2, coordinator.CurrentGeneration);
    }

    [Fact]
    public void Begin_CancelsPreviousToken()
    {
        var coordinator = new GeometryBuildCoordinator();
        var (g1, token1) = coordinator.Begin();
        Assert.False(token1.IsCancellationRequested);

        var (g2, token2) = coordinator.Begin();
        Assert.True(token1.IsCancellationRequested);   // 古い構築は途中破棄される
        Assert.False(token2.IsCancellationRequested);
        Assert.False(coordinator.IsCurrent(g1));       // 古い世代の結果は反映してはいけない
        Assert.True(coordinator.IsCurrent(g2));
    }

    [Fact]
    public void CancelAll_InvalidatesEverything()
    {
        var coordinator = new GeometryBuildCoordinator();
        var (generation, token) = coordinator.Begin();

        coordinator.CancelAll();
        Assert.True(token.IsCancellationRequested);
        Assert.False(coordinator.IsCurrent(generation));

        // その後の Begin は正常に新世代を開始できる
        var (next, nextToken) = coordinator.Begin();
        Assert.True(coordinator.IsCurrent(next));
        Assert.False(nextToken.IsCancellationRequested);
    }

    [Fact]
    public void RapidRestarts_OnlyLatestIsCurrent()
    {
        var coordinator = new GeometryBuildCoordinator();
        var generations = new List<(int Generation, CancellationToken Token)>();
        for (var i = 0; i < 5; i++)
        {
            generations.Add(coordinator.Begin());
        }

        for (var i = 0; i < 4; i++)
        {
            Assert.True(generations[i].Token.IsCancellationRequested);
            Assert.False(coordinator.IsCurrent(generations[i].Generation));
        }

        Assert.False(generations[4].Token.IsCancellationRequested);
        Assert.True(coordinator.IsCurrent(generations[4].Generation));
    }
}
