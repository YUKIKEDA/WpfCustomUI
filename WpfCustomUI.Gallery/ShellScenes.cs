using System.Windows.Media;
using WpfCustomUI.Viewport3D;

namespace WpfCustomUI.Gallery;

/// <summary>
/// 統合ミニ CAE シェル(spec 6.25)のデモシーン生成。
/// 題材は既存ページの厳密解を流用する:
/// 静解析 = Kirsch の円孔付き平板+円筒ボス(Viewport3DPage と同型)、
/// 過渡応答 = Euler-Bernoulli 片持ち梁の減衰自由振動(ViewportDeformationPage と同型)。
/// </summary>
internal static class ShellScenes
{
    // ---- 静解析: 円孔付き平板(Kirsch) ----
    private const double PlateHalfWidth = 60.0;   // [mm]
    private const double PlateHalfHeight = 40.0;  // [mm]
    private const double HoleRadius = 10.0;       // [mm]
    internal const double NominalStress = 100.0;  // [MPa]

    // ---- 過渡応答: 片持ち梁 ----
    private const double BeamLength = 100.0;  // [mm]
    private const double BeamWidth = 40.0;    // [mm]
    internal const double TipAmplitude = 1.0; // [mm]
    private const int DivisionsX = 50;
    private const int DivisionsY = 20;
    internal const int TransientFrameCount = 90;
    internal const double TransientDuration = 3.0; // [s]

    /// <summary>片持ち梁の曲げ固有値 βL(cosh·cos = -1 の根)。</summary>
    private static readonly double[] BetaL = [1.8751040687, 4.6940911330, 7.8547574382];

    internal sealed record StaticScene(ViewportMesh Plate, ViewportMesh Boss);

    internal sealed record TransientScene(ViewportMesh Plate, double[][] Frames);

    // ================= 静解析シーン =================

    internal static StaticScene CreateStaticScene() => new(CreatePlateWithHole(), CreateBoss());

    /// <summary>
    /// 円孔付き平板(構造格子)。孔から矩形境界へ放射状にブレンドしたリング格子を
    /// 三角形分割し、Kirsch の厳密解で von Mises 応力を与える。
    /// </summary>
    private static ViewportMesh CreatePlateWithHole()
    {
        const int radialDivisions = 24;   // 孔→外周
        const int angularDivisions = 96;  // 周方向(閉じる)

        var vertexCount = (radialDivisions + 1) * angularDivisions;
        var positions = new double[vertexCount * 3];
        var scalars = new double[vertexCount];

        for (var a = 0; a < angularDivisions; a++)
        {
            var theta = 2.0 * Math.PI * a / angularDivisions;
            var (cos, sin) = (Math.Cos(theta), Math.Sin(theta));

            var kx = Math.Abs(cos) < 1e-12 ? double.MaxValue : PlateHalfWidth / Math.Abs(cos);
            var ky = Math.Abs(sin) < 1e-12 ? double.MaxValue : PlateHalfHeight / Math.Abs(sin);
            var boundaryRadius = Math.Min(kx, ky);

            for (var r = 0; r <= radialDivisions; r++)
            {
                // 孔付近の応力勾配が急なため、二乗分布で孔側を細かく刻む
                var t = (double)r / radialDivisions;
                var radius = HoleRadius + (boundaryRadius - HoleRadius) * t * t;

                var index = (a * (radialDivisions + 1) + r) * 3;
                positions[index] = radius * cos;
                positions[index + 1] = radius * sin;
                positions[index + 2] = 0.0;

                scalars[a * (radialDivisions + 1) + r] = KirschVonMises(radius, theta);
            }
        }

        var triangles = new List<int>(radialDivisions * angularDivisions * 6);
        for (var a = 0; a < angularDivisions; a++)
        {
            var nextA = (a + 1) % angularDivisions;
            for (var r = 0; r < radialDivisions; r++)
            {
                var i00 = a * (radialDivisions + 1) + r;
                var i01 = a * (radialDivisions + 1) + r + 1;
                var i10 = nextA * (radialDivisions + 1) + r;
                var i11 = nextA * (radialDivisions + 1) + r + 1;

                triangles.Add(i00);
                triangles.Add(i10);
                triangles.Add(i11);
                triangles.Add(i00);
                triangles.Add(i11);
                triangles.Add(i01);
            }
        }

        return new ViewportMesh
        {
            Name = "円孔付き平板",
            Positions = positions,
            TriangleIndices = [.. triangles],
            ScalarValues = scalars,
        };
    }

