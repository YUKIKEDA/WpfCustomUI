using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace WpfCustomUI.Viewport3D.Rendering;

/// <summary>
/// メッシュ 1 パーツ分の選択ハイライト GPU リソース(spec 6.17.3)。
/// <para>
/// - 選択面: 選択三角形だけを集めたインデックスバッファ(頂点バッファは <see cref="GpuMesh"/> を再利用)。
/// - 選択節点: 節点位置+コーナーオフセットの 6 頂点クワッド列(ポイントシェーダで円形描画)。
/// 選択変更のたびに作り直す(ユーザー操作ペースなのでコストは無視できる)。
/// </para>
/// </summary>
internal sealed unsafe class GpuSelectionMesh : IDisposable
{
    /// <summary>ポイント頂点レイアウト: position(12B) + corner(8B) + displacement(12B) = 32B。</summary>
    public const uint PointVertexStride = 32;

    private ComPtr<ID3D11Buffer> _faceIndexBuffer;
    private ComPtr<ID3D11Buffer> _nodeVertexBuffer;

    private GpuSelectionMesh()
    {
    }

    public uint FaceIndexCount { get; private set; }

    public uint NodeVertexCount { get; private set; }

    public ID3D11Buffer* FaceIndexBufferHandle => _faceIndexBuffer.Handle;

    public ID3D11Buffer* NodeVertexBufferHandle => _nodeVertexBuffer.Handle;

    /// <summary>選択集合から GPU リソースを構築する。選択が空(または全て範囲外)なら null。</summary>
    public static GpuSelectionMesh? Create(
        ComPtr<ID3D11Device> device,
        ViewportMesh source,
        IReadOnlyCollection<int> selectedFaces,
        IReadOnlyCollection<int> selectedNodes,
        double originX, double originY, double originZ)
    {
        var triangles = source.TriangleIndices;
        var positions = source.Positions;
        var triangleCount = triangles.Length / 3;
        var vertexCount = positions.Length / 3;

        // 選択面インデックス(ジオメトリ差し替えで無効になったインデックスは黙って捨てる)
        var faceIndices = new List<int>(selectedFaces.Count * 3);
        foreach (var face in selectedFaces)
        {
            if (face < 0 || face >= triangleCount)
            {
                continue;
            }

            faceIndices.Add(triangles[face * 3]);
            faceIndices.Add(triangles[face * 3 + 1]);
            faceIndices.Add(triangles[face * 3 + 2]);
        }

        // 選択節点クワッド(変位を頂点属性として持たせ、変形表示に追従させる。
        // Displacements 差し替え時は WcuViewport 側が選択バッファごと再構築する)
        var displacements = ViewportDeformation.ToDisplacementArray(source.Displacements, vertexCount);
        var validNodes = selectedNodes.Where(n => n >= 0 && n < vertexCount).ToList();
        var nodeVertices = new float[validNodes.Count * 6 * 8];
        ReadOnlySpan<(float X, float Y)> corners =
        [
            (-0.5f, -0.5f), (0.5f, -0.5f), (0.5f, 0.5f),
            (-0.5f, -0.5f), (0.5f, 0.5f), (-0.5f, 0.5f),
        ];

        for (var i = 0; i < validNodes.Count; i++)
        {
            var node = validNodes[i];
            var px = (float)(positions[node * 3] - originX);
            var py = (float)(positions[node * 3 + 1] - originY);
            var pz = (float)(positions[node * 3 + 2] - originZ);

            for (var c = 0; c < 6; c++)
            {
                var dst = (i * 6 + c) * 8;
                nodeVertices[dst] = px;
                nodeVertices[dst + 1] = py;
                nodeVertices[dst + 2] = pz;
                nodeVertices[dst + 3] = corners[c].X;
                nodeVertices[dst + 4] = corners[c].Y;
                nodeVertices[dst + 5] = displacements[node * 3];
                nodeVertices[dst + 6] = displacements[node * 3 + 1];
                nodeVertices[dst + 7] = displacements[node * 3 + 2];
            }
        }

        if (faceIndices.Count == 0 && nodeVertices.Length == 0)
        {
            return null;
        }

        var result = new GpuSelectionMesh
        {
            FaceIndexCount = (uint)faceIndices.Count,
            NodeVertexCount = (uint)(validNodes.Count * 6),
        };

        if (faceIndices.Count > 0)
        {
            var array = faceIndices.ToArray();
            fixed (int* pIndices = array)
            {
                result._faceIndexBuffer = CreateImmutableBuffer(
                    device, pIndices, (uint)(array.Length * sizeof(int)), BindFlag.IndexBuffer);
            }
        }

        if (nodeVertices.Length > 0)
        {
            fixed (float* pVertices = nodeVertices)
            {
                result._nodeVertexBuffer = CreateImmutableBuffer(
                    device, pVertices, (uint)(nodeVertices.Length * sizeof(float)), BindFlag.VertexBuffer);
            }
        }

        return result;
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
        _faceIndexBuffer.Dispose();
        _nodeVertexBuffer.Dispose();
        _faceIndexBuffer = default;
        _nodeVertexBuffer = default;
    }
}
