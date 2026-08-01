using System.Numerics;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace WpfCustomUI.Viewport3D.Rendering;

/// <summary>
/// チャンク 1 個分の GPU リソース(spec 6.22.2 / 6.23)。
/// 頂点レイアウトは position(12B) + octahedral 法線(4B) + scalar(4B) = 20B インターリーブ(slot 0)、
/// 変位は slot 1 の独立した Dynamic バッファ(12B/頂点)。変位を持たないメッシュでは
/// 変位バッファを作らず、レンダラー共有のゼロバッファ(ストライド 0)を束縛して
/// GPU メモリを節約する(spec 6.23.2)。
/// インデックスは**チャンクローカル**で、<see cref="TriangleBase"/> がピック ID の
/// グローバル基点、<see cref="GlobalVertices"/> が変位ギャザー用のローカル→グローバル表。
/// <see cref="BoundsMin"/>/<see cref="BoundsMax"/> はフラスタムカリング用の AABB(spec 6.23.4)。
/// </summary>
internal sealed unsafe class GpuMeshChunk : IDisposable
{
    private ComPtr<ID3D11Buffer> _vertexBuffer;
    private ComPtr<ID3D11Buffer> _displacementBuffer;
    private ComPtr<ID3D11Buffer> _triangleIndexBuffer;
    private ComPtr<ID3D11Buffer> _edgeIndexBuffer;

    internal GpuMeshChunk(int[] globalVertices, uint triangleBase)
    {
        GlobalVertices = globalVertices;
        TriangleBase = triangleBase;
    }

    /// <summary>ローカル頂点→グローバル節点インデックス(変位バッファの部分更新に使用)。</summary>
    public int[] GlobalVertices { get; }

    /// <summary>チャンク先頭のグローバル三角形インデックス(GPU ID ピックのオフセット)。</summary>
    public uint TriangleBase { get; }

    /// <summary>非変形形状の AABB 最小(ローカル座標、フラスタムカリング用)。</summary>
    public Vector3 BoundsMin { get; internal set; }

    /// <summary>非変形形状の AABB 最大(ローカル座標、フラスタムカリング用)。</summary>
    public Vector3 BoundsMax { get; internal set; }

    public int VertexCount => GlobalVertices.Length;

    public uint TriangleIndexCount { get; internal set; }

    public uint EdgeIndexCount { get; internal set; }

    public ID3D11Buffer* VertexBufferHandle => _vertexBuffer.Handle;

    /// <summary>変位バッファ(null なら変位なし → レンダラーがゼロバッファを束縛する)。</summary>
    public ID3D11Buffer* DisplacementBufferHandle => _displacementBuffer.Handle;

    public ID3D11Buffer* TriangleIndexBufferHandle => _triangleIndexBuffer.Handle;

    public ID3D11Buffer* EdgeIndexBufferHandle => _edgeIndexBuffer.Handle;

    internal void SetVertexBuffer(ComPtr<ID3D11Buffer> buffer) => _vertexBuffer = buffer;

    internal void SetDisplacementBuffer(ComPtr<ID3D11Buffer> buffer) => _displacementBuffer = buffer;

    internal void SetTriangleIndexBuffer(ComPtr<ID3D11Buffer> buffer) => _triangleIndexBuffer = buffer;

    internal void SetEdgeIndexBuffer(ComPtr<ID3D11Buffer> buffer) => _edgeIndexBuffer = buffer;

    public void Dispose()
    {
        _vertexBuffer.Dispose();
        _displacementBuffer.Dispose();
        _triangleIndexBuffer.Dispose();
        _edgeIndexBuffer.Dispose();
        _vertexBuffer = default;
        _displacementBuffer = default;
        _triangleIndexBuffer = default;
        _edgeIndexBuffer = default;
    }
}

/// <summary>
/// <see cref="ViewportMesh"/> 1 パーツ分の GPU リソース。
/// <para>
/// 大規模メッシュ対応(spec 6.22 / 6.23)のため、頂点/インデックスバッファは
/// <see cref="ViewportChunking"/> の境界でチャンク分割され、描画・ピックは
/// チャンク毎のドローになる。構築はチャンク逐次×チャンク内並列で、
/// フルサイズの中間配列は法線(float→octahedral uint に即圧縮)のみ。
/// 三角形数が LOD 閾値を超えるメッシュは、操作中描画用の LOD チャンク
/// (グリッドクラスタリング、spec 6.23.3)も併せて構築する。
/// </para>
/// </summary>
internal sealed unsafe class GpuMesh : IDisposable
{
    /// <summary>頂点ストライド: position float3(12B) + octahedral 法線 uint(4B) + scalar float(4B)。</summary>
    public const uint VertexStride = 20;

