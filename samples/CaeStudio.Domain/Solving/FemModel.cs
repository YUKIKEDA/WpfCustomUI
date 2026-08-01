using CaeStudio.Domain.Meshing;
using CaeStudio.Domain.Models;

namespace CaeStudio.Domain.Solving;

/// <summary>
/// 平面応力 FEM の離散化モデル: CST(3節点三角形・定ひずみ)要素で
/// 剛性行列(CSR)・荷重ベクトル・集中質量ベクトルを組み立てる。
/// 拘束 DOF は消去法(自由 DOF のみで方程式を構成)で処理する。
/// </summary>
public sealed class FemModel
{
    private FemModel(
        Mesh2D mesh, Material material, double thickness,
        int[] dofMap, int freeDofCount, SparseMatrixCsr stiffness, double[] loads, double[] lumpedMass)
    {
        Mesh = mesh;
        Material = material;
        Thickness = thickness;
        DofMap = dofMap;
        FreeDofCount = freeDofCount;
        Stiffness = stiffness;
        Loads = loads;
        LumpedMass = lumpedMass;
    }

    public Mesh2D Mesh { get; }

    public Material Material { get; }

    public double Thickness { get; }

    /// <summary>全 DOF(node*2 + 成分)→ 自由 DOF インデックス(拘束は -1)。</summary>
    public int[] DofMap { get; }

    public int FreeDofCount { get; }

    /// <summary>自由 DOF 上の剛性行列。</summary>
    public SparseMatrixCsr Stiffness { get; }

    /// <summary>自由 DOF 上の節点荷重ベクトル [N]。</summary>
    public double[] Loads { get; }

    /// <summary>自由 DOF 上の集中質量 [t](固有値解析用。行列は対角)。</summary>
    public double[] LumpedMass { get; }

    /// <summary>自由 DOF 解ベクトルを全節点変位 [ux0, uy0, ux1, ...] に展開する。</summary>
    public double[] ExpandToFullDisplacements(double[] freeSolution)
    {
        var full = new double[Mesh.NodeCount * 2];
        for (var dof = 0; dof < full.Length; dof++)
        {
            var free = DofMap[dof];
            if (free >= 0)
            {
                full[dof] = freeSolution[free];
            }
        }

        return full;
    }

    /// <summary>メッシュ+材料+境界条件からモデルを組み立てる。</summary>
    public static FemModel Build(
        Mesh2D mesh, Material material, double thickness, IReadOnlyList<BoundaryCondition> boundaryConditions)
    {
        // ---- 拘束の解決: 全 DOF → 自由 DOF の写像 ----
        var constrained = new bool[mesh.NodeCount * 2];
        foreach (var bc in boundaryConditions)
        {
            if (bc.Constraint == ConstraintKind.None || !mesh.Groups.TryGetValue(bc.GroupName, out var group))
            {
                continue;
            }

            foreach (var node in group.Nodes)
            {
                if (bc.Constraint is ConstraintKind.Fixed or ConstraintKind.PinX)
                {
                    constrained[node * 2] = true;
                }

                if (bc.Constraint is ConstraintKind.Fixed or ConstraintKind.PinY)
                {
                    constrained[node * 2 + 1] = true;
                }
            }
        }

        var dofMap = new int[mesh.NodeCount * 2];
        var freeDofCount = 0;
        for (var dof = 0; dof < dofMap.Length; dof++)
        {
            dofMap[dof] = constrained[dof] ? -1 : freeDofCount++;
        }

        if (freeDofCount == 0)
        {
            throw new InvalidOperationException("すべての自由度が拘束されています。");
        }

        // ---- 剛性行列+集中質量の組み立て ----
        var rows = new Dictionary<int, double>[freeDofCount];
        for (var i = 0; i < freeDofCount; i++)
        {
            rows[i] = [];
        }

        var d = ElasticityMatrix(material);
        var lumpedMass = new double[freeDofCount];
        var positions = mesh.Positions;
        var triangles = mesh.Triangles;

        Span<int> elementDofs = stackalloc int[6];
        Span<double> ke = stackalloc double[36];

        for (var t = 0; t < triangles.Length; t += 3)
        {
            var (n0, n1, n2) = (triangles[t], triangles[t + 1], triangles[t + 2]);
            var area = ElementStiffness(positions, n0, n1, n2, d, thickness, ke);

            elementDofs[0] = n0 * 2;
            elementDofs[1] = n0 * 2 + 1;
            elementDofs[2] = n1 * 2;
            elementDofs[3] = n1 * 2 + 1;
            elementDofs[4] = n2 * 2;
            elementDofs[5] = n2 * 2 + 1;

            for (var i = 0; i < 6; i++)
            {
                var rowFree = dofMap[elementDofs[i]];
                if (rowFree < 0)
                {
                    continue;
                }

                var row = rows[rowFree];
                for (var j = 0; j < 6; j++)
                {
                    var columnFree = dofMap[elementDofs[j]];
                    if (columnFree < 0)
                    {
                        continue;
                    }

                    var value = ke[i * 6 + j];
                    row[columnFree] = row.GetValueOrDefault(columnFree) + value;
                }
            }

            // 集中質量: 要素質量 ρtA を 3 節点に等分(各節点の x/y DOF に同じ値)
            var nodeMass = material.Density * thickness * area / 3.0;
            for (var i = 0; i < 6; i++)
            {
                var free = dofMap[elementDofs[i]];
                if (free >= 0)
                {
                    lumpedMass[free] += nodeMass;
                }
            }
        }

        var stiffness = SparseMatrixCsr.FromRows(rows);

        // ---- 表面力 → 節点荷重(ポリライングループの辺長×板厚で積分) ----
        var loads = new double[freeDofCount];
        foreach (var bc in boundaryConditions)
        {
            if ((bc.TractionX == 0 && bc.TractionY == 0) ||
                !mesh.Groups.TryGetValue(bc.GroupName, out var group) || !group.IsPolyline)
            {
                continue;
            }

            var nodes = group.Nodes;
            for (var s = 0; s < nodes.Length - 1; s++)
            {
                var (a, b) = (nodes[s], nodes[s + 1]);
                var dx = positions[b * 2] - positions[a * 2];
                var dy = positions[b * 2 + 1] - positions[a * 2 + 1];
                var halfForce = Math.Sqrt(dx * dx + dy * dy) * thickness / 2.0;

                foreach (var node in (ReadOnlySpan<int>)[a, b])
                {
                    var freeX = dofMap[node * 2];
                    var freeY = dofMap[node * 2 + 1];
                    if (freeX >= 0)
                    {
                        loads[freeX] += bc.TractionX * halfForce;
                    }

                    if (freeY >= 0)
                    {
                        loads[freeY] += bc.TractionY * halfForce;
                    }
                }
            }
        }

        return new FemModel(mesh, material, thickness, dofMap, freeDofCount, stiffness, loads, lumpedMass);
    }

