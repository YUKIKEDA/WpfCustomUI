using System.Numerics;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace WpfCustomUI.Viewport3D.Rendering;

/// <summary>
/// チャンク 1 個分の GPU リソース(spec 6.22.2)。
/// 頂点レイアウトは position(12B) + normal(12B) + scalar(4B) = 28B インターリーブ(slot 0)、
/// 変位は slot 1 の独立した Dynamic バッファ(12B/頂点)。
/// インデックスは**チャンクローカル**で、<see cref="TriangleBase"/> がピック ID の
/// グローバル基点、<see cref="GlobalVertices"/> が変位ギャザー用のローカル→グローバル表。
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

    public int VertexCount => GlobalVertices.Length;

    public uint TriangleIndexCount { get; internal set; }

    public uint EdgeIndexCount { get; internal set; }

    public ID3D11Buffer* VertexBufferHandle => _vertexBuffer.Handle;

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
/// 大規模メッシュ対応(spec 6.22)のため、頂点/インデックスバッファは
/// <see cref="ViewportChunking"/> の境界でチャンク分割され、描画・ピックは
/// チャンク毎のドローになる。D3D11 の単一リソース上限(保証 128MB)を超えないよう
/// チャンクサイズを制限し、5,000万三角形級でもロードできるようにする。
/// 構築はチャンク逐次×チャンク内並列(ピークメモリをチャンク 1 個分に抑えつつ CPU を使い切る)。
/// </para>
/// </summary>
internal sealed unsafe class GpuMesh : IDisposable
{
    public const uint VertexStride = 28;

    public const uint DisplacementStride = 12;

    /// <summary>グリフインスタンスのストライド(<see cref="ViewportGlyph.FloatsPerInstance"/> × 4 バイト)。</summary>
    public const uint GlyphInstanceStride = ViewportGlyph.FloatsPerInstance * sizeof(float);

    /// <summary>グリフインスタンスバッファ 1 本あたりの最大インスタンス数(56B × 200万 = 112MB)。</summary>
    public const int MaxGlyphInstancesPerBuffer = 2_000_000;

    /// <summary>チャンク内の並列ギャザーを使う最小頂点数。</summary>
    private const int ParallelThreshold = 65536;

    private readonly List<GpuMeshChunk> _chunks = [];
    private readonly List<(ComPtr<ID3D11Buffer> Buffer, uint InstanceCount, int CapacityFloats)> _glyphBuffers = [];

    private GpuMesh()
    {
    }

    /// <summary>描画チャンク列(spec 6.22.2)。</summary>
    public IReadOnlyList<GpuMeshChunk> Chunks => _chunks;

    /// <summary>グローバル三角形数(全チャンク合計)。</summary>
    public int TriangleCount { get; private set; }

    /// <summary>ソースメッシュの節点数(チャンク境界の重複は含まない)。</summary>
    public int VertexCount { get; private set; }

    public bool HasScalars { get; private set; }

    /// <summary>EdgeExtractionLimit 超過でエッジ抽出をスキップしたか(spec 6.22.4)。</summary>
    public bool EdgesSkipped { get; private set; }

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
    public static GpuMesh? Create(
        ComPtr<ID3D11Device> device,
        ViewportMesh mesh,
        double originX, double originY, double originZ,
        int edgeExtractionLimit = int.MaxValue,
        int maxVerticesPerChunk = ViewportChunking.DefaultMaxVerticesPerChunk,
        int maxTrianglesPerChunk = ViewportChunking.DefaultMaxTrianglesPerChunk)
    {
        var vertexCount = mesh.VertexCount;
        var triangles = mesh.TriangleIndices;
        if (vertexCount == 0 || triangles.Length < 3)
        {
            return null;
        }

        // メッシュ全体の前処理(float 化・法線)。要素独立の部分は内部で並列化される
        var local = ViewportGeometry.ToLocalPositions(mesh.Positions, originX, originY, originZ);
        var normals = ViewportGeometry.ComputeVertexNormals(local, triangles);
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
        };