    public const uint DisplacementStride = 12;

    /// <summary>グリフインスタンスのストライド(<see cref="ViewportGlyph.FloatsPerInstance"/> × 4 バイト)。</summary>
    public const uint GlyphInstanceStride = ViewportGlyph.FloatsPerInstance * sizeof(float);

    /// <summary>グリフインスタンスバッファ 1 本あたりの最大インスタンス数(56B × 200万 = 112MB)。</summary>
    public const int MaxGlyphInstancesPerBuffer = 2_000_000;

    /// <summary>チャンク内の並列ギャザーを使う最小頂点数。</summary>
    private const int ParallelThreshold = 65536;

    private readonly List<GpuMeshChunk> _chunks = [];
    private readonly List<GpuMeshChunk> _lodChunks = [];
    private readonly List<(ComPtr<ID3D11Buffer> Buffer, uint InstanceCount, int CapacityFloats)> _glyphBuffers = [];

    /// <summary>LOD 頂点 → 元節点(変位バッファ更新時のギャザーに使用)。</summary>
    private int[]? _lodRepresentativeNodes;

    private GpuMesh()
    {
    }

    /// <summary>フル解像度の描画チャンク列(spec 6.22.2)。</summary>
    public IReadOnlyList<GpuMeshChunk> Chunks => _chunks;

    /// <summary>操作中 LOD の描画チャンク列(spec 6.23.3、閾値未満のメッシュは空)。</summary>
    public IReadOnlyList<GpuMeshChunk> LodChunks => _lodChunks;

    public bool HasLod => _lodChunks.Count > 0;

    /// <summary>グローバル三角形数(全チャンク合計)。</summary>
    public int TriangleCount { get; private set; }

    /// <summary>LOD メッシュの三角形数(LOD なしは 0)。</summary>
    public int LodTriangleCount { get; private set; }

    /// <summary>ソースメッシュの節点数(チャンク境界の重複は含まない)。</summary>
    public int VertexCount { get; private set; }

    public bool HasScalars { get; private set; }

    /// <summary>EdgeExtractionLimit 超過でエッジ抽出をスキップしたか(spec 6.22.4)。</summary>
    public bool EdgesSkipped { get; private set; }

    /// <summary>最大変位量(フラスタムカリングの AABB 余白、spec 6.23.4)。</summary>
    public float MaxDisplacementMagnitude { get; private set; }

    public Vector4 Color { get; set; }

    public bool ShowEdges { get; set; }

    /// <summary>断面カットの対象か(<see cref="ViewportMesh.IsClippable"/> を毎フレーム同期)。</summary>
    public bool IsClippable { get; set; } = true;

    public bool IsTransparent => Color.W < 0.999f;

    /// <summary>グリフのインスタンス総数(0 ならこのパーツはグリフなし)。</summary>
    public uint GlyphInstanceCount { get; private set; }

    /// <summary>グリフインスタンスバッファ列(大規模ベクトル場では複数本に分割)。</summary>
    public IReadOnlyList<(ComPtr<ID3D11Buffer> Buffer, uint InstanceCount, int CapacityFloats)> GlyphBuffers => _glyphBuffers;

