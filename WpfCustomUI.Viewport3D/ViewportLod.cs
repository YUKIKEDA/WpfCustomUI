namespace WpfCustomUI.Viewport3D;

/// <summary>
/// 操作中 LOD メッシュのデータ(spec 6.23.3)。LOD 頂点は元メッシュの代表節点
/// (セル内の最小インデックス節点)に対応し、位置・変位は代表節点から取る。
/// スカラーはセル内平均(コンターのちらつき低減)。
/// </summary>
internal sealed class LodMeshData
{
    /// <summary>LOD 頂点 → 元メッシュの代表節点インデックス。</summary>
    public required int[] RepresentativeNodes { get; init; }

    /// <summary>LOD 三角形(LOD 頂点参照、退化除去+重複除去済み)。</summary>
    public required int[] TriangleIndices { get; init; }

    /// <summary>LOD 頂点毎のスカラー(セル内平均)。元メッシュがスカラーなしなら null。</summary>
    public required double[]? ScalarValues { get; init; }

    public int VertexCount => RepresentativeNodes.Length;

    public int TriangleCount => TriangleIndices.Length / 3;
}

/// <summary>
/// グリッド頂点クラスタリングによる LOD 構築(spec 6.23.3)。GPU 非依存の純粋関数で単体テスト可能。
/// <para>
/// 一様グリッドのセル内頂点を 1 点(セル内最小インデックスの代表節点)に束ね、
/// 退化した三角形と重複三角形を除去する。ブロック並列+順序保存マージのため、
/// 結果は逐次実行と完全一致し、コア数にも依存しない(ピクセル差分検証の決定性を保つ)。
/// </para>
/// </summary>
internal static class ViewportLod
{
    /// <summary>目標間引き率(頂点数 1/20 → 三角形数もおよそ 1/20)。</summary>
    public const int DefaultReductionFactor = 20;

    /// <summary>グリッド解像度の上限(セルキーを 21bit×3 の long に詰めるため 2^21 未満)。</summary>
    public const int MaxGridResolution = 4096;

    /// <summary>ブロック並列の 1 ブロックあたり頂点/三角形数。</summary>
    private const int BlockSize = 1 << 20;

    /// <summary>
    /// グリッド 1 軸の解像度を求める。表面メッシュでは占有セル数 ∝ 解像度² のため、
    /// 解像度 = √(頂点数/間引き率) で LOD 頂点数がおよそ 頂点数/間引き率 になる。
    /// </summary>
    public static int ComputeGridResolution(int vertexCount, int reductionFactor = DefaultReductionFactor)
    {
        var target = Math.Sqrt(vertexCount / (double)Math.Max(reductionFactor, 1));
        return Math.Clamp((int)Math.Ceiling(target), 2, MaxGridResolution);
    }

    /// <summary>
    /// 座標をグリッドセルキー(21bit×3 パック)へ変換する。
    /// セルは最長軸を resolution 分割した「立方体」(全軸同一の物理サイズ)。
    /// 軸毎に正規化すると薄い方向(シェルの厚み等)が過剰分割されて間引きが効かないため。
    /// </summary>
    public static long ComputeCellKey(
        double x, double y, double z, in Bounds3D bounds, int resolution)
    {
        var maxExtent = Math.Max(
            bounds.MaxX - bounds.MinX,
            Math.Max(bounds.MaxY - bounds.MinY, bounds.MaxZ - bounds.MinZ));
        if (maxExtent <= 0.0)
        {
            return 0;
        }

        var invCellSize = resolution / maxExtent;
        var ix = ToCellIndex(x, bounds.MinX, invCellSize, resolution);
        var iy = ToCellIndex(y, bounds.MinY, invCellSize, resolution);
        var iz = ToCellIndex(z, bounds.MinZ, invCellSize, resolution);
        return ((long)ix << 42) | ((long)iy << 21) | (uint)iz;

        static int ToCellIndex(double v, double min, double invCellSize, int resolution)
        {
            // 境界ちょうど(v = max)は最終セルへ折り込む。範囲外の座標もクランプで安全に収める
            return Math.Clamp((int)((v - min) * invCellSize), 0, resolution - 1);
        }
    }

    /// <summary>
    /// LOD メッシュを構築する。三角形が全て退化する(モデルが 1 セルに収まる)場合など、
    /// 有効な三角形が残らないときは null。
    /// </summary>
    public static LodMeshData? Build(
        double[] positions, int[] triangleIndices, double[]? scalarValues,
        in Bounds3D bounds, int reductionFactor = DefaultReductionFactor)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(triangleIndices);

        var vertexCount = positions.Length / 3;
        if (vertexCount == 0 || triangleIndices.Length < 3 || bounds.IsEmpty)
        {
            return null;
        }

        var resolution = ComputeGridResolution(vertexCount, reductionFactor);

        // --- 頂点クラスタリング ---
        // ブロック毎にローカル辞書で「セル→ローカル連番」を作り(並列)、
        // ブロック順にグローバル連番へマージする(逐次)。マージ順がブロック順+発見順のため、
        // 結果の LOD 頂点番号・代表節点は逐次スキャンと完全一致する
        var vertexBlocks = SplitBlocks(vertexCount);
        var vertexLocal = new int[vertexCount];
        var blockKeys = new List<long>[vertexBlocks.Length];
        var blockFirsts = new List<int>[vertexBlocks.Length];

