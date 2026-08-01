using System.Numerics;
using WpfCustomUI.Viewport3D;

namespace WpfCustomUI.Viewport3D.Tests;

public class ViewportPickingTests
{
    private const double Width = 800.0;
    private const double Height = 600.0;

    private static ViewportCamera CreateCamera() => new()
    {
        Target = Vector3.Zero,
        Yaw = 0.0,
        Pitch = 0.0,
        Distance = 10.0,
        SceneRadius = 5.0,
    };

    private static Matrix4x4 GetViewProj(ViewportCamera camera) =>
        camera.GetViewMatrix() * camera.GetProjectionMatrix(Width / Height);

    [Fact]
    public void ProjectToPixel_Target_MapsToViewportCenter()
    {
        var camera = CreateCamera();
        var viewProj = GetViewProj(camera);

        var pixel = ViewportPicking.ProjectToPixel(Vector3.Zero, in viewProj, Width, Height);

        Assert.NotNull(pixel);
        Assert.Equal(Width / 2.0, pixel.Value.X, 1e-2);
        Assert.Equal(Height / 2.0, pixel.Value.Y, 1e-2);
    }

    [Fact]
    public void ProjectToPixel_PointAboveTarget_MapsAboveCenter()
    {
        var camera = CreateCamera();
        var viewProj = GetViewProj(camera);
        var (_, up) = camera.GetViewBasis();

        var pixel = ViewportPicking.ProjectToPixel(up * 1.0f, in viewProj, Width, Height);

        Assert.NotNull(pixel);
        // スクリーン Y は下向きなので「上の点」は中央より小さい Y
        Assert.True(pixel.Value.Y < Height / 2.0);
        Assert.Equal(Width / 2.0, pixel.Value.X, 1e-2);
    }

    [Fact]
    public void ProjectToPixel_BehindCamera_ReturnsNull()
    {
        var camera = CreateCamera();
        var viewProj = GetViewProj(camera);

        // カメラは +X 側(距離 10)。x=+20 はカメラの後ろ
        var pixel = ViewportPicking.ProjectToPixel(new Vector3(20.0f, 0.0f, 0.0f), in viewProj, Width, Height);

        Assert.Null(pixel);
    }

    [Fact]
    public void FindNearestNodeOnTriangle_ReturnsNodeClosestToCursor()
    {
        // カメラは +X 側から YZ 平面を見る。三角形は YZ 平面上
        var camera = CreateCamera();
        var viewProj = GetViewProj(camera);

        var mesh = new ViewportMesh
        {
            Positions =
            [
                0.0, -2.0, -2.0, // node 0: 画面左下
                0.0, 2.0, -2.0,  // node 1: 画面右下
                0.0, 0.0, 2.0,   // node 2: 画面上
            ],
            TriangleIndices = [0, 1, 2],
        };

        // node 2(画面上部)の射影位置近くをクリック
        var node2Pixel = ViewportPicking.ProjectToPixel(
            new Vector3(0.0f, 0.0f, 2.0f), in viewProj, Width, Height)!.Value;
        var cursor = node2Pixel + new Vector2(3.0f, 3.0f);

        var nearest = ViewportPicking.FindNearestNodeOnTriangle(
            mesh, 0, 0.0, 0.0, 0.0, in viewProj, Width, Height, cursor);

        Assert.Equal(2, nearest);
    }

    [Fact]
    public void FindNearestNodeOnTriangle_InvalidTriangle_ReturnsNull()
    {
        var camera = CreateCamera();
        var viewProj = GetViewProj(camera);
        var mesh = new ViewportMesh
        {
            Positions = [0.0, 0.0, 0.0],
            TriangleIndices = [0, 0, 0],
        };

        var result = ViewportPicking.FindNearestNodeOnTriangle(
            mesh, 5, 0.0, 0.0, 0.0, in viewProj, Width, Height, Vector2.Zero);

        Assert.Null(result);
    }

    [Fact]
    public void FindNodesInRectangle_ReturnsOnlyNodesInsideRect()
    {
        var camera = CreateCamera();
        var viewProj = GetViewProj(camera);

        var mesh = new ViewportMesh
        {
            Positions =
            [
                0.0, -2.0, -2.0, // node 0
                0.0, 2.0, -2.0,  // node 1
                0.0, 0.0, 2.0,   // node 2
            ],
            TriangleIndices = [0, 1, 2],
        };

        // node 2 の射影位置まわりの小さな矩形
        var node2Pixel = ViewportPicking.ProjectToPixel(
            new Vector3(0.0f, 0.0f, 2.0f), in viewProj, Width, Height)!.Value;
        var rectMin = node2Pixel - new Vector2(10.0f, 10.0f);
        var rectMax = node2Pixel + new Vector2(10.0f, 10.0f);

        var nodes = ViewportPicking.FindNodesInRectangle(
            mesh, [0], 0.0, 0.0, 0.0, in viewProj, Width, Height, rectMin, rectMax);

        Assert.Single(nodes);
        Assert.Contains(2, nodes);
    }

    [Fact]
    public void FindNodesInRectangle_FullViewportRect_ReturnsAllTriangleNodes()
    {
        var camera = CreateCamera();
        var viewProj = GetViewProj(camera);

        var mesh = new ViewportMesh
        {
            Positions =
            [
                0.0, -2.0, -2.0,
                0.0, 2.0, -2.0,
                0.0, 0.0, 2.0,
            ],
            TriangleIndices = [0, 1, 2],
        };

        var nodes = ViewportPicking.FindNodesInRectangle(
            mesh, [0], 0.0, 0.0, 0.0, in viewProj, Width, Height,
            Vector2.Zero, new Vector2((float)Width, (float)Height));

        Assert.Equal(3, nodes.Count);
    }

    [Fact]
    public void FindNodesInRectangle_RecenteringOriginIsApplied()
    {
        var camera = CreateCamera();
        var viewProj = GetViewProj(camera);

        // 大座標のメッシュ(原点 1e6)でも、origin を渡せばローカル座標で判定される
        const double origin = 1.0e6;
        var mesh = new ViewportMesh
        {
            Positions =
            [
                origin, origin - 2.0, origin - 2.0,
                origin, origin + 2.0, origin - 2.0,
                origin, origin, origin + 2.0,
            ],
            TriangleIndices = [0, 1, 2],
        };

        var nodes = ViewportPicking.FindNodesInRectangle(
            mesh, [0], origin, origin, origin, in viewProj, Width, Height,
            Vector2.Zero, new Vector2((float)Width, (float)Height));

        Assert.Equal(3, nodes.Count);
    }
}
