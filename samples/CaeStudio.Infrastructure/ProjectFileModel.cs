using CaeStudio.Domain.Models;

namespace CaeStudio.Infrastructure;

/// <summary>
/// .wcuproj の JSON スキーマ(DTO)。Domain のレコードにシリアライズ属性を付けず
/// Infrastructure 側で明示マッピングすることで、Domain の純粋性とファイル形式の
/// バージョン管理(FormatVersion)を両立する(spec 6.26.6)。
/// </summary>
public sealed record ProjectFileModel
{
    public int FormatVersion { get; init; } = 1;

    public required string Name { get; init; }

    /// <summary>"PlateWithHole" または "CantileverPlate"。</summary>
    public required string TemplateKind { get; init; }

    public required Dictionary<string, double> GeometryParameters { get; init; }

    public required string MaterialName { get; init; }

    /// <summary>"Static" または "Modal"。</summary>
    public required string AnalysisType { get; init; }

    public required List<BoundaryConditionModel> BoundaryConditions { get; init; }

    public double SolverTolerance { get; init; }

    public int SolverMaxIterations { get; init; }

    public int SolverModeCount { get; init; }

    public sealed record BoundaryConditionModel(
        string GroupName, string DisplayName, string Constraint,
        double TractionX, double TractionY, bool IsLoadEditable);

    // ================= Domain ⇔ DTO マッピング =================

    public static ProjectFileModel FromDomain(CaeProjectData project)
    {
        var (kind, parameters) = project.Geometry switch
        {
            PlateWithHoleGeometry p => ("PlateWithHole", new Dictionary<string, double>
            {
                ["Width"] = p.Width,
                ["Height"] = p.Height,
                ["HoleDiameter"] = p.HoleDiameter,
                ["RadialDivisions"] = p.RadialDivisions,
                ["AngularDivisions"] = p.AngularDivisions,
                ["Thickness"] = p.Thickness,
            }),
            CantileverPlateGeometry b => ("CantileverPlate", new Dictionary<string, double>
            {
                ["Length"] = b.Length,
                ["Height"] = b.Height,
                ["DivisionsX"] = b.DivisionsX,
                ["DivisionsY"] = b.DivisionsY,
                ["Thickness"] = b.Thickness,
            }),
            _ => throw new NotSupportedException($"未知の形状テンプレート: {project.Geometry.GetType().Name}"),
        };

        return new ProjectFileModel
        {
            Name = project.Name,
            TemplateKind = kind,
            GeometryParameters = parameters,
            MaterialName = project.Material.Name,
            AnalysisType = project.AnalysisType.ToString(),
            BoundaryConditions =
            [
                .. project.BoundaryConditions.Select(bc => new BoundaryConditionModel(
                    bc.GroupName, bc.DisplayName, bc.Constraint.ToString(),
                    bc.TractionX, bc.TractionY, bc.IsLoadEditable)),
            ],
            SolverTolerance = project.Solver.Tolerance,
            SolverMaxIterations = project.Solver.MaxIterations,
            SolverModeCount = project.Solver.ModeCount,
        };
    }

    public CaeProjectData ToDomain()
    {
        var p = GeometryParameters;
        GeometryDefinition geometry = TemplateKind switch
        {
            "PlateWithHole" => new PlateWithHoleGeometry
            {
                Width = p["Width"],
                Height = p["Height"],
                HoleDiameter = p["HoleDiameter"],
                RadialDivisions = (int)p["RadialDivisions"],
                AngularDivisions = (int)p["AngularDivisions"],
                Thickness = p["Thickness"],
            },
            "CantileverPlate" => new CantileverPlateGeometry
            {
                Length = p["Length"],
                Height = p["Height"],
                DivisionsX = (int)p["DivisionsX"],
                DivisionsY = (int)p["DivisionsY"],
                Thickness = p["Thickness"],
            },
            _ => throw new InvalidDataException($"未知のテンプレート種別: {TemplateKind}"),
        };

        return new CaeProjectData
        {
            Name = Name,
            Geometry = geometry,
            Material = MaterialName == Material.Aluminum.Name ? Material.Aluminum : Material.Steel,
            AnalysisType = Enum.Parse<AnalysisType>(AnalysisType),
            BoundaryConditions =
            [
                .. BoundaryConditions.Select(bc => new BoundaryCondition
                {
                    GroupName = bc.GroupName,
                    DisplayName = bc.DisplayName,
                    Constraint = Enum.Parse<ConstraintKind>(bc.Constraint),
                    TractionX = bc.TractionX,
                    TractionY = bc.TractionY,
                    IsLoadEditable = bc.IsLoadEditable,
                }),
            ],
            Solver = new SolverSettings
            {
                Tolerance = SolverTolerance,
                MaxIterations = SolverMaxIterations,
                ModeCount = SolverModeCount,
            },
        };
    }
}