    /// <summary>平面応力の弾性マトリクス D(3×3、行優先)。</summary>
    public static double[] ElasticityMatrix(Material material)
    {
        var e = material.YoungsModulus;
        var nu = material.PoissonsRatio;
        var factor = e / (1.0 - nu * nu);
        return
        [
            factor, factor * nu, 0.0,
            factor * nu, factor, 0.0,
            0.0, 0.0, factor * (1.0 - nu) / 2.0,
        ];
    }

    /// <summary>
    /// CST 要素剛性 Ke = t·A·BᵀDB(6×6、行優先で <paramref name="ke"/> に書き込み)。
    /// 戻り値は要素面積 A(反時計回り前提で正)。
    /// </summary>
    internal static double ElementStiffness(
        double[] positions, int n0, int n1, int n2, double[] d, double thickness, Span<double> ke)
    {
        Span<double> b = stackalloc double[18];
        var area = StrainDisplacementMatrix(positions, n0, n1, n2, b);

        // Ke = (t·A)·Bᵀ(3×6)ᵀ D(3×3) B(3×6)
        Span<double> db = stackalloc double[18];
        for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < 6; j++)
            {
                db[i * 6 + j] = d[i * 3] * b[j] + d[i * 3 + 1] * b[6 + j] + d[i * 3 + 2] * b[12 + j];
            }
        }

        var weight = thickness * area;
        for (var i = 0; i < 6; i++)
        {
            for (var j = 0; j < 6; j++)
            {
                ke[i * 6 + j] = weight *
                    (b[i] * db[j] + b[6 + i] * db[6 + j] + b[12 + i] * db[12 + j]);
            }
        }

        return area;
    }

    /// <summary>
    /// ひずみ-変位マトリクス B(3×6、行優先)。ε = B·ue(ue = [ux0, uy0, ux1, ...])。
    /// 戻り値は要素面積(反時計回りで正)。
    /// </summary>
    internal static double StrainDisplacementMatrix(
        double[] positions, int n0, int n1, int n2, Span<double> b)
    {
        var (x0, y0) = (positions[n0 * 2], positions[n0 * 2 + 1]);
        var (x1, y1) = (positions[n1 * 2], positions[n1 * 2 + 1]);
        var (x2, y2) = (positions[n2 * 2], positions[n2 * 2 + 1]);

        var signedArea2 = (x1 - x0) * (y2 - y0) - (y1 - y0) * (x2 - x0);
        var area = signedArea2 / 2.0;

        // 形状関数勾配: bᵢ = yⱼ - yₖ, cᵢ = xₖ - xⱼ(添字は巡回)
        Span<double> gradB = [y1 - y2, y2 - y0, y0 - y1];
        Span<double> gradC = [x2 - x1, x0 - x2, x1 - x0];

        b.Clear();
        for (var i = 0; i < 3; i++)
        {
            var bi = gradB[i] / signedArea2;
            var ci = gradC[i] / signedArea2;
            b[i * 2] = bi;             // εxx ← ux
            b[6 + i * 2 + 1] = ci;     // εyy ← uy
            b[12 + i * 2] = ci;        // γxy ← ux
            b[12 + i * 2 + 1] = bi;    // γxy ← uy
        }

        return area;
    }
}
