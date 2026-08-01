using CaeStudio.Domain.Meshing;
using CaeStudio.Domain.Solving;
using WpfCustomUI.Viewport3D;

namespace CaeStudio.App;

/// <summary>
/// Domain の 2D メッシュ/解析結果 → ライブラリの <see cref="ViewportMesh"/> への変換。
/// 2D 平面応力モデルなので z=0 平面に配置し、変位は (ux, uy, 0) とする。
/// </summary>
public static class ViewportMeshFactory
{
    /// <summary>プリ処理用のメッシュプレビュー(単色+エッジ)。</summary>
    public static ViewportMesh CreatePreview(Mesh2D mesh, string name) => new()
    {
        Name = name,
        Positions = To3D(mesh.Positions),
        TriangleIndices = (int[])mesh.Triangles.Clone(),
        ShowEdges = true,
    };

    /// <summary>
    /// 静解析結果(von Mises コンター+変位)。
    /// エッジは非表示(勾配の急な領域は要素が細かく、エッジ線がコンターを覆い隠すため)。
    /// </summary>
    public static ViewportMesh CreateStaticResult(StaticResult result, string name)
    {
        var mesh = CreatePreview(result.Mesh, name);
        mesh.ScalarValues = result.NodalVonMises;
        mesh.Displacements = ToDisplacements3D(result.Displacements);
        mesh.ShowEdges = false;
        return mesh;
    }

    /// <summary>
    /// 固有モード形状(コンター = 正規化変位量 |u|、変位 = 最大 1 mm に正規化した形状)。
    /// </summary>
    public static ViewportMesh CreateModeShape(ModalResult result, ModalMode mode, string name)
    {
        var mesh = CreatePreview(result.Mesh, name);

        var nodeCount = mode.Shape.Length / 2;
        var scale = mode.MaxAmplitude > 0 ? 1.0 / mode.MaxAmplitude : 1.0;
        var displacements = new double[nodeCount * 3];
        var scalars = new double[nodeCount];
        for (var node = 0; node < nodeCount; node++)
        {
            var ux = mode.Shape[node * 2] * scale;
            var uy = mode.Shape[node * 2 + 1] * scale;
            displacements[node * 3] = ux;
            displacements[node * 3 + 1] = uy;
            scalars[node] = Math.Sqrt(ux * ux + uy * uy);
        }

        mesh.ScalarValues = scalars;
        mesh.Displacements = displacements;
        mesh.ShowEdges = false;
        return mesh;
    }

    /// <summary>2D 変位 [ux, uy]×n → 3D 変位 [ux, uy, 0]×n。</summary>
    public static double[] ToDisplacements3D(double[] displacements2D)
    {
        var nodeCount = displacements2D.Length / 2;
        var result = new double[nodeCount * 3];
        for (var node = 0; node < nodeCount; node++)
        {
            result[node * 3] = displacements2D[node * 2];
            result[node * 3 + 1] = displacements2D[node * 2 + 1];
        }

        return result;
    }

    private static double[] To3D(double[] positions2D)
    {
        var nodeCount = positions2D.Length / 2;
        var result = new double[nodeCount * 3];
        for (var node = 0; node < nodeCount; node++)
        {
            result[node * 3] = positions2D[node * 2];
            result[node * 3 + 1] = positions2D[node * 2 + 1];
        }

        return result;
    }
}
