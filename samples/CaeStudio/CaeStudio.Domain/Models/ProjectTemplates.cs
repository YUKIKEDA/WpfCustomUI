namespace CaeStudio.Domain.Models;

/// <summary>
/// 新規プロジェクトのテンプレート。境界条件は幾何条件で定義された節点グループ
/// (メッシュ生成時に毎回解決される)に紐づくため、再メッシュに自動追従する(spec 6.26.4)。
/// </summary>
public static class ProjectTemplates
{
    /// <summary>グループ名の定数(メッシャと境界条件の共有キー)。</summary>
    public static class Groups
    {
        public const string LeftEdge = "LeftEdge";
        public const string RightEdge = "RightEdge";
        public const string HoleEdge = "HoleEdge";
        public const string XAxis = "XAxis";
        public const string YAxis = "YAxis";
        public const string FixedEdge = "FixedEdge";
        public const string TipEdge = "TipEdge";
    }

    /// <summary>
    /// 円孔付き平板の一軸引張(Kirsch 問題)。左右辺に ±x 引張、
    /// 対称軸上の節点をピン留めして剛体モードを除去する(対称問題なので厳密に正しい)。
    /// </summary>
    public static CaeProjectData CreatePlateWithHole(double tension = 100.0) => new()
    {
        Name = "円孔付き平板の引張",
        Geometry = new PlateWithHoleGeometry(),
        Material = Material.Steel,
        AnalysisType = AnalysisType.Static,
        BoundaryConditions =
        [
            new BoundaryCondition
            {
                GroupName = Groups.RightEdge, DisplayName = "右辺 引張荷重",
                TractionX = tension, IsLoadEditable = true,
            },
            new BoundaryCondition
            {
                GroupName = Groups.LeftEdge, DisplayName = "左辺 引張荷重",
                TractionX = -tension, IsLoadEditable = true,
            },
            new BoundaryCondition
            {
                GroupName = Groups.XAxis, DisplayName = "x 軸対称(uy=0)",
                Constraint = ConstraintKind.PinY,
            },
            new BoundaryCondition
            {
                GroupName = Groups.YAxis, DisplayName = "y 軸対称(ux=0)",
                Constraint = ConstraintKind.PinX,
            },
        ],
    };

    /// <summary>
    /// 片持ち板(x=0 固定)。静解析では先端辺にせん断荷重、
    /// 固有値解析では面内曲げモード(Euler-Bernoulli 梁理論と比較可能)。
    /// </summary>
    public static CaeProjectData CreateCantileverPlate(
        AnalysisType analysisType = AnalysisType.Modal, double tipShear = 10.0) => new()
    {
        Name = "片持ち板",
        Geometry = new CantileverPlateGeometry(),
        Material = Material.Steel,
        AnalysisType = analysisType,
        BoundaryConditions =
        [
            new BoundaryCondition
            {
                GroupName = Groups.FixedEdge, DisplayName = "固定端",
                Constraint = ConstraintKind.Fixed,
            },
            new BoundaryCondition
            {
                GroupName = Groups.TipEdge, DisplayName = "先端 せん断荷重",
                TractionY = -tipShear, IsLoadEditable = true,
            },
        ],
    };
}
