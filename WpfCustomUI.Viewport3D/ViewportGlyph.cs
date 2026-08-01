using System.Numerics;
using WpfCustomUI.Controls;

namespace WpfCustomUI.Viewport3D;

/// <summary>
/// ベクトルグリフの純粋関数群(spec 6.21)。GPU 非依存で単体テスト可能。
/// 矢印プロトタイプ(単位長)の生成、節点ベクトル場→インスタンスデータの構築、
/// 回転基底(HLSL と同式)を提供する。
/// </summary>
internal static class ViewportGlyph
{
    /// <summary>
    /// インスタンス 1 件分の float 数: 基点(3) + 変位(3) + 単位方向(3) + |v|(1) + 色 RGBA(4)。
    /// 矢印の実長は |v| × GlyphScale(cbuffer)としてシェーダ側で掛けるため、
    /// スケール変更ではインスタンスバッファを再構築しない。
    /// </summary>
    public const int FloatsPerInstance = 14;

    // 単位矢印(+Z 方向、全長 1)のプロポーション
    private const float ShaftRadius = 0.045f;
    private const float HeadRadius = 0.13f;
    private const float HeadStart = 0.68f;

    /// <summary>
    /// 単位矢印(+Z 方向、全長 1)の低ポリジオメトリを作る。
    /// 戻り値は (頂点: position3+normal3 のインターリーブ, 三角形インデックス)。
    /// 構成: シャフト円柱側面 + 底面キャップ + コーン基部ディスク + コーン側面。
    /// </summary>
    public static (float[] Vertices, int[] Indices) BuildArrowGeometry(int segments = 12)
    {
        segments = Math.Max(segments, 3);

        var vertices = new List<float>(segments * 40);
        var indices = new List<int>(segments * 15);

        void AddVertex(float px, float py, float pz, float nx, float ny, float nz)
        {
            vertices.Add(px);
            vertices.Add(py);
            vertices.Add(pz);
            vertices.Add(nx);
            vertices.Add(ny);
            vertices.Add(nz);
        }

        var cos = new float[segments];
        var sin = new float[segments];
        for (var k = 0; k < segments; k++)
        {
            var theta = 2.0 * Math.PI * k / segments;
            cos[k] = (float)Math.Cos(theta);
            sin[k] = (float)Math.Sin(theta);
        }

        // 1. シャフト側面(z=0 → HeadStart、法線=半径方向)
        var shaftBase = 0;
        for (var k = 0; k < segments; k++)
        {
            AddVertex(ShaftRadius * cos[k], ShaftRadius * sin[k], 0.0f, cos[k], sin[k], 0.0f);
            AddVertex(ShaftRadius * cos[k], ShaftRadius * sin[k], HeadStart, cos[k], sin[k], 0.0f);
        }

        for (var k = 0; k < segments; k++)
        {
            var next = (k + 1) % segments;
            var b0 = shaftBase + k * 2;
            var t0 = b0 + 1;
            var b1 = shaftBase + next * 2;
            var t1 = b1 + 1;
            indices.AddRange([b0, b1, t0, t0, b1, t1]);
        }

        // 2. 底面キャップ(z=0、法線 -Z)
        var capCenter = vertices.Count / 6;
        AddVertex(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, -1.0f);
        var capRing = vertices.Count / 6;
        for (var k = 0; k < segments; k++)
        {
            AddVertex(ShaftRadius * cos[k], ShaftRadius * sin[k], 0.0f, 0.0f, 0.0f, -1.0f);
        }

        for (var k = 0; k < segments; k++)
        {
            indices.AddRange([capCenter, capRing + k, capRing + (k + 1) % segments]);
        }

        // 3. コーン基部ディスク(z=HeadStart、法線 -Z)
        var discCenter = vertices.Count / 6;
        AddVertex(0.0f, 0.0f, HeadStart, 0.0f, 0.0f, -1.0f);
        var discRing = vertices.Count / 6;
        for (var k = 0; k < segments; k++)
        {
            AddVertex(HeadRadius * cos[k], HeadRadius * sin[k], HeadStart, 0.0f, 0.0f, -1.0f);
        }

        for (var k = 0; k < segments; k++)
        {
            indices.AddRange([discCenter, discRing + k, discRing + (k + 1) % segments]);
        }

        // 4. コーン側面(z=HeadStart, r=HeadRadius → 先端 z=1)。
        //    斜面法線 = normalize((L·cosθ, L·sinθ, r))、L=コーン軸長
        var coneLength = 1.0f - HeadStart;
        var coneRing = vertices.Count / 6;
        for (var k = 0; k < segments; k++)
        {
            var n = Vector3.Normalize(new Vector3(coneLength * cos[k], coneLength * sin[k], HeadRadius));
            AddVertex(HeadRadius * cos[k], HeadRadius * sin[k], HeadStart, n.X, n.Y, n.Z);
        }

        var coneTip = vertices.Count / 6;
        for (var k = 0; k < segments; k++)
        {
            // 先端は面の中央角の法線を持つ頂点をセグメント毎に複製(低ポリでの陰影を整える)
            var midCos = (float)Math.Cos(2.0 * Math.PI * (k + 0.5) / segments);
            var midSin = (float)Math.Sin(2.0 * Math.PI * (k + 0.5) / segments);
            var n = Vector3.Normalize(new Vector3(coneLength * midCos, coneLength * midSin, HeadRadius));
            AddVertex(0.0f, 0.0f, 1.0f, n.X, n.Y, n.Z);
        }

        for (var k = 0; k < segments; k++)
        {
            indices.AddRange([coneRing + k, coneRing + (k + 1) % segments, coneTip + k]);
        }

        return ([.. vertices], [.. indices]);
    }

