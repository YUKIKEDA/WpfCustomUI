using CaeStudio.Domain.Models;

namespace CaeStudio.Domain.Meshing;

/// <summary>
/// 形状テンプレート→2D 三角形メッシュの生成。境界条件の適用先となる
/// 節点グループ(幾何条件で定義)も同時に解決する。
/// </summary>
public static class MeshGenerator
{
    /// <summary>形状定義からメッシュを生成する。</summary>
    public static Mesh2D Generate(GeometryDefinition geometry) => geometry switch
    {
        PlateWithHoleGeometry plate => GeneratePlateWithHole(plate),
        CantileverPlateGeometry beam => GenerateCantileverPlate(beam),
        _ => throw new NotSupportedException($"未対応の形状: {geometry.GetType().Name}"),
    };

    // ================= 円孔付き平板 =================

    /// <summary>
    /// 孔から矩形境界へ放射状にブレンドしたリング構造格子。
    /// 孔付近の応力勾配が急なため半径方向は二乗分布で孔側を細かく刻む。
    /// </summary>
    private static Mesh2D GeneratePlateWithHole(PlateWithHoleGeometry plate)
    {
        var halfWidth = plate.Width / 2.0;
        var halfHeight = plate.Height / 2.0;
        var holeRadius = plate.HoleDiameter / 2.0;
        if (holeRadius <= 0 || holeRadius >= Math.Min(halfWidth, halfHeight))
        {
            throw new ArgumentException("孔直径は 0 より大きく、板の短辺より小さい必要があります。");
        }

        var radial = Math.Max(4, plate.RadialDivisions);
        // 対称軸(θ = 0, π/2, π, 3π/2)上に節点を確保するため 4 の倍数に丸める
        var angular = Math.Max(8, plate.AngularDivisions / 4 * 4);

        var thetas = BuildAngularStations(angular, halfWidth, halfHeight);

        var nodeCount = (radial + 1) * angular;
        var positions = new double[nodeCount * 2];

        for (var a = 0; a < angular; a++)
        {
            var theta = thetas[a];
            var (cos, sin) = (Math.Cos(theta), Math.Sin(theta));

            var kx = Math.Abs(cos) < 1e-12 ? double.MaxValue : halfWidth / Math.Abs(cos);
            var ky = Math.Abs(sin) < 1e-12 ? double.MaxValue : halfHeight / Math.Abs(sin);
            var boundaryRadius = Math.Min(kx, ky);

            for (var r = 0; r <= radial; r++)
            {
                var t = (double)r / radial;
                var radius = holeRadius + (boundaryRadius - holeRadius) * t * t;

                var node = a * (radial + 1) + r;
                positions[node * 2] = radius * cos;
                positions[node * 2 + 1] = radius * sin;
            }
        }

        var triangles = new int[radial * angular * 6];
        var write = 0;
        for (var a = 0; a < angular; a++)
        {
            var nextA = (a + 1) % angular;
            for (var r = 0; r < radial; r++)
            {
                var i00 = a * (radial + 1) + r;
                var i01 = i00 + 1;
                var i10 = nextA * (radial + 1) + r;
                var i11 = i10 + 1;

                triangles[write++] = i00;
                triangles[write++] = i10;
                triangles[write++] = i11;
                triangles[write++] = i00;
                triangles[write++] = i11;
                triangles[write++] = i01;
            }
        }

        FixOrientation(positions, triangles);

        // ---- 節点グループの解決(幾何条件) ----
        var tolerance = 1e-6 * Math.Max(plate.Width, plate.Height);

        // 左右辺: 最外周リング(r = radial)のうち x = ±W/2 に載る節点を y 昇順のポリラインに
        var outerRing = Enumerable.Range(0, angular).Select(a => a * (radial + 1) + radial);
        int[] EdgeByX(double x) =>
            [.. outerRing
                .Where(n => Math.Abs(positions[n * 2] - x) < tolerance)
                .OrderBy(n => positions[n * 2 + 1])];

        // 対称軸: θ = 0, π 上(y=0)と θ = π/2, 3π/2 上(x=0)の全節点(拘束専用)
        int[] RayNodes(params int[] angularIndices) =>
            [.. angularIndices.SelectMany(a => Enumerable.Range(0, radial + 1).Select(r => a * (radial + 1) + r))];

        var holeEdge = Enumerable.Range(0, angular).Select(a => a * (radial + 1)).ToArray();

        var groups = new Dictionary<string, NodeGroup>
        {
            [ProjectTemplates.Groups.RightEdge] = new() { Nodes = EdgeByX(halfWidth), IsPolyline = true },
            [ProjectTemplates.Groups.LeftEdge] = new() { Nodes = EdgeByX(-halfWidth), IsPolyline = true },
            [ProjectTemplates.Groups.HoleEdge] = new() { Nodes = holeEdge },
            [ProjectTemplates.Groups.XAxis] = new() { Nodes = RayNodes(0, angular / 2) },
            [ProjectTemplates.Groups.YAxis] = new() { Nodes = RayNodes(angular / 4, 3 * angular / 4) },
        };

        return new Mesh2D { Positions = positions, Triangles = triangles, Groups = groups };
    }

