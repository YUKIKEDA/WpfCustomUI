using System.Numerics;
using WpfCustomUI.Viewport3D;

namespace WpfCustomUI.Viewport3D.Tests;

public class ViewportProbingTests
{
    private const double Width = 800.0;
    private const double Height = 600.0;

    /// <summary>テスト用のビュー射影行列(原点近くを +Z 側から見る、D3D 深度規約)。</summary>
    private static Matrix4x4 CreateViewProj(Vector3 eye, Vector3 target)
    {
        var view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(
            (float)(Math.PI / 4.0), (float)(Width / Height), 0.1f, 1000.0f);
        return view * proj;
    }

    // ================= ComputePickRay =================

    [Fact]
    public void ComputePickRay_PassesThroughProjectedPoint()
    {
        // 射影 → 逆射影のラウンドトリップ: 3D 点をピクセルへ射影し、
        // そのピクセルから作ったレイが元の点を通ること
        var viewProj = CreateViewProj(new Vector3(3.0f, 2.0f, 10.0f), Vector3.Zero);
        var point = new Vector3(0.7f, -0.4f, 0.2f);

        var pixel = ViewportPicking.ProjectToPixel(point, in viewProj, Width, Height);
        Assert.NotNull(pixel);

        var ray = ViewportProbing.ComputePickRay(pixel.Value, in viewProj, Width, Height);
        Assert.NotNull(ray);

        // 点とレイ(直線)の距離
        var toPoint = point - ray.Value.Origin;
        var distance = Vector3.Cross(toPoint, ray.Value.Direction).Length();
        Assert.True(distance < 1e-3f, $"レイが点を通っていない: distance={distance}");
    }

    [Fact]
    public void ComputePickRay_SingularMatrix_ReturnsNull()
    {
        var singular = default(Matrix4x4); // 全成分 0 → 逆行列なし
        Assert.Null(ViewportProbing.ComputePickRay(new Vector2(10.0f, 10.0f), in singular, Width, Height));
    }

    // ================= TryIntersectTriangle =================

    [Fact]
    public void TryIntersectTriangle_CentroidHit_ReturnsUniformBarycentric()
    {
        var v0 = new Vector3(0.0f, 0.0f, 0.0f);
        var v1 = new Vector3(3.0f, 0.0f, 0.0f);
        var v2 = new Vector3(0.0f, 3.0f, 0.0f);
        var centroid = (v0 + v1 + v2) / 3.0f;
        var origin = centroid + new Vector3(0.0f, 0.0f, 5.0f);

        var hit = ViewportProbing.TryIntersectTriangle(
            origin, new Vector3(0.0f, 0.0f, -1.0f), v0, v1, v2, out var t, out var bary);

        Assert.True(hit);
        Assert.Equal(5.0f, t, 4);
        Assert.Equal(1.0f / 3.0f, bary.X, 4);
        Assert.Equal(1.0f / 3.0f, bary.Y, 4);
        Assert.Equal(1.0f / 3.0f, bary.Z, 4);
    }

    [Fact]
    public void TryIntersectTriangle_BackFace_StillHits()
    {
        // 両面判定(裏向きの三角形もヒットする)
        var v0 = new Vector3(0.0f, 0.0f, 0.0f);
        var v1 = new Vector3(0.0f, 3.0f, 0.0f); // 巻き順を逆に
        var v2 = new Vector3(3.0f, 0.0f, 0.0f);

        var hit = ViewportProbing.TryIntersectTriangle(
            new Vector3(1.0f, 1.0f, 5.0f), new Vector3(0.0f, 0.0f, -1.0f),
            v0, v1, v2, out _, out _);

        Assert.True(hit);
    }