    /// <summary>
    /// 単位方向ベクトル w から矢印の回転基底 (u, v) を作る(HLSL の VSMain と同式)。
    /// 戻り値は正規直交基底で、プロトタイプの (x, y, z) 成分がそれぞれ (u, v, w) 方向に写る。
    /// </summary>
    public static (Vector3 U, Vector3 V) ComputeBasis(Vector3 w)
    {
        var helper = Math.Abs(w.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(helper, w));
        var v = Vector3.Cross(w, u);
        return (u, v);
    }

    /// <summary>
    /// メッシュの節点ベクトル場からインスタンスデータ(<see cref="FloatsPerInstance"/> float/件)を作る。
    /// <paramref name="stride"/> 節点ごとに 1 本を採用し、ゼロ・非有限ベクトルの節点はスキップする。
    /// 座標はシーン原点で再センタリング、変位はインスタンスに同梱して変形追従はシェーダで行う。
    /// 色は |v| を <paramref name="colorScale"/> で変換(null なら <paramref name="fallbackColor"/>)。
    /// </summary>
    public static float[] BuildInstances(
        ViewportMesh mesh, int stride,
        double originX, double originY, double originZ,
        ColorScale? colorScale, Vector4 fallbackColor, out int instanceCount)
    {
        instanceCount = 0;
        var vectors = mesh.VectorValues;
        var positions = mesh.Positions;
        if (vectors is null || vectors.Length < 3 || positions.Length < 3)
        {
            return [];
        }

        stride = Math.Max(stride, 1);
        var nodeCount = Math.Min(positions.Length, vectors.Length) / 3;
        var data = new List<float>(nodeCount / stride * FloatsPerInstance + FloatsPerInstance);

        for (var node = 0; node < nodeCount; node += stride)
        {
            var vx = vectors[node * 3];
            var vy = vectors[node * 3 + 1];
            var vz = vectors[node * 3 + 2];
            var magnitude = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            if (!double.IsFinite(magnitude) || magnitude <= 0.0)
            {
                continue; // ゼロ・NaN ベクトルは矢印を立てない(縮退回転の回避)
            }

            var color = fallbackColor;
            if (colorScale is not null)
            {
                // 対数・範囲外・離散レベルの扱いはコンター凡例と同じ ColorScale に委ねる
                var sampled = colorScale.GetColor(magnitude);
                color = new Vector4(sampled.R / 255.0f, sampled.G / 255.0f, sampled.B / 255.0f, sampled.A / 255.0f);
            }

            // 基点(再センタリング済みローカル座標)
            data.Add((float)(positions[node * 3] - originX));
            data.Add((float)(positions[node * 3 + 1] - originY));
            data.Add((float)(positions[node * 3 + 2] - originZ));

            // 変位(変形追従用、なければゼロ)
            var displacements = mesh.Displacements;
            if (displacements is not null && node * 3 + 2 < displacements.Length)
            {
                data.Add((float)displacements[node * 3]);
                data.Add((float)displacements[node * 3 + 1]);
                data.Add((float)displacements[node * 3 + 2]);
            }
            else
            {
                data.Add(0.0f);
                data.Add(0.0f);
                data.Add(0.0f);
            }

            // 単位方向 + 大きさ(実長はシェーダで |v| × GlyphScale)
            data.Add((float)(vx / magnitude));
            data.Add((float)(vy / magnitude));
            data.Add((float)(vz / magnitude));
            data.Add((float)magnitude);

            data.Add(color.X);
            data.Add(color.Y);
            data.Add(color.Z);
            data.Add(color.W);
            instanceCount++;
        }

        return [.. data];
    }
}