    /// <summary>
    /// メッシュモデルから GPU リソースを構築する。座標は origin(シーン中心)で再センタリング済みの
    /// float に変換され、法線はチャンク分割前にメッシュ全体で計算する(境界の陰影継ぎ目なし)。
    /// </summary>
    /// <param name="edgeExtractionLimit">三角形数がこれを超えるメッシュはエッジ抽出をスキップ(spec 6.22.4)。</param>
    /// <param name="interactiveLodThreshold">三角形数がこれを超えるメッシュは操作中 LOD を構築(spec 6.23.3)。</param>
    /// <param name="meshBounds">メッシュの境界ボックス(LOD のグリッド定義に使用)。</param>
    public static GpuMesh? Create(
        ComPtr<ID3D11Device> device,
        ViewportMesh mesh,
        double originX, double originY, double originZ,
        int edgeExtractionLimit = int.MaxValue,
        int interactiveLodThreshold = int.MaxValue,
        Bounds3D meshBounds = default,
        int maxVerticesPerChunk = ViewportChunking.DefaultMaxVerticesPerChunk,
        int maxTrianglesPerChunk = ViewportChunking.DefaultMaxTrianglesPerChunk)
    {
        var vertexCount = mesh.VertexCount;
        var triangles = mesh.TriangleIndices;
        if (vertexCount == 0 || triangles.Length < 3)
        {
            return null;
        }

        var scalars = mesh.ScalarValues;
        var displacements = mesh.Displacements;
        var triangleCount = triangles.Length / 3;
        var extractEdges = triangleCount <= edgeExtractionLimit;

        var result = new GpuMesh
        {
            TriangleCount = triangleCount,
            VertexCount = vertexCount,
            HasScalars = scalars is not null,
            EdgesSkipped = !extractEdges,
            MaxDisplacementMagnitude =
                (float)ViewportDeformation.GetMaxDisplacementMagnitude(displacements),
        };

        try
        {
            BuildChunkSet(
                device, result._chunks, mesh.Positions, triangles, scalars, displacements,
                displacementSourceMap: null, originX, originY, originZ, extractEdges,
                maxVerticesPerChunk, maxTrianglesPerChunk);

            // 操作中 LOD(spec 6.23.3)。グリッドクラスタリングで約 1/20 に間引いたメッシュを
            // 同じチャンクパイプラインで構築する(エッジ抽出なし)
            if (triangleCount > interactiveLodThreshold && !meshBounds.IsEmpty)
            {
                var lod = ViewportLod.Build(mesh.Positions, triangles, scalars, meshBounds);
                if (lod is not null)
                {
                    var lodPositions = GatherLodPositions(mesh.Positions, lod.RepresentativeNodes);
                    var lodDisplacements = displacements is null
                        ? null
                        : GatherLodDoubles(displacements, lod.RepresentativeNodes);

                    BuildChunkSet(
                        device, result._lodChunks, lodPositions, lod.TriangleIndices,
                        lod.ScalarValues, lodDisplacements,
                        displacementSourceMap: null, originX, originY, originZ, extractEdges: false,
                        maxVerticesPerChunk, maxTrianglesPerChunk);

                    result._lodRepresentativeNodes = lod.RepresentativeNodes;
                    result.LodTriangleCount = lod.TriangleCount;
                }
            }
        }
        catch
        {
            result.Dispose();
            throw;
        }

        return result;
    }

