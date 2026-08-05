using CaeStudio.Application;
using CaeStudio.Domain.Models;
using CaeStudio.Domain.Solving;
using R3;

namespace CaeStudio.Tests.Application;

/// <summary>
/// 解析実行ユースケースの状態遷移・キャンセル・残差ストリームの検証(spec 6.26.7)。
/// UI(WPF)なしで Application 層が検証できることの確認を兼ねる。
/// </summary>
public class AnalysisRunnerTests
{
    private static CaeProjectData SmallProject() =>
        ProjectTemplates.CreatePlateWithHole() with
        {
            Geometry = new PlateWithHoleGeometry { RadialDivisions = 8, AngularDivisions = 32 },
        };

    [Fact]
    public async Task RunStatic_TransitionsIdleToRunningToCompleted()
    {
        using var runner = new AnalysisRunner();
        var states = new List<AnalysisState>();
        using var subscription = runner.State.Subscribe(states.Add);

        Assert.Equal(AnalysisState.Idle, runner.State.CurrentValue);

        await runner.RunStaticAsync(SmallProject());

        Assert.Equal(AnalysisState.Completed, runner.State.CurrentValue);
        Assert.Equal([AnalysisState.Idle, AnalysisState.Running, AnalysisState.Completed], states);
        Assert.NotNull(runner.StaticResult.CurrentValue);
        Assert.True(runner.StaticResult.CurrentValue!.Converged);
    }

    [Fact]
    public async Task RunStatic_PublishesResidualStream()
    {
        using var runner = new AnalysisRunner();
        var residuals = new List<CgIteration>();
        using var subscription = runner.Residuals.Subscribe(residuals.Add);

        await runner.RunStaticAsync(SmallProject());

        Assert.NotEmpty(residuals);
        // 反復番号は単調増加し、最終残差は収束判定値を下回る
        Assert.True(residuals.Select(r => r.Iteration).SequenceEqual(
            Enumerable.Range(1, residuals.Count)));
        Assert.True(residuals[^1].RelativeResidual < 1e-8);
    }

    [Fact]
    public async Task Cancel_DuringRun_TransitionsToCancelled()
    {
        using var runner = new AnalysisRunner();

        // 大きめの問題+残差ストリームでキャンセルを仕掛ける(最初の反復で即時)
        var project = ProjectTemplates.CreatePlateWithHole() with
        {
            Geometry = new PlateWithHoleGeometry { RadialDivisions = 48, AngularDivisions = 192 },
        };
        using var subscription = runner.Residuals.Take(1).Subscribe(_ => runner.Cancel());

        await runner.RunStaticAsync(project);

        Assert.Equal(AnalysisState.Cancelled, runner.State.CurrentValue);
        Assert.Null(runner.StaticResult.CurrentValue);
    }

    [Fact]
    public async Task RunStatic_InvalidInput_TransitionsToFailed()
    {
        using var runner = new AnalysisRunner();
        var project = SmallProject() with
        {
            Geometry = new PlateWithHoleGeometry { HoleDiameter = 1000.0 }, // 孔 > 板
        };

        await runner.RunStaticAsync(project);

        Assert.Equal(AnalysisState.Failed, runner.State.CurrentValue);
        Assert.False(string.IsNullOrEmpty(runner.ErrorMessage.CurrentValue));
    }

    [Fact]
    public async Task Invalidate_AfterCompleted_DiscardsResult()
    {
        using var runner = new AnalysisRunner();
        await runner.RunStaticAsync(SmallProject());
        Assert.NotNull(runner.StaticResult.CurrentValue);

        runner.Invalidate();

        Assert.Equal(AnalysisState.Idle, runner.State.CurrentValue);
        Assert.Null(runner.StaticResult.CurrentValue);
    }
}

/// <summary>プロジェクトストア(単一情報源+ダーティ管理)の検証。</summary>
public class ProjectStoreTests
{
    [Fact]
    public void Update_ChangesCurrentAndSetsDirty()
    {
        using var store = new ProjectStore(ProjectTemplates.CreatePlateWithHole());
        Assert.False(store.IsDirty.CurrentValue);

        store.Update(p => p with { Name = "変更後" });

        Assert.Equal("変更後", store.Current.CurrentValue.Name);
        Assert.True(store.IsDirty.CurrentValue);
    }

    [Fact]
    public void Replace_ResetsDirtyAndFilePath()
    {
        using var store = new ProjectStore(ProjectTemplates.CreatePlateWithHole());
        store.Update(p => p with { Name = "編集" });

        store.Replace(ProjectTemplates.CreateCantileverPlate(), filePath: @"C:\temp\beam.wcuproj");

        Assert.False(store.IsDirty.CurrentValue);
        Assert.Equal(@"C:\temp\beam.wcuproj", store.FilePath.CurrentValue);
        Assert.Equal("片持ち板", store.Current.CurrentValue.Name);
    }

    [Fact]
    public void MarkSaved_ClearsDirty()
    {
        using var store = new ProjectStore(ProjectTemplates.CreatePlateWithHole());
        store.Update(p => p with { Name = "編集" });

        store.MarkSaved(@"C:\temp\plate.wcuproj");

        Assert.False(store.IsDirty.CurrentValue);
        Assert.Equal(@"C:\temp\plate.wcuproj", store.FilePath.CurrentValue);
    }
}