    /// <summary>Kirsch の厳密解(x 方向一軸引張・無限板の円孔まわり)による平面応力 von Mises。</summary>
    private static double KirschVonMises(double r, double theta)
    {
        var s = NominalStress;
        var a2 = HoleRadius * HoleRadius / (r * r);
        var a4 = a2 * a2;
        var cos2T = Math.Cos(2.0 * theta);
        var sin2T = Math.Sin(2.0 * theta);

        var sigmaR = s / 2.0 * (1.0 - a2) + s / 2.0 * (1.0 - 4.0 * a2 + 3.0 * a4) * cos2T;
        var sigmaT = s / 2.0 * (1.0 + a2) - s / 2.0 * (1.0 + 3.0 * a4) * cos2T;
        var tauRT = -s / 2.0 * (1.0 + 2.0 * a2 - 3.0 * a4) * sin2T;

        return Math.Sqrt(sigmaR * sigmaR + sigmaT * sigmaT - sigmaR * sigmaT + 3.0 * tauRT * tauRT);
    }

    /// <summary>孔に通した円筒ボス(スカラーなしの単色パーツ)。</summary>
    private static ViewportMesh CreateBoss()
    {
        const int segments = 48;
        const double radius = HoleRadius * 0.72;
        const double halfLength = 18.0;

        var vertexCount = segments * 2 + 2;
        var positions = new double[vertexCount * 3];
        for (var i = 0; i < segments; i++)
        {
            var theta = 2.0 * Math.PI * i / segments;
            var (x, y) = (radius * Math.Cos(theta), radius * Math.Sin(theta));
            positions[i * 3] = x;
            positions[i * 3 + 1] = y;
            positions[i * 3 + 2] = halfLength;
            positions[(segments + i) * 3] = x;
            positions[(segments + i) * 3 + 1] = y;
            positions[(segments + i) * 3 + 2] = -halfLength;
        }

        var topCenter = segments * 2;
        var bottomCenter = segments * 2 + 1;
        positions[topCenter * 3 + 2] = halfLength;
        positions[bottomCenter * 3 + 2] = -halfLength;

        var triangles = new List<int>(segments * 12);
        for (var i = 0; i < segments; i++)
        {
            var next = (i + 1) % segments;

            triangles.Add(i);
            triangles.Add(segments + i);
            triangles.Add(segments + next);
            triangles.Add(i);
            triangles.Add(segments + next);
            triangles.Add(next);

            triangles.Add(topCenter);
            triangles.Add(i);
            triangles.Add(next);
            triangles.Add(bottomCenter);
            triangles.Add(segments + next);
            triangles.Add(segments + i);
        }

        return new ViewportMesh
        {
            Name = "円筒ボス",
            Positions = positions,
            TriangleIndices = [.. triangles],
            Color = Color.FromRgb(0x8A, 0x9B, 0xB0),
            ShowEdges = false,
        };
    }

    // ================= 過渡応答シーン =================

    internal static TransientScene CreateTransientScene()
    {
        var (mesh, nodeX) = CreateCantileverPlate();
        var modeShapes = BetaL.Select(bl => ComputeModeShape(bl, nodeX)).ToArray();
        var frames = ComputeTransientFrames(nodeX, modeShapes);

        // 初期表示は 1 次モードの静的たわみ(コンターは |w|)
        var shape = modeShapes[0];
        var displacements = new double[shape.Length * 3];
        var scalars = new double[shape.Length];
        for (var node = 0; node < shape.Length; node++)
        {
            var w = TipAmplitude * shape[node];
            displacements[node * 3 + 2] = w;
            scalars[node] = Math.Abs(w);
        }

        mesh.ScalarValues = scalars;
        mesh.Displacements = displacements;
        return new TransientScene(mesh, frames);
    }

