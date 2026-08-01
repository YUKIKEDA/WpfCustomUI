namespace CaeStudio.Domain.Models;

/// <summary>実行する解析の種類。</summary>
public enum AnalysisType
{
    /// <summary>線形静解析(CG 反復法)。</summary>
    Static,

    /// <summary>固有値解析(集中質量+逆反復法)。</summary>
    Modal,
}

/// <summary>反復ソルバの設定。</summary>
public sealed record SolverSettings
{
    /// <summary>CG の相対残差の収束判定値。</summary>
    public double Tolerance { get; init; } = 1e-8;

    /// <summary>CG の最大反復回数。</summary>
    public int MaxIterations { get; init; } = 20_000;

    /// <summary>固有値解析で求める下位モード数。</summary>
    public int ModeCount { get; init; } = 4;
}

/// <summary>
/// プロジェクトの入力一式(不変レコード)。これだけで解析結果が決定的に再現できる
/// (結果はファイル保存しない方針の根拠。spec 6.26.6)。
/// </summary>
public sealed record CaeProjectData
{
    /// <summary>プロジェクト名。</summary>
    public required string Name { get; init; }

    /// <summary>形状テンプレートのパラメータ。</summary>
    public required GeometryDefinition Geometry { get; init; }

    /// <summary>材料。</summary>
    public required Material Material { get; init; }

    /// <summary>境界条件(テンプレート定義の節点グループに対する値)。</summary>
    public required IReadOnlyList<BoundaryCondition> BoundaryConditions { get; init; }

    /// <summary>解析の種類。</summary>
    public AnalysisType AnalysisType { get; init; } = AnalysisType.Static;

    /// <summary>ソルバ設定。</summary>
    public SolverSettings Solver { get; init; } = new();
}
