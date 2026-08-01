namespace WpfCustomUI.Viewport3D;

/// <summary>
/// チャンク 1 個分の範囲情報(spec 6.22.2)。三角形は入力順の連続範囲で、
/// VertexCount はその範囲が参照するユニーク節点数(チャンクローカル頂点バッファのサイズ)。
/// </summary>
/// <param name="TriangleStart">チャンク先頭の三角形インデックス(グローバル、三角形単位)。</param>
/// <param name="TriangleCount">チャンクの三角形数。</param>
/// <param name="VertexCount">チャンクが参照するユニーク節点数。</param>
internal readonly record struct ChunkBoundary(int TriangleStart, int TriangleCount, int VertexCount);

/// <summary>
/// グローバル節点インデックス→チャンクローカルインデックスの再マップ表。
/// エポック方式で全要素クリアなしにチャンク毎のリセットができる
/// (節点 2,500万 × チャンク数のクリアを避ける)。
/// </summary>
internal sealed class ChunkVertexRemap(int vertexCount)
{
    private readonly int[] _localIndex = new int[vertexCount];
    private readonly int[] _epoch = new int[vertexCount]; // 0 = 未使用
    private int _currentEpoch;
    private int _count;

    /// <summary>現在のチャンクに割り当て済みのユニーク節点数。</summary>
    public int Count => _count;

    /// <summary>新しいチャンクを開始する(割り当てをすべて無効化)。</summary>
    public void BeginChunk()
    {
        _currentEpoch++;
        _count = 0;
    }

    /// <summary>この節点が現在のチャンクに割り当て済みか。</summary>
    public bool Contains(int globalVertex) => _epoch[globalVertex] == _currentEpoch;

    /// <summary>
    /// 節点のローカルインデックスを返す(未割り当てなら連番で新規割り当て)。
    /// </summary>
    public int GetOrAdd(int globalVertex)
    {
        if (_epoch[globalVertex] == _currentEpoch)
        {
            return _localIndex[globalVertex];
        }

        _epoch[globalVertex] = _currentEpoch;
        _localIndex[globalVertex] = _count;
        return _count++;
    }
}

/// <summary>
/// 大規模メッシュのチャンク分割数学(spec 6.22.2)。GPU 非依存の純粋関数で単体テスト可能。
/// <para>
/// D3D11 は単一リソースのサイズ上限(保証 128MB、実質 1〜2GB)があるため、
/// 数千万三角形級のメッシュは頂点/インデックスバッファを分割して複数ドローする。
/// 三角形を入力順の連続範囲で貪欲に詰め、範囲が参照する節点をチャンクローカル頂点バッファに
/// 集める(境界節点はチャンク間で重複するが、表面メッシュでは重複率は僅少)。
/// </para>
/// </summary>
internal static class ViewportChunking
{
    /// <summary>チャンクあたりの最大節点数(28B/頂点 × 400万 = 112MB)。</summary>
    public const int DefaultMaxVerticesPerChunk = 4_000_000;

    /// <summary>チャンクあたりの最大三角形数(12B/三角形 × 800万 = 96MB)。</summary>
    public const int DefaultMaxTrianglesPerChunk = 8_000_000;

    /// <summary>
    /// 三角形列を先頭から貪欲にスキャンし、節点数・三角形数の上限を超えない
    /// 連続チャンク境界を求める。<see cref="BuildChunkData"/> と同じスキャン順なので
    /// ユニーク節点の数え方は必ず一致する。
    /// </summary>
    public static List<ChunkBoundary> ComputeChunkBoundaries(
        int[] triangleIndices, int vertexCount,
        int maxVerticesPerChunk = DefaultMaxVerticesPerChunk,
        int maxTrianglesPerChunk = DefaultMaxTrianglesPerChunk)
    {
        ArgumentNullException.ThrowIfNull(triangleIndices);
        maxVerticesPerChunk = Math.Max(maxVerticesPerChunk, 3); // 1 三角形は必ず収める
        maxTrianglesPerChunk = Math.Max(maxTrianglesPerChunk, 1);

        var result = new List<ChunkBoundary>();
        var triangleCount = triangleIndices.Length / 3;
        if (triangleCount == 0 || vertexCount == 0)
        {
            return result;
        }

        var remap = new ChunkVertexRemap(vertexCount);
        remap.BeginChunk();
        var chunkStart = 0;
        var chunkTriangles = 0;

        for (var t = 0; t < triangleCount; t++)
        {
            var a = triangleIndices[t * 3];
            var b = triangleIndices[t * 3 + 1];
            var c = triangleIndices[t * 3 + 2];

            // この三角形が持ち込む新規節点数(三角形内の重複も数えない)
            var newVertices = 0;
            if (!remap.Contains(a))
            {
                newVertices++;
            }

            if (b != a && !remap.Contains(b))
            {
                newVertices++;
            }

            if (c != a && c != b && !remap.Contains(c))
            {
                newVertices++;
            }

            if (chunkTriangles > 0
                && (remap.Count + newVertices > maxVerticesPerChunk || chunkTriangles + 1 > maxTrianglesPerChunk))
            {
                result.Add(new ChunkBoundary(chunkStart, chunkTriangles, remap.Count));
                chunkStart = t;
                chunkTriangles = 0;
                remap.BeginChunk();
            }

            remap.GetOrAdd(a);
            remap.GetOrAdd(b);
            remap.GetOrAdd(c);
            chunkTriangles++;
        }

        result.Add(new ChunkBoundary(chunkStart, chunkTriangles, remap.Count));
        return result;
    }

    /// <summary>
    /// チャンク 1 個分のローカルデータを作る。
    /// 戻り値は (ローカル節点インデックスの三角形列, ローカル→グローバル節点マップ)。
    /// グローバルマップは頂点データの収集と変位バッファの部分更新に使う。
    /// remap は再利用のため呼び出し元が保持する(内部で BeginChunk する)。
    /// </summary>
    public static (int[] LocalTriangles, int[] GlobalVertices) BuildChunkData(
        int[] triangleIndices, ChunkBoundary boundary, ChunkVertexRemap remap)
    {
        ArgumentNullException.ThrowIfNull(triangleIndices);
        ArgumentNullException.ThrowIfNull(remap);

        var localTriangles = new int[boundary.TriangleCount * 3];
        var globalVertices = new int[boundary.VertexCount];

        remap.BeginChunk();
        var srcBase = boundary.TriangleStart * 3;
        for (var i = 0; i < localTriangles.Length; i++)
        {
            var global = triangleIndices[srcBase + i];
            var before = remap.Count;
            var local = remap.GetOrAdd(global);
            if (remap.Count > before)
            {
                globalVertices[local] = global;
            }

            localTriangles[i] = local;
        }

        return (localTriangles, globalVertices);
    }
}
