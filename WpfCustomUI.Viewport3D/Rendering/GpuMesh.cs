using System.Numerics;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace WpfCustomUI.Viewport3D.Rendering;

/// <summary>
/// <see cref="ViewportMesh"/> 1 パーツ分の GPU リソース(頂点/インデックスバッファ)。
/// 頂点レイアウトは position(12B) + normal(12B) + scalar(4B) = 28B インターリーブ(slot 0)。
/// 変位は slot 1 の独立した Dynamic バッファ(12B/頂点)に置き、
/// フレーム再生での差し替え時にジオメトリ全体を再構築せず部分更新できるようにする(spec 6.18.2)。
/// </summary>
internal sealed unsafe class GpuMesh : IDisposable
{
    public const uint VertexStride = 28;

    public const uint DisplacementStride = 12;

    private ComPtr<ID3D11Buffer> _vertexBuffer;
    private ComPtr<ID3D11Buffer> _displacementBuffer;
    private ComPtr<ID3D11Buffer> _triangleIndexBuffer;
    private ComPtr<ID3D11Buffer> _edgeIndexBuffer;

    private GpuMesh()
    {
    }

    public uint TriangleIndexCount { get; private set; }

    public uint EdgeIndexCount { get; private set; }

    public int VertexCount { get; private set; }

    public bool HasScalars { get; private set; }

    public Vector4 Color { get; set; }

    public bool ShowEdges { get; set; }

    public bool IsTransparent => Color.W < 0.999f;

    public ID3D11Buffer* VertexBufferHandle => _vertexBuffer.Handle;

    public ID3D11Buffer* DisplacementBufferHandle => _displacementBuffer.Handle;

    public ID3D11Buffer* TriangleIndexBufferHandle => _triangleIndexBuffer.Handle;

    public ID3D11Buffer* EdgeIndexBufferHandle => _edgeIndexBuffer.Handle;

    /// <summary>
    /// メッシュモデルから GPU リソースを構築する。座標は origin(シーン中心)で再センタリング済みの
    /// float に変換され、法線・エッジはここで計算される。
    /// </summary>
    public static GpuMesh? Create(
        ComPtr<ID3D11Device> device,
        ViewportMesh mesh,
        double originX, double originY, double originZ)
    {
        var vertexCount = mesh.VertexCount;
        var triangles = mesh.TriangleIndices;
        if (vertexCount == 0 || triangles.Length < 3)
        {
            return null;
        }

        var local = ViewportGeometry.ToLocalPositions(mesh.Positions, originX, originY, originZ);
        var normals = ViewportGeometry.ComputeVertexNormals(local, triangles);
        var scalars = ViewportGeometry.ToScalarArray(mesh.ScalarValues, vertexCount);
        var edges = ViewportGeometry.ExtractEdges(triangles);

        // インターリーブ頂点データを構築
        var vertexData = new float[vertexCount * 7];
        for (var v = 0; v < vertexCount; v++)
        {
            var src = v * 3;
            var dst = v * 7;
            vertexData[dst] = local[src];
            vertexData[dst + 1] = local[src + 1];
            vertexData[dst + 2] = local[src + 2];
            vertexData[dst + 3] = normals[src];
            vertexData[dst + 4] = normals[src + 1];
            vertexData[dst + 5] = normals[src + 2];
            vertexData[dst + 6] = scalars[v];
        }

        var result = new GpuMesh
        {
            TriangleIndexCount = (uint)triangles.Length,
            EdgeIndexCount = (uint)edges.Length,
            VertexCount = vertexCount,
            HasScalars = mesh.ScalarValues is not null,
        };

        fixed (float* pVertices = vertexData)
        {
            result._vertexBuffer = CreateImmutableBuffer(
                device, pVertices, (uint)(vertexData.Length * sizeof(float)), BindFlag.VertexBuffer);
        }

        // 変位バッファは常に作る(レイアウトが slot 1 を要求するため)。変位なしはゼロ埋め
        var displacements = ViewportDeformation.ToDisplacementArray(mesh.Displacements, vertexCount);
        fixed (float* pDisplacements = displacements)
        {
            var desc = new BufferDesc
            {
                ByteWidth = (uint)(displacements.Length * sizeof(float)),
                Usage = Usage.Dynamic,
                BindFlags = (uint)BindFlag.VertexBuffer,
                CPUAccessFlags = (uint)CpuAccessFlag.Write,
            };
            var subresource = new SubresourceData { PSysMem = pDisplacements };
            SilkMarshal.ThrowHResult(device.CreateBuffer(in desc, in subresource, ref result._displacementBuffer));
        }

        fixed (int* pIndices = triangles)
        {
            result._triangleIndexBuffer = CreateImmutableBuffer(
                device, pIndices, (uint)(triangles.Length * sizeof(int)), BindFlag.IndexBuffer);
        }

        if (edges.Length > 0)
        {
            fixed (int* pEdges = edges)
            {
                result._edgeIndexBuffer = CreateImmutableBuffer(
                    device, pEdges, (uint)(edges.Length * sizeof(int)), BindFlag.IndexBuffer);
            }
        }

        return result;
    }

    /// <summary>
    /// 変位バッファだけを差し替える(Map/WriteDiscard)。ジオメトリ本体は再構築しないため、
    /// 過渡応答のフレーム再生で毎フレーム呼んでも軽い。
    /// </summary>
    public void UpdateDisplacements(ComPtr<ID3D11DeviceContext> context, double[]? displacements)
    {
        if (_displacementBuffer.Handle is null)
        {
            return;
        }

        var data = ViewportDeformation.ToDisplacementArray(displacements, VertexCount);
        MappedSubresource mapped = default;
        SilkMarshal.ThrowHResult(context.Map(_displacementBuffer, 0, Map.WriteDiscard, 0, ref mapped));
        try
        {
            fixed (float* pData = data)
            {
                System.Buffer.MemoryCopy(pData, mapped.PData, data.Length * sizeof(float), data.Length * sizeof(float));
            }
        }
        finally
        {
            context.Unmap(_displacementBuffer, 0);
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