    [Fact]
    public void TryIntersectTriangle_OutsideTriangle_Misses()
    {
        var v0 = new Vector3(0.0f, 0.0f, 0.0f);
        var v1 = new Vector3(3.0f, 0.0f, 0.0f);
        var v2 = new Vector3(0.0f, 3.0f, 0.0f);

        var hit = ViewportProbing.TryIntersectTriangle(
            new Vector3(5.0f, 5.0f, 5.0f), new Vector3(0.0f, 0.0f, -1.0f),
            v0, v1, v2, out _, out _);

        Assert.False(hit);
    }

    [Fact]
    public void TryIntersectTriangle_BehindRay_Misses()
    {
        var v0 = new Vector3(0.0f, 0.0f, 0.0f);
        var v1 = new Vector3(3.0f, 0.0f, 0.0f);
        var v2 = new Vector3(0.0f, 3.0f, 0.0f);

        // 三角形はレイ始点の背後(+Z 方向に離れる)
        var hit = ViewportProbing.TryIntersectTriangle(
            new Vector3(1.0f, 1.0f, -5.0f), new Vector3(0.0f, 0.0f, -1.0f),
            v0, v1, v2, out _, out _);

        Assert.False(hit);
    }

    [Fact]
    public void TryIntersectTriangle_ParallelRay_Misses()
    {
        var v0 = new Vector3(0.0f, 0.0f, 0.0f);
        var v1 = new Vector3(3.0f, 0.0f, 0.0f);
        var v2 = new Vector3(0.0f, 3.0f, 0.0f);

        var hit = ViewportProbing.TryIntersectTriangle(
            new Vector3(1.0f, 1.0f, 5.0f), new Vector3(1.0f, 0.0f, 0.0f),
            v0, v1, v2, out _, out _);

        Assert.False(hit);
    }

    [Fact]
    public void TryIntersectTriangle_NearEdge_ClampsAndNormalizes()
    {
        var v0 = new Vector3(0.0f, 0.0f, 0.0f);
        var v1 = new Vector3(3.0f, 0.0f, 0.0f);
        var v2 = new Vector3(0.0f, 3.0f, 0.0f);

        // v0-v1 辺のわずかに外側(許容幅内)→ クランプされ、重みの合計は 1
        var hit = ViewportProbing.TryIntersectTriangle(
            new Vector3(1.5f, -0.01f, 5.0f), new Vector3(0.0f, 0.0f, -1.0f),
            v0, v1, v2, out _, out var bary);

        Assert.True(hit);
        Assert.True(bary.X >= 0.0f && bary.Y >= 0.0f && bary.Z >= 0.0f);
        Assert.Equal(1.0f, bary.X + bary.Y + bary.Z, 4);
    }

    // ================= Probe(統合) =================

    private static ViewportMesh CreateUnitTriangleMesh() => new()
    {
        Name = "tri",
        Positions =
        [
            0.0, 0.0, 0.0,
            4.0, 0.0, 0.0,
            0.0, 4.0, 0.0,
        ],
        TriangleIndices = [0, 1, 2],
        ScalarValues = [10.0, 30.0, 50.0],
    };

    [Fact]
    public void Probe_InterpolatesScalarAtClickedPoint()
    {
        var mesh = CreateUnitTriangleMesh();
        var viewProj = CreateViewProj(new Vector3(1.0f, 1.0f, 12.0f), new Vector3(1.0f, 1.0f, 0.0f));

        // 既知の内部点(重心座標 0.5, 0.25, 0.25)をピクセルへ射影してからプローブする
        var target = new Vector3(
            0.25f * 4.0f, // x = w1 * 4
            0.25f * 4.0f, // y = w2 * 4
            0.0f);
        var pixel = ViewportPicking.ProjectToPixel(target, in viewProj, Width, Height)!.Value;

        var result = ViewportProbing.Probe(
            mesh, 0, pixel, in viewProj, Width, Height, 0.0, 0.0, 0.0, 0.0);

        Assert.NotNull(result);
        var expected = 0.5 * 10.0 + 0.25 * 30.0 + 0.25 * 50.0; // = 25
        Assert.Equal(expected, result.ScalarValue!.Value, 1);
        Assert.Equal(0, result.NodeIndex); // 重心座標最大の頂点 = v0
        Assert.Equal(10.0, result.NodeScalarValue!.Value, 6);
        Assert.Equal(target.X, result.X, 2);
        Assert.Equal(target.Y, result.Y, 2);
        Assert.Same(mesh, result.Mesh);
        Assert.Equal(0, result.TriangleIndex);
    }