    /// <summary>
    /// 頂点/インデックス/エッジのチャンク列を構築する(フル解像度と LOD で共用)。
    /// フルサイズ中間は法線のみで、octahedral 圧縮(4B/頂点)後に float 配列を手放す。
    /// 頂点の float 化はチャンク毎のギャザー中にオンザフライで行う(spec 6.23.2)。
    /// </summary>
    private static void BuildChunkSet(
        ComPtr<ID3D11Device> device,
        List<GpuMeshChunk> target,
        double[] positions, int[] triangles, double[]? scalars, double[]? displacements,
        int[]? displacementSourceMap,
        double originX, double originY, double originZ,
        bool extractEdges, int maxVerticesPerChunk, int maxTrianglesPerChunk)
    {
        var vertexCount = positions.Length / 3;

        // 法線: フルサイズ float で累積 → octahedral uint(4B/頂点)へ即圧縮して float を解放
        uint[] octNormals;
        {
            var normals = ViewportGeometry.ComputeVertexNormalsFromDouble(
                positions, originX, originY, originZ, triangles);
            octNormals = ViewportGeometry.CompressNormals(normals);
        }

        var boundaries = ViewportChunking.ComputeChunkBoundaries(
            triangles, vertexCount, maxVerticesPerChunk, maxTrianglesPerChunk);
        var remap = new ChunkVertexRemap(vertexCount);

        // チャンク逐次×チャンク内並列。中間配列(インターリーブ/変位/ローカルインデックス)は
        // チャンク 1 個分しか同時に存在しないため、ピークメモリが全体サイズに比例しない
        foreach (var boundary in boundaries)
        {
            var (localTriangles, globalVertices) = ViewportChunking.BuildChunkData(triangles, boundary, remap);
            var chunk = new GpuMeshChunk(globalVertices, (uint)boundary.TriangleStart)
            {
                TriangleIndexCount = (uint)localTriangles.Length,
            };

            // インターリーブ頂点データ(position + oct 法線 + scalar)をギャザー。
            // 位置の再センタリング+float 化はここでオンザフライ(ToLocalPositions と同式)
            var vertexData = new float[globalVertices.Length * 5];
            ForEachVertex(globalVertices.Length, localIndex =>
            {
                var gv = globalVertices[localIndex];
                var g = gv * 3;
                var dst = localIndex * 5;
                vertexData[dst] = (float)(positions[g] - originX);
                vertexData[dst + 1] = (float)(positions[g + 1] - originY);
                vertexData[dst + 2] = (float)(positions[g + 2] - originZ);
                vertexData[dst + 3] = BitConverter.UInt32BitsToSingle(octNormals[gv]);
                vertexData[dst + 4] = scalars is not null && gv < scalars.Length
                    ? (float)scalars[gv]
                    : 0.0f;
            });

            var (min, max) = ComputeChunkBounds(vertexData);
            chunk.BoundsMin = min;
            chunk.BoundsMax = max;

            fixed (float* pVertices = vertexData)
            {
                chunk.SetVertexBuffer(CreateImmutableBuffer(
                    device, pVertices, (uint)(vertexData.Length * sizeof(float)), BindFlag.VertexBuffer));
            }

            // 変位バッファ(Dynamic)は変位を持つメッシュだけ作る。持たないメッシュは
            // レンダラー共有のゼロバッファ(ストライド 0)で代用し GPU メモリを節約する(spec 6.23.2)
            if (displacements is not null)
            {
                var chunkDisplacements = GatherDisplacements(displacements, globalVertices, displacementSourceMap);
                chunk.SetDisplacementBuffer(CreateDisplacementBuffer(device, chunkDisplacements));
            }

            fixed (int* pIndices = localTriangles)
            {
                chunk.SetTriangleIndexBuffer(CreateImmutableBuffer(
                    device, pIndices, (uint)(localTriangles.Length * sizeof(int)), BindFlag.IndexBuffer));
            }

            // エッジ(ローカルインデックス)。チャンク境界を跨ぐエッジは両チャンクに
            // 現れて二重描画になるが、ライン重畳なので見た目は変わらない
            if (extractEdges)
            {
                var edges = ViewportGeometry.ExtractEdges(localTriangles);
                if (edges.Length > 0)
                {
                    chunk.EdgeIndexCount = (uint)edges.Length;
                    fixed (int* pEdges = edges)
                    {
                        chunk.SetEdgeIndexBuffer(CreateImmutableBuffer(
                            device, pEdges, (uint)(edges.Length * sizeof(int)), BindFlag.IndexBuffer));
                    }
                }
            }

            target.Add(chunk);
        }
    }

    /// <summary>
    /// 変位バッファだけを差し替える(チャンク毎にギャザーして Map/WriteDiscard)。
    /// ジオメトリ本体は再構築しないため、過渡応答のフレーム再生で毎フレーム呼んでも軽い。
    /// 初めて変位が設定されたメッシュではバッファを遅延作成する。LOD チャンクには
    /// 代表節点マップ経由でギャザーした変位を書く(spec 6.23.3)。
    /// </summary>
    public void UpdateDisplacements(
        ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, double[]? displacements)
    {
        MaxDisplacementMagnitude = (float)ViewportDeformation.GetMaxDisplacementMagnitude(displacements);
        UpdateChunkDisplacements(device, context, _chunks, displacements, sourceMap: null);
        UpdateChunkDisplacements(device, context, _lodChunks, displacements, _lodRepresentativeNodes);
    }