    /// <summary>
    /// 周方向の角度ステーション。均等割りを基本に、矩形コーナー方向の 4 角度へ
    /// 最寄りステーションをスナップする(コーナーに正確に節点を置かないと外周が
    /// 面取りされ、荷重辺の被覆が欠けて遠方場応力が狂うため)。
    /// 対称軸上(θ = 0, π/2, π, 3π/2)のステーションは動かさない。
    /// </summary>
    private static double[] BuildAngularStations(int angular, double halfWidth, double halfHeight)
    {
        var thetas = new double[angular];
        for (var a = 0; a < angular; a++)
        {
            thetas[a] = 2.0 * Math.PI * a / angular;
        }

        var cornerAngle = Math.Atan2(halfHeight, halfWidth);
        var quarter = angular / 4;
        foreach (var corner in (double[])
            [cornerAngle, Math.PI - cornerAngle, Math.PI + cornerAngle, 2.0 * Math.PI - cornerAngle])
        {
            var nearest = (int)Math.Round(corner / (2.0 * Math.PI) * angular) % angular;
            if (nearest % quarter != 0)
            {
                thetas[nearest] = corner;
            }
            else if ((nearest + 1) % quarter != 0)
            {
                thetas[nearest + 1] = corner;
            }
        }

        return thetas;
    }

    // ================= 片持ち板 =================

    private static Mesh2D GenerateCantileverPlate(CantileverPlateGeometry beam)
    {
        var nx = Math.Max(2, beam.DivisionsX);
        var ny = Math.Max(2, beam.DivisionsY);

        var nodeCount = (nx + 1) * (ny + 1);
        var positions = new double[nodeCount * 2];
        for (var i = 0; i <= nx; i++)
        {
            var x = beam.Length * i / nx;
            for (var j = 0; j <= ny; j++)
            {
                var node = i * (ny + 1) + j;
                positions[node * 2] = x;
                positions[node * 2 + 1] = -beam.Height / 2.0 + beam.Height * j / ny;
            }
        }

        var triangles = new int[nx * ny * 6];
        var write = 0;
        for (var i = 0; i < nx; i++)
        {
            for (var j = 0; j < ny; j++)
            {
                var i00 = i * (ny + 1) + j;
                var i01 = i00 + 1;
                var i10 = (i + 1) * (ny + 1) + j;
                var i11 = i10 + 1;

                triangles[write++] = i00;
                triangles[write++] = i10;
                triangles[write++] = i11;
                triangles[write++] = i00;
                triangles[write++] = i11;
                triangles[write++] = i01;
            }
        }

        FixOrientation(positions, triangles);

        int[] Column(int i) => [.. Enumerable.Range(0, ny + 1).Select(j => i * (ny + 1) + j)];

        var groups = new Dictionary<string, NodeGroup>
        {
            [ProjectTemplates.Groups.FixedEdge] = new() { Nodes = Column(0), IsPolyline = true },
            [ProjectTemplates.Groups.TipEdge] = new() { Nodes = Column(nx), IsPolyline = true },
        };

        return new Mesh2D { Positions = positions, Triangles = triangles, Groups = groups };
    }

    /// <summary>全三角形を反時計回り(符号付き面積 > 0)に揃える。</summary>
    private static void FixOrientation(double[] positions, int[] triangles)
    {
        for (var t = 0; t < triangles.Length; t += 3)
        {
            var (n0, n1, n2) = (triangles[t], triangles[t + 1], triangles[t + 2]);
            var signedArea =
                (positions[n1 * 2] - positions[n0 * 2]) * (positions[n2 * 2 + 1] - positions[n0 * 2 + 1]) -
                (positions[n1 * 2 + 1] - positions[n0 * 2 + 1]) * (positions[n2 * 2] - positions[n0 * 2]);
            if (signedArea < 0)
            {
                (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
            }
        }
    }
}
