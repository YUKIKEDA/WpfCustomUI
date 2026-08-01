using CaeStudio.Domain.Models;
using WpfCustomUI.Controls;

namespace CaeStudio.App.ViewModels;

/// <summary>
/// 現在のプロジェクト入力 → PropertyGrid のアイテム列を構築する。
/// 各アイテムの編集は <paramref name="update"/> コールバック経由で
/// 不変レコードの差し替え(ProjectStore.Update)に変換される。
/// </summary>
public static class ProjectPropertyFactory
{
    public const string CategoryGeometry = "形状";
    public const string CategoryLoad = "荷重";
    public const string CategoryMaterial = "材料";
    public const string CategorySolver = "ソルバ";

    public static IReadOnlyList<PropertyItem> Build(
        CaeProjectData project,
        Action<Func<CaeProjectData, CaeProjectData>> update,
        string? categoryFilter = null)
    {
        var items = new List<PropertyItem>();

        switch (project.Geometry)
        {
            case PlateWithHoleGeometry plate:
                AddPlateItems(items, plate, update);
                break;
            case CantileverPlateGeometry beam:
                AddBeamItems(items, beam, update);
                break;
        }

        AddLoadItems(items, project, update);
        AddMaterialItems(items, project, update);
        AddSolverItems(items, project, update);

        return categoryFilter is null
            ? items
            : [.. items.Where(i => i.Category == categoryFilter)];
    }

    // ================= 形状 =================

    private static void AddPlateItems(
        List<PropertyItem> items, PlateWithHoleGeometry plate,
        Action<Func<CaeProjectData, CaeProjectData>> update)
    {
        items.Add(Numeric("板幅 W", CategoryGeometry, plate.Width, "mm", 10, 10000,
            v => update(p => p with { Geometry = Plate(p) with { Width = v } })));
        items.Add(Numeric("板高さ H", CategoryGeometry, plate.Height, "mm", 10, 10000,
            v => update(p => p with { Geometry = Plate(p) with { Height = v } })));
        items.Add(Numeric("孔直径 d", CategoryGeometry, plate.HoleDiameter, "mm", 1, 5000,
            v => update(p => p with { Geometry = Plate(p) with { HoleDiameter = v } })));
        items.Add(Numeric("板厚 t", CategoryGeometry, plate.Thickness, "mm", 0.1, 1000,
            v => update(p => p with { Geometry = Plate(p) with { Thickness = v } })));
        items.Add(Numeric("半径方向分割", CategoryGeometry, plate.RadialDivisions, null, 4, 128,
            v => update(p => p with { Geometry = Plate(p) with { RadialDivisions = (int)v } }), "0"));
        items.Add(Numeric("周方向分割", CategoryGeometry, plate.AngularDivisions, null, 8, 512,
            v => update(p => p with { Geometry = Plate(p) with { AngularDivisions = (int)v } }), "0"));
    }

    private static void AddBeamItems(
        List<PropertyItem> items, CantileverPlateGeometry beam,
        Action<Func<CaeProjectData, CaeProjectData>> update)
    {
        items.Add(Numeric("梁長さ L", CategoryGeometry, beam.Length, "mm", 10, 10000,
            v => update(p => p with { Geometry = Beam(p) with { Length = v } })));
        items.Add(Numeric("梁せい H", CategoryGeometry, beam.Height, "mm", 1, 5000,
            v => update(p => p with { Geometry = Beam(p) with { Height = v } })));
        items.Add(Numeric("板厚 t", CategoryGeometry, beam.Thickness, "mm", 0.1, 1000,
            v => update(p => p with { Geometry = Beam(p) with { Thickness = v } })));
        items.Add(Numeric("x 方向分割", CategoryGeometry, beam.DivisionsX, null, 2, 1024,
            v => update(p => p with { Geometry = Beam(p) with { DivisionsX = (int)v } }), "0"));
        items.Add(Numeric("y 方向分割", CategoryGeometry, beam.DivisionsY, null, 2, 256,
            v => update(p => p with { Geometry = Beam(p) with { DivisionsY = (int)v } }), "0"));
    }

    // ================= 荷重(編集可能な境界条件のみ) =================