    private static void UpdateChunkDisplacements(
        ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context,
        List<GpuMeshChunk> chunks, double[]? displacements, int[]? sourceMap)
    {
        foreach (var chunk in chunks)
        {
            if (chunk.DisplacementBufferHandle is null)
            {
                if (displacements is null)
                {
                    continue; // 変位なしのまま(ゼロバッファ束縛で十分)
                }

                // 変位が初めて設定された: バッファを遅延作成
                var initial = GatherDisplacements(displacements, chunk.GlobalVertices, sourceMap);
                chunk.SetDisplacementBuffer(CreateDisplacementBuffer(device, initial));
                continue;
            }

            var data = GatherDisplacements(displacements, chunk.GlobalVertices, sourceMap);
            MappedSubresource mapped = default;
            var buffer = new ComPtr<ID3D11Buffer>(chunk.DisplacementBufferHandle);
            SilkMarshal.ThrowHResult(context.Map(buffer, 0, Map.WriteDiscard, 0, ref mapped));
            try
            {
                fixed (float* pData = data)
                {
                    System.Buffer.MemoryCopy(pData, mapped.PData, data.Length * sizeof(float), data.Length * sizeof(float));
                }
            }
            finally
            {
                context.Unmap(buffer, 0);
            }
        }
    }

    /// <summary>
    /// グリフのインスタンスバッファを差し替える(spec 6.21 / 6.22)。
    /// 大規模ベクトル場では <see cref="MaxGlyphInstancesPerBuffer"/> 毎に複数バッファへ分割する。
    /// 既存バッファの本数・容量が足りるときは Map/WriteDiscard の部分更新で済ませる。
    /// </summary>
    public void UpdateGlyphInstances(
        ComPtr<ID3D11Device> device, ComPtr<ID3D11DeviceContext> context, float[] data, int instanceCount)
    {
        GlyphInstanceCount = (uint)Math.Max(instanceCount, 0);
        if (instanceCount <= 0 || data.Length == 0)
        {
            return;
        }

        var requiredBuffers = (instanceCount + MaxGlyphInstancesPerBuffer - 1) / MaxGlyphInstancesPerBuffer;
        var reusable = _glyphBuffers.Count == requiredBuffers;
        if (reusable)
        {
            for (var i = 0; i < requiredBuffers; i++)
            {
                var count = Math.Min(instanceCount - i * MaxGlyphInstancesPerBuffer, MaxGlyphInstancesPerBuffer);
                if (_glyphBuffers[i].CapacityFloats < count * ViewportGlyph.FloatsPerInstance)
                {
                    reusable = false;
                    break;
                }
            }
        }

        if (!reusable)
        {
            ReleaseGlyphBuffers();
        }

        for (var i = 0; i < requiredBuffers; i++)
        {
            var count = Math.Min(instanceCount - i * MaxGlyphInstancesPerBuffer, MaxGlyphInstancesPerBuffer);
            var floatOffset = i * MaxGlyphInstancesPerBuffer * ViewportGlyph.FloatsPerInstance;
            var floatCount = count * ViewportGlyph.FloatsPerInstance;

            fixed (float* pData = &data[floatOffset])
            {
                if (reusable)
                {
                    var (buffer, _, capacity) = _glyphBuffers[i];
                    MappedSubresource mapped = default;
                    SilkMarshal.ThrowHResult(context.Map(buffer, 0, Map.WriteDiscard, 0, ref mapped));
                    try
                    {
                        System.Buffer.MemoryCopy(pData, mapped.PData, capacity * sizeof(float), floatCount * sizeof(float));
                    }
                    finally
                    {
                        context.Unmap(buffer, 0);
                    }

                    _glyphBuffers[i] = (buffer, (uint)count, capacity);
                }
                else
                {
                    var desc = new BufferDesc
                    {
                        ByteWidth = (uint)(floatCount * sizeof(float)),
                        Usage = Usage.Dynamic,
                        BindFlags = (uint)BindFlag.VertexBuffer,
                        CPUAccessFlags = (uint)CpuAccessFlag.Write,
                    };
                    var subresource = new SubresourceData { PSysMem = pData };
                    ComPtr<ID3D11Buffer> buffer = default;
                    SilkMarshal.ThrowHResult(device.CreateBuffer(in desc, in subresource, ref buffer));
                    _glyphBuffers.Add((buffer, (uint)count, floatCount));
                }
            }
        }
    }