        var boundsCopy = bounds; // ラムダキャプチャ用(in パラメータは直接キャプチャ不可)
        Parallel.For(0, vertexBlocks.Length, b =>
        {
            var (start, count) = vertexBlocks[b];
            var dict = new Dictionary<long, int>();
            var keys = new List<long>();
            var firsts = new List<int>();
            for (var i = start; i < start + count; i++)
            {
                var key = ComputeCellKey(
                    positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2], boundsCopy, resolution);
                if (!dict.TryGetValue(key, out var local))
                {
                    local = dict.Count;
                    dict[key] = local;
                    keys.Add(key);
                    firsts.Add(i);
                }

                vertexLocal[i] = local;
            }

            blockKeys[b] = keys;
            blockFirsts[b] = firsts;
        });

        var globalCells = new Dictionary<long, int>();
        var representatives = new List<int>();
        var blockRemaps = new int[vertexBlocks.Length][];
        for (var b = 0; b < vertexBlocks.Length; b++)
        {
            var keys = blockKeys[b];
            var firsts = blockFirsts[b];
            var remap = new int[keys.Count];
            for (var k = 0; k < keys.Count; k++)
            {
                if (!globalCells.TryGetValue(keys[k], out var lod))
                {
                    lod = representatives.Count;
                    globalCells[keys[k]] = lod;
                    representatives.Add(firsts[k]);
                }

                remap[k] = lod;
            }

            blockRemaps[b] = remap;
        }

        var lodVertexCount = representatives.Count;

        // 元節点 → LOD 頂点(vertexLocal をローカル→グローバルへ並列上書き)
        Parallel.For(0, vertexBlocks.Length, b =>
        {
            var (start, count) = vertexBlocks[b];
            var remap = blockRemaps[b];
            for (var i = start; i < start + count; i++)
            {
                vertexLocal[i] = remap[vertexLocal[i]];
            }
        });

        // スカラーのセル内平均(順序依存の加算のため逐次 = 決定的)
        double[]? lodScalars = null;
        if (scalarValues is not null)
        {
            var sums = new double[lodVertexCount];
            var counts = new int[lodVertexCount];
            var n = Math.Min(vertexCount, scalarValues.Length);
            for (var i = 0; i < n; i++)
            {
                var lod = vertexLocal[i];
                sums[lod] += scalarValues[i];
                counts[lod]++;
            }

            lodScalars = new double[lodVertexCount];
            for (var i = 0; i < lodVertexCount; i++)
            {
                lodScalars[i] = counts[i] > 0 ? sums[i] / counts[i] : 0.0;
            }
        }

        // --- 三角形の再マップ(退化除去+重複除去) ---
        // ブロック内で重複除去してからブロック順にマージするため、残る三角形と順序は
        // 逐次スキャン(最初の出現を採用)と完全一致する
        var triangleCount = triangleIndices.Length / 3;
        var triangleBlocks = SplitBlocks(triangleCount);
        var blockTriangles = new List<int>[triangleBlocks.Length];
        var blockCanonicals = new List<(int A, int B, int C)>[triangleBlocks.Length];

        Parallel.For(0, triangleBlocks.Length, b =>
        {
            var (start, count) = triangleBlocks[b];
            var seen = new HashSet<(int, int, int)>();
            var tris = new List<int>();
            var canonicals = new List<(int, int, int)>();
            for (var t = start; t < start + count; t++)
            {
                var a = vertexLocal[triangleIndices[t * 3]];
                var bb = vertexLocal[triangleIndices[t * 3 + 1]];
                var c = vertexLocal[triangleIndices[t * 3 + 2]];
                if (a == bb || bb == c || a == c)
                {
                    continue; // クラスタリングで退化した三角形
                }

                // 重複判定は無順序(ソート済みタプル)。最初の出現の巻き方向を採用する
                var canonical = Canonical(a, bb, c);
                if (seen.Add(canonical))
                {
                    tris.Add(a);
                    tris.Add(bb);
                    tris.Add(c);
                    canonicals.Add(canonical);
                }
            }

            blockTriangles[b] = tris;
            blockCanonicals[b] = canonicals;
        });

        var globalSeen = new HashSet<(int, int, int)>();
        var lodTriangles = new List<int>();
        for (var b = 0; b < triangleBlocks.Length; b++)
        {
            var tris = blockTriangles[b];
            var canonicals = blockCanonicals[b];
            for (var k = 0; k < canonicals.Count; k++)
            {
                if (globalSeen.Add(canonicals[k]))
                {
                    lodTriangles.Add(tris[k * 3]);
                    lodTriangles.Add(tris[k * 3 + 1]);
                    lodTriangles.Add(tris[k * 3 + 2]);
                }
            }
        }

        if (lodTriangles.Count < 3)
        {
            return null;
        }

        return new LodMeshData
        {
            RepresentativeNodes = [.. representatives],
            TriangleIndices = [.. lodTriangles],
            ScalarValues = lodScalars,
        };
    }

    private static (int Start, int Count)[] SplitBlocks(int total)
    {
        var blockCount = Math.Max((total + BlockSize - 1) / BlockSize, 1);
        var result = new (int, int)[blockCount];
        for (var b = 0; b < blockCount; b++)
        {
            var start = b * BlockSize;
            result[b] = (start, Math.Min(BlockSize, total - start));
        }

        return result;
    }

    private static (int, int, int) Canonical(int a, int b, int c)
    {
        // 3 値のソート(分岐最小)
        if (a > b)
        {
            (a, b) = (b, a);
        }

        if (b > c)
        {
            (b, c) = (c, b);
        }

        if (a > b)
        {
            (a, b) = (b, a);
        }

        return (a, b, c);
    }
}