    [Fact]
    public void Probe_WithDeformation_HitsDeformedTriangle_ReturnsUndeformedCoords()
    {
        // 頂点を +X に 2 ずつ(スケール 0.5 で 1)ずらした変形表示をプローブする
        var mesh = CreateUnitTriangleMesh();
        mesh.Displacements =
        [
            2.0, 0.0, 0.0,
            2.0, 0.0, 0.0,
            2.0, 0.0, 0.0,
        ];
        const double scale = 0.5;
        var viewProj = CreateViewProj(new Vector3(2.0f, 1.0f, 12.0f), new Vector3(2.0f, 1.0f, 0.0f));

        // 変形後の重心(モデル重心 + (1,0,0))をクリック
        var deformedCentroid = new Vector3(4.0f / 3.0f + 1.0f, 4.0f / 3.0f, 0.0f);
        var pixel = ViewportPicking.ProjectToPixel(deformedCentroid, in viewProj, Width, Height)!.Value;

        var result = ViewportProbing.Probe(
            mesh, 0, pixel, in viewProj, Width, Height, 0.0, 0.0, 0.0, scale);

        Assert.NotNull(result);
        // ヒット点のモデル座標は変形前の形状上(重心 ≒ (4/3, 4/3))
        Assert.Equal(4.0 / 3.0, result.X, 2);
        Assert.Equal(4.0 / 3.0, result.Y, 2);
        Assert.Equal(30.0, result.ScalarValue!.Value, 0); // ほぼ重心 → 平均 30
    }

    [Fact]
    public void Probe_MissedRay_FallsBackToNearestNode()
    {
        // 三角形の外のピクセル(GPU ID パスとのサブピクセルずれを想定)でも
        // 最近傍節点スナップで結果を返す
        var mesh = CreateUnitTriangleMesh();
        var viewProj = CreateViewProj(new Vector3(1.0f, 1.0f, 12.0f), new Vector3(1.0f, 1.0f, 0.0f));

        // v1 (4,0,0) の少し外側
        var outside = new Vector3(4.5f, -0.5f, 0.0f);
        var pixel = ViewportPicking.ProjectToPixel(outside, in viewProj, Width, Height)!.Value;

        var result = ViewportProbing.Probe(
            mesh, 0, pixel, in viewProj, Width, Height, 0.0, 0.0, 0.0, 0.0);

        Assert.NotNull(result);
        Assert.Equal(1, result.NodeIndex);
        Assert.Equal(30.0, result.ScalarValue!.Value, 6); // 節点値そのもの
        Assert.Equal(4.0, result.X, 4);
    }

    [Fact]
    public void Probe_InvalidTriangleIndex_ReturnsNull()
    {
        var mesh = CreateUnitTriangleMesh();
        var viewProj = CreateViewProj(new Vector3(0.0f, 0.0f, 10.0f), Vector3.Zero);

        Assert.Null(ViewportProbing.Probe(
            mesh, 5, new Vector2(400.0f, 300.0f), in viewProj, Width, Height, 0.0, 0.0, 0.0, 0.0));
        Assert.Null(ViewportProbing.Probe(
            mesh, -1, new Vector2(400.0f, 300.0f), in viewProj, Width, Height, 0.0, 0.0, 0.0, 0.0));
    }