    /// <summary>
    /// チャンクの変位ギャザー(グローバル配列→ローカル float)。大チャンクは並列。
    /// sourceMap は LOD チャンク用(LOD 頂点→代表節点)。
    /// </summary>
    private static float[] GatherDisplacements(double[]? displacements, int[] globalVertices, int[]? sourceMap)
    {
        var result = new float[globalVertices.Length * 3];
        if (displacements is null)
        {
            return result;
        }

        ForEachVertex(globalVertices.Length, localIndex =>
        {
            var node = globalVertices[localIndex];
            if (sourceMap is not null)
            {
                node = sourceMap[node];
            }

            var g = node * 3;
            if (g + 2 < displacements.Length)
            {
                var dst = localIndex * 3;
                result[dst] = (float)displacements[g];
                result[dst + 1] = (float)displacements[g + 1];
                result[dst + 2] = (float)displacements[g + 2];
            }
        });

        return result;
    }

    /// <summary>LOD 頂点の座標を代表節点からギャザーする。</summary>
    private static double[] GatherLodPositions(double[] positions, int[] representativeNodes)
    {
        var result = new double[representativeNodes.Length * 3];
        ForEachVertex(representativeNodes.Length, i =>
        {
            var g = representativeNodes[i] * 3;
            result[i * 3] = positions[g];
            result[i * 3 + 1] = positions[g + 1];
            result[i * 3 + 2] = positions[g + 2];
        });

        return result;
    }

    /// <summary>LOD 頂点毎の 3 成分値(変位)を代表節点からギャザーする。</summary>
    private static double[] GatherLodDoubles(double[] values, int[] representativeNodes)
    {
        var result = new double[representativeNodes.Length * 3];
        ForEachVertex(representativeNodes.Length, i =>
        {
            var g = representativeNodes[i] * 3;
            if (g + 2 < values.Length)
            {
                result[i * 3] = values[g];
                result[i * 3 + 1] = values[g + 1];
                result[i * 3 + 2] = values[g + 2];
            }
        });

        return result;
    }

    /// <summary>インターリーブ頂点列(5 float/頂点)から AABB を求める。</summary>
    private static (Vector3 Min, Vector3 Max) ComputeChunkBounds(float[] vertexData)
    {
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        for (var i = 0; i + 2 < vertexData.Length; i += 5)
        {
            var p = new Vector3(vertexData[i], vertexData[i + 1], vertexData[i + 2]);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        return min.X <= max.X ? (min, max) : (Vector3.Zero, Vector3.Zero);
    }

    private static ComPtr<ID3D11Buffer> CreateDisplacementBuffer(
        ComPtr<ID3D11Device> device, float[] data)
    {
        fixed (float* pData = data)
        {
            var desc = new BufferDesc
            {
                ByteWidth = (uint)(data.Length * sizeof(float)),
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.VertexBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write,
            };
            var subresource = new SubresourceData { PSysMem = pData };
            ComPtr<ID3D11Buffer> buffer = default;
            SilkMarshal.ThrowHResult(device.CreateBuffer(in desc, in subresource, ref buffer));
            return buffer;
        }
    }

    private static void ForEachVertex(int count, Action<int> body)
    {
        if (count >= ParallelThreshold)
        {
            Parallel.For(0, count, body);
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                body(i);
            }
        }
    }

    private static ComPtr<ID3D11Buffer> CreateImmutableBuffer(
        ComPtr<ID3D11Device> device, void* data, uint byteWidth, BindFlag bindFlag)
    {
        var desc = new BufferDesc
        {
            ByteWidth = byteWidth,
            Usage = Usage.Immutable,
            BindFlags = (uint)bindFlag,
        };
        var subresource = new SubresourceData { PSysMem = data };

        ComPtr<ID3D11Buffer> buffer = default;
        SilkMarshal.ThrowHResult(device.CreateBuffer(in desc, in subresource, ref buffer));
        return buffer;
    }

    private void ReleaseGlyphBuffers()
    {
        foreach (var (buffer, _, _) in _glyphBuffers)
        {
            buffer.Dispose();
        }

        _glyphBuffers.Clear();
    }

    public void Dispose()
    {
        foreach (var chunk in _chunks)
        {
            chunk.Dispose();
        }

        foreach (var chunk in _lodChunks)
        {
            chunk.Dispose();
        }

        _chunks.Clear();
        _lodChunks.Clear();
        _lodRepresentativeNodes = null;
        LodTriangleCount = 0;
        ReleaseGlyphBuffers();
        GlyphInstanceCount = 0;
    }
}
