using CaeStudio.Domain.Models;
using R3;
using System.Globalization;

namespace CaeStudio.App.ViewModels;

/// <summary>
/// 新規解析ウィザード(テンプレート選択→パラメータ→確認)の VM。
/// 入力は R3 の BindableReactiveProperty、確認サマリと完了可否は入力ストリームの合成で導出する。
/// </summary>
public sealed class NewProjectWizardViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public NewProjectWizardViewModel()
    {
        var plateDefaults = new PlateWithHoleGeometry();
        var beamDefaults = new CantileverPlateGeometry();

        PlateWidth = Register(new BindableReactiveProperty<double?>(plateDefaults.Width));
        PlateHeight = Register(new BindableReactiveProperty<double?>(plateDefaults.Height));
        HoleDiameter = Register(new BindableReactiveProperty<double?>(plateDefaults.HoleDiameter));
        Tension = Register(new BindableReactiveProperty<double?>(100.0));
        RadialDivisions = Register(new BindableReactiveProperty<double?>(plateDefaults.RadialDivisions));
        AngularDivisions = Register(new BindableReactiveProperty<double?>(plateDefaults.AngularDivisions));

        BeamLength = Register(new BindableReactiveProperty<double?>(beamDefaults.Length));
        BeamHeight = Register(new BindableReactiveProperty<double?>(beamDefaults.Height));
        TipShear = Register(new BindableReactiveProperty<double?>(10.0));
        BeamDivisionsX = Register(new BindableReactiveProperty<double?>(beamDefaults.DivisionsX));
        BeamDivisionsY = Register(new BindableReactiveProperty<double?>(beamDefaults.DivisionsY));

        Thickness = Register(new BindableReactiveProperty<double?>(plateDefaults.Thickness));
        ProjectName = Register(new BindableReactiveProperty<string>("円孔付き平板の引張"));
        IsPlate = Register(new BindableReactiveProperty<bool>(true));
        IsBeam = Register(new BindableReactiveProperty<bool>(false));
        IsModalAnalysis = Register(new BindableReactiveProperty<bool>(false));
        SelectedMaterial = Register(new BindableReactiveProperty<Material>(Material.Steel));
        Summary = Register(new BindableReactiveProperty<string>(""));
        CanFinish = Register(new BindableReactiveProperty<bool>(true));

        // テンプレート切替で既定のプロジェクト名を追従(ユーザが編集済みなら触らない)
        var nameIsDefault = true;
        Register(ProjectName.Skip(1).Subscribe(_ => nameIsDefault = false));
        Register(IsPlate.Skip(1).Subscribe(isPlate =>
        {
            if (nameIsDefault)
            {
                ProjectName.Value = isPlate ? "円孔付き平板の引張" : "片持ち板の曲げ";
                nameIsDefault = true; // 上の Subscribe で false にされるのを戻す
            }
        }));

        // 入力のどれかが変わるたびにサマリ+完了可否を再計算
        var inputs = new Observable<Unit>[]
        {
            IsPlate.AsUnitObservable(), ProjectName.AsUnitObservable(),
            SelectedMaterial.AsUnitObservable(), Thickness.AsUnitObservable(),
            PlateWidth.AsUnitObservable(), PlateHeight.AsUnitObservable(),
            HoleDiameter.AsUnitObservable(), Tension.AsUnitObservable(),
            RadialDivisions.AsUnitObservable(), AngularDivisions.AsUnitObservable(),
            BeamLength.AsUnitObservable(), BeamHeight.AsUnitObservable(),
            TipShear.AsUnitObservable(), BeamDivisionsX.AsUnitObservable(), BeamDivisionsY.AsUnitObservable(),
            IsModalAnalysis.AsUnitObservable(),
        };
        Register(inputs.Merge().Subscribe(_ => Recalculate()));
        Recalculate();
    }

    // ---- ステップ 1: テンプレート ----
    public BindableReactiveProperty<bool> IsPlate { get; }

    public BindableReactiveProperty<bool> IsBeam { get; }

    /// <summary>解析タイプ: false = 静解析 / true = 固有値解析。</summary>
    public BindableReactiveProperty<bool> IsModalAnalysis { get; }

    // ---- ステップ 2: パラメータ ----
    public BindableReactiveProperty<string> ProjectName { get; }

    public IReadOnlyList<Material> Materials { get; } = [Material.Steel, Material.Aluminum];

    public BindableReactiveProperty<Material> SelectedMaterial { get; }

    public BindableReactiveProperty<double?> Thickness { get; }

    public BindableReactiveProperty<double?> PlateWidth { get; }

    public BindableReactiveProperty<double?> PlateHeight { get; }

    public BindableReactiveProperty<double?> HoleDiameter { get; }

    public BindableReactiveProperty<double?> Tension { get; }

    public BindableReactiveProperty<double?> RadialDivisions { get; }

    public BindableReactiveProperty<double?> AngularDivisions { get; }

    public BindableReactiveProperty<double?> BeamLength { get; }

    public BindableReactiveProperty<double?> BeamHeight { get; }

    public BindableReactiveProperty<double?> TipShear { get; }

    public BindableReactiveProperty<double?> BeamDivisionsX { get; }

    public BindableReactiveProperty<double?> BeamDivisionsY { get; }

    // ---- ステップ 3: 確認 ----
    public BindableReactiveProperty<string> Summary { get; }

    public BindableReactiveProperty<bool> CanFinish { get; }

    /// <summary>ウィザードの入力からプロジェクト(入力一式の不変レコード)を構築する。</summary>
    public CaeProjectData BuildProject()
    {
        var project = IsPlate.Value
            ? ProjectTemplates.CreatePlateWithHole(Tension.Value ?? 100.0) with
            {
                Geometry = new PlateWithHoleGeometry
                {
                    Width = PlateWidth.Value ?? 240.0,
                    Height = PlateHeight.Value ?? 160.0,
                    HoleDiameter = HoleDiameter.Value ?? 20.0,
                    RadialDivisions = (int)(RadialDivisions.Value ?? 24),
                    AngularDivisions = (int)(AngularDivisions.Value ?? 96),
                    Thickness = Thickness.Value ?? 5.0,
                },
            }
            : ProjectTemplates.CreateCantileverPlate(AnalysisType.Static, TipShear.Value ?? 10.0) with
            {
                Geometry = new CantileverPlateGeometry
                {
                    Length = BeamLength.Value ?? 200.0,
                    Height = BeamHeight.Value ?? 20.0,
                    DivisionsX = (int)(BeamDivisionsX.Value ?? 80),
                    DivisionsY = (int)(BeamDivisionsY.Value ?? 8),
                    Thickness = Thickness.Value ?? 5.0,
                },
            };

        return project with
        {
            Name = ProjectName.Value,
            Material = SelectedMaterial.Value,
            AnalysisType = IsModalAnalysis.Value ? AnalysisType.Modal : AnalysisType.Static,
        };
    }

    private void Recalculate()
    {
        double?[] required = IsPlate.Value
            ? [PlateWidth.Value, PlateHeight.Value, HoleDiameter.Value, Tension.Value,
               RadialDivisions.Value, AngularDivisions.Value, Thickness.Value]
            : [BeamLength.Value, BeamHeight.Value, TipShear.Value,
               BeamDivisionsX.Value, BeamDivisionsY.Value, Thickness.Value];

        var valid = !string.IsNullOrWhiteSpace(ProjectName.Value) && required.All(v => v is > 0.0);
        if (IsPlate.Value && valid)
        {
            valid = HoleDiameter.Value < Math.Min(PlateWidth.Value ?? 0, PlateHeight.Value ?? 0);
        }

        CanFinish.Value = valid;
        if (!valid)
        {
            Summary.Value = "入力に不備があります。パラメータを見直してください。";
        }
        else if (IsPlate.Value)
        {
            var analysis = IsModalAnalysis.Value ? "固有値解析" : "静解析";
            Summary.Value = string.Create(CultureInfo.InvariantCulture,
                $"""
                 プロジェクト: {ProjectName.Value}
                 テンプレート: 円孔付き平板の一軸引張({analysis})
                 板: {PlateWidth.Value:0.#} × {PlateHeight.Value:0.#} × t{Thickness.Value:0.#} mm / 孔 φ{HoleDiameter.Value:0.#} mm
                 引張応力: {Tension.Value:0.#} MPa(左右辺)
                 メッシュ: 半径 {RadialDivisions.Value:0} × 周 {AngularDivisions.Value:0} 分割
                 材料: {SelectedMaterial.Value.Name}(E = {SelectedMaterial.Value.YoungsModulus:N0} MPa)
                 """);
        }
        else
        {
            var analysis = IsModalAnalysis.Value ? "固有値解析" : "静解析";
            Summary.Value = string.Create(CultureInfo.InvariantCulture,
                $"""
                 プロジェクト: {ProjectName.Value}
                 テンプレート: 片持ち板の曲げ({analysis})
                 梁: L{BeamLength.Value:0.#} × H{BeamHeight.Value:0.#} × t{Thickness.Value:0.#} mm
                 先端せん断: {TipShear.Value:0.#} MPa
                 メッシュ: {BeamDivisionsX.Value:0} × {BeamDivisionsY.Value:0} 分割
                 材料: {SelectedMaterial.Value.Name}(E = {SelectedMaterial.Value.YoungsModulus:N0} MPa)
                 """);
        }
    }

    private T Register<T>(T disposable) where T : IDisposable
    {
        _disposables.Add(disposable);
        return disposable;
    }

    public void Dispose() => _disposables.Dispose();
}