    [Fact]
    public void Probe_MeshWithoutScalars_ReturnsNullScalar()
    {
        var mesh = CreateUnitTriangleMesh();
        mesh.ScalarValues = null;
        var viewProj = CreateViewProj(new Vector3(1.0f, 1.0f, 12.0f), new Vector3(1.0f, 1.0f, 0.0f));
        var pixel = ViewportPicking.ProjectToPixel(
            new Vector3(1.0f, 1.0f, 0.0f), in viewProj, Width, Height)!.Value;

        var result = ViewportProbing.Probe(
            mesh, 0, pixel, in viewProj, Width, Height, 0.0, 0.0, 0.0, 0.0);

        Assert.NotNull(result);
        Assert.Null(result.ScalarValue);
        Assert.Null(result.NodeScalarValue);
    }

    // ================= 既定ラベル書式 =================

    [Fact]
    public void FormatDefaultProbeLabel_WithScalar_UsesInvariantCulture()
    {
        var mesh = CreateUnitTriangleMesh();
        var result = new ProbeResult(mesh, 0, 12, 1.0, 2.0, 3.0, 123.456, 120.0);

        Assert.Equal("N12: 123.5", WcuViewport.FormatDefaultProbeLabel(result));
    }

    [Fact]
    public void FormatDefaultProbeLabel_WithoutScalar_ShowsCoordinates()
    {
        var mesh = CreateUnitTriangleMesh();
        var result = new ProbeResult(mesh, 0, 3, 1.5, -2.0, 0.25, null, null);

        Assert.Equal("N3 (1.5, -2, 0.25)", WcuViewport.FormatDefaultProbeLabel(result));
    }

    // ================= クリップ非表示判定(spec 6.20.4) =================

    [Fact]
    public void IsClipped_DisabledClip_NeverClips()
    {
        Assert.False(ViewportSection.IsClipped(new Vector3(100.0f, -50.0f, 3.0f), ViewportSection.DisabledClip));
        Assert.False(ViewportSection.IsClipped(Vector3.Zero, ViewportSection.DisabledClip));
    }

    [Fact]
    public void IsClipped_MatchesSignedDistance()
    {
        // 平面 z=1、法線 +Z → z<1 がクリップ
        var clip = ViewportSection.ComputeClipCoefficients(
            new SectionPlane(0.0, 0.0, 1.0, 0.0, 0.0, 1.0), 0.0, 0.0, 0.0)!.Value;

        Assert.True(ViewportSection.IsClipped(new Vector3(0.0f, 0.0f, 0.0f), clip));
        Assert.False(ViewportSection.IsClipped(new Vector3(0.0f, 0.0f, 2.0f), clip));
    }

    // ================= ViewportAnnotation =================

    [Fact]
    public void ViewportAnnotation_RaisesPropertyChanged()
    {
        var annotation = new ViewportAnnotation();
        var changed = new List<string?>();
        annotation.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        var mesh = CreateUnitTriangleMesh();
        annotation.Mesh = mesh;
        annotation.NodeIndex = 7;
        annotation.X = 1.0;
        annotation.Y = 2.0;
        annotation.Z = 3.0;
        annotation.Text = "hello";
        annotation.Tag = "tag";

        Assert.Equal(
            [
                nameof(ViewportAnnotation.Mesh),
                nameof(ViewportAnnotation.NodeIndex),
                nameof(ViewportAnnotation.X),
                nameof(ViewportAnnotation.Y),
                nameof(ViewportAnnotation.Z),
                nameof(ViewportAnnotation.Text),
                nameof(ViewportAnnotation.Tag),
            ],
            changed);

        // 同値の再代入では発火しない
        changed.Clear();
        annotation.NodeIndex = 7;
        annotation.Text = "hello";
        Assert.Empty(changed);
    }

    [Fact]
    public void ViewportAnnotation_Defaults_AreFreePointAnchor()
    {
        var annotation = new ViewportAnnotation();
        Assert.Null(annotation.Mesh);
        Assert.Equal(-1, annotation.NodeIndex);
        Assert.Equal("", annotation.Text);
    }
}