        // チャンク逐次×チャンク内並列。中間配列(インターリーブ/変位/ローカルインデックス)は
        // チャンク 1 個分しか同時に存在しないため、ピークメモリが全体サイズに比例しない
        var boundaries = ViewportChunking.ComputeChunkBoundaries(
            triangles, vertexCount, maxVerticesPerChunk, maxTrianglesPerChunk);
        var remap = new ChunkVertexRemap(vertexCount);

        try
        {
            foreach (var boundary in boundaries)
            {
                var (localTriangles, globalVertices) = ViewportChunking.BuildChunkData(triangles, boundary, remap);
                var chunk = new GpuMeshChunk(globalVertices, (uint)boundary.TriangleStart)
                {
                    TriangleIndexCount = (uint)localTriangles.Length,
                };

                // インターリーブ頂点データ(position + normal + scalar)をギャザー
                var vertexData = new float[globalVertices.Length * 7];
                ForEachVertex(globalVertices.Length, localIndex =>
                {
                    var g = globalVertices[localIndex] * 3;
                    var dst = localIndex * 7;
                    vertexData[dst] = local[g];
                    vertexData[dst + 1] = local[g + 1];
                    vertexData[dst + 2] = local[g + 2];
                    vertexData[dst + 3] = normals[g];
                    vertexData[dst + 4] = normals[g + 1];
                    vertexData[dst + 5] = normals[g + 2];
                    vertexData[dst + 6] = scalars is not null && globalVertices[localIndex] < scalars.Length
                        ? (float)scalars[globalVertices[localIndex]]
                        : 0.0f;
                });

                fixed (float* pVertices = vertexData)
                {
                    chunk.SetVertexBuffer(CreateImmutableBuffer(
                        device, pVertices, (uint)(vertexData.Length * sizeof(float)), BindFlag.VertexBuffer));
                }

                // 変位バッファ(Dynamic、常に作る: レイアウトが slot 1 を要求するため)
                var chunkDisplacements = GatherDisplacements(displacements, globalVertices);
                fixed (float* pDisplacements = chunkDisplacements)
                {
                    var desc = new BufferDesc
                    {
                        ByteWidth = (uint)(chunkDisplacements.Length * sizeof(float)),
                        Usage = Usage.Dynamic,
                        BindFlags = (uint)BindFlag.VertexBuffer,
                        CPUAccessFlags = (uint)CpuAccessFlag.Write,
                    };
                    var subresource = new SubresourceData { PSysMem = pDisplacements };
                    ComPtr<ID3D11Buffer> buffer = default;
                    SilkMarshal.ThrowHResult(device.CreateBuffer(in desc, in subresource, ref buffer));
                    chunk.SetDisplacementBuffer(buffer);
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

                result._chunks.Add(chunk);
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
    /// 変位バッファだけを差し替える(チャンク毎にギャザーして Map/WriteDiscard)。
    /// ジオメトリ本体は再構築しないため、過渡応答のフレーム再生で毎フレーム呼んでも軽い。
    /// </summary>
    public void UpdateDisplacements(ComPtr<ID3D11DeviceContext> context, double[]? displacements)
    {
        foreach (var chunk in _chunks)
        {
            if (chunk.DisplacementBufferHandle is null)
            {
                continue;
            }

            var data = GatherDisplacements(displacements, chunk.GlobalVertices);
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

    /// <summary>チャンクの変位ギャザー(グローバル配列→ローカル float)。大チャンクは並列。</summary>
    private static float[] GatherDisplacements(double[]? displacements, int[] globalVertices)
    {
        var result = new float[globalVertices.Length * 3];
        if (displacements is null)
        {
            return result;
        }

        ForEachVertex(globalVertices.Length, localIndex =>
        {
            var g = globalVertices[localIndex] * 3;
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

        _chunks.Clear();
        ReleaseGlyphBuffers();
        GlyphInstanceCount = 0;
    }
}