    private static (ViewportMesh Mesh, double[] NodeX) CreateCantileverPlate()
    {
        var vertexCount = (DivisionsX + 1) * (DivisionsY + 1);
        var positions = new double[vertexCount * 3];
        var nodeX = new double[vertexCount];

        for (var i = 0; i <= DivisionsX; i++)
        {
            var x = BeamLength * i / DivisionsX;
            for (var j = 0; j <= DivisionsY; j++)
            {
                var node = i * (DivisionsY + 1) + j;
                positions[node * 3] = x;
                positions[node * 3 + 1] = -BeamWidth / 2.0 + BeamWidth * j / DivisionsY;
                positions[node * 3 + 2] = 0.0;
                nodeX[node] = x;
            }
        }

        var triangles = new List<int>(DivisionsX * DivisionsY * 6);
        for (var i = 0; i < DivisionsX; i++)
        {
            for (var j = 0; j < DivisionsY; j++)
            {
                var i00 = i * (DivisionsY + 1) + j;
                var i01 = i00 + 1;
                var i10 = (i + 1) * (DivisionsY + 1) + j;
                var i11 = i10 + 1;

                triangles.Add(i00);
                triangles.Add(i10);
                triangles.Add(i11);
                triangles.Add(i00);
                triangles.Add(i11);
                triangles.Add(i01);
            }
        }

        var mesh = new ViewportMesh
        {
            Name = "片持ち板",
            Positions = positions,
            TriangleIndices = [.. triangles],
        };
        return (mesh, nodeX);
    }

    /// <summary>Euler-Bernoulli 片持ち梁のモード形状(先端たわみ = 1 に正規化)。</summary>
    private static double[] ComputeModeShape(double betaL, double[] nodeX)
    {
        var beta = betaL / BeamLength;
        var sigma = (Math.Sinh(betaL) - Math.Sin(betaL)) / (Math.Cosh(betaL) + Math.Cos(betaL));

        double Phi(double x) =>
            Math.Cosh(beta * x) - Math.Cos(beta * x)
            - sigma * (Math.Sinh(beta * x) - Math.Sin(beta * x));

        var tip = Phi(BeamLength);
        return [.. nodeX.Select(x => Phi(x) / tip)];
    }

    /// <summary>
    /// 過渡応答フレーム列: 先端初期変位からの減衰自由振動。
    /// w(x,t) = Σ aᵢ φᵢ(x) cos(ωᵢt) e^(−ζωᵢt)、ωᵢ ∝ (βᵢL)²(1 次を 1 Hz に正規化)。
    /// </summary>
    private static double[][] ComputeTransientFrames(double[] nodeX, double[][] modeShapes)
    {
        double[] amplitudes = [0.7, 0.22, 0.08];
        const double zeta = 0.04;
        var omega1 = 2.0 * Math.PI;

        var frames = new double[TransientFrameCount][];
        for (var f = 0; f < TransientFrameCount; f++)
        {
            var t = TransientDuration * f / (TransientFrameCount - 1);
            var displacements = new double[nodeX.Length * 3];

            for (var mode = 0; mode < BetaL.Length; mode++)
            {
                var omega = omega1 * (BetaL[mode] * BetaL[mode]) / (BetaL[0] * BetaL[0]);
                var factor = amplitudes[mode] * TipAmplitude
                    * Math.Cos(omega * t) * Math.Exp(-zeta * omega * t);
                var shape = modeShapes[mode];
                for (var node = 0; node < nodeX.Length; node++)
                {
                    displacements[node * 3 + 2] += factor * shape[node];
                }
            }

            frames[f] = displacements;
        }

        return frames;
    }
}