    private static void AddLoadItems(
        List<PropertyItem> items, CaeProjectData project,
        Action<Func<CaeProjectData, CaeProjectData>> update)
    {
        foreach (var bc in project.BoundaryConditions)
        {
            if (!bc.IsLoadEditable)
            {
                continue;
            }

            var groupName = bc.GroupName;
            var dominantX = Math.Abs(bc.TractionX) >= Math.Abs(bc.TractionY);
            items.Add(Numeric(bc.DisplayName, CategoryLoad, dominantX ? bc.TractionX : bc.TractionY,
                "MPa", -100000, 100000,
                v => update(p => p with
                {
                    BoundaryConditions =
                    [
                        .. p.BoundaryConditions.Select(b => b.GroupName != groupName
                            ? b
                            : dominantX ? b with { TractionX = v } : b with { TractionY = v }),
                    ],
                })));
        }
    }

    // ================= 材料 =================

    private static void AddMaterialItems(
        List<PropertyItem> items, CaeProjectData project,
        Action<Func<CaeProjectData, CaeProjectData>> update)
    {
        var choice = new ChoicePropertyItem
        {
            Name = "材料",
            Category = CategoryMaterial,
            Choices = new[] { Material.Steel.Name, Material.Aluminum.Name },
            Value = project.Material.Name,
            Description = "定義済み材料からの選択",
        };
        choice.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChoicePropertyItem.Value))
            {
                var material = (string?)choice.Value == Material.Aluminum.Name
                    ? Material.Aluminum : Material.Steel;
                update(p => p with { Material = material });
            }
        };
        items.Add(choice);

        items.Add(new NumericPropertyItem
        {
            Name = "ヤング率 E", Category = CategoryMaterial,
            Value = project.Material.YoungsModulus, Unit = "MPa", Format = "N0", IsReadOnly = true,
        });
        items.Add(new NumericPropertyItem
        {
            Name = "ポアソン比 ν", Category = CategoryMaterial,
            Value = project.Material.PoissonsRatio, Format = "0.00", IsReadOnly = true,
        });
        items.Add(new NumericPropertyItem
        {
            Name = "密度 ρ", Category = CategoryMaterial,
            Value = project.Material.Density, Unit = "t/mm³", Format = "E2", IsReadOnly = true,
        });
    }

    // ================= ソルバ =================

    public const string AnalysisTypeStatic = "静解析";
    public const string AnalysisTypeModal = "固有値解析";

    private static void AddSolverItems(
        List<PropertyItem> items, CaeProjectData project,
        Action<Func<CaeProjectData, CaeProjectData>> update)
    {
        var analysisType = new ChoicePropertyItem
        {
            Name = "解析タイプ",
            Category = CategorySolver,
            Choices = new[] { AnalysisTypeStatic, AnalysisTypeModal },
            Value = project.AnalysisType == AnalysisType.Modal ? AnalysisTypeModal : AnalysisTypeStatic,
            Description = "静解析(CG)または固有値解析(逆反復)",
        };
        analysisType.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChoicePropertyItem.Value))
            {
                var kind = (string?)analysisType.Value == AnalysisTypeModal
                    ? AnalysisType.Modal : AnalysisType.Static;
                update(p => p with { AnalysisType = kind });
            }
        };
        items.Add(analysisType);

        items.Add(Numeric("収束判定値", CategorySolver, project.Solver.Tolerance, null, 1e-14, 1e-2,
            v => update(p => p with { Solver = p.Solver with { Tolerance = v } }), "E1"));
        items.Add(Numeric("最大反復回数", CategorySolver, project.Solver.MaxIterations, null, 100, 1_000_000,
            v => update(p => p with { Solver = p.Solver with { MaxIterations = (int)v } }), "0"));
        items.Add(Numeric("モード数", CategorySolver, project.Solver.ModeCount, null, 1, 12,
            v => update(p => p with { Solver = p.Solver with { ModeCount = (int)v } }), "0"));
    }

    // ================= ヘルパ =================

    private static PlateWithHoleGeometry Plate(CaeProjectData p) => (PlateWithHoleGeometry)p.Geometry;

    private static CantileverPlateGeometry Beam(CaeProjectData p) => (CantileverPlateGeometry)p.Geometry;

    private static NumericPropertyItem Numeric(
        string name, string category, double value, string? unit,
        double minimum, double maximum, Action<double> apply, string format = "0.###")
    {
        var item = new NumericPropertyItem
        {
            Name = name, Category = category, Value = value, Unit = unit,
            Minimum = minimum, Maximum = maximum, Format = format,
        };
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NumericPropertyItem.Value) && item.Value is { } v)
            {
                apply(v);
            }
        };
        return item;
    }
}
