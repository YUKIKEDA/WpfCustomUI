using CaeStudio.Application;
using CaeStudio.Domain.Models;
using CaeStudio.Infrastructure;
using R3;

namespace CaeStudio.Tests.Infrastructure;

/// <summary>
/// SimulatedHpcClient の状態遷移・キャンセル・決定論的失敗・結果取得(spec 6.27.4 / 6.27.5)。
/// キュー遅延は実時間で短くして決定論性を保つ(TimeProvider 差し替え口は実装側に用意)。
/// </summary>
public class SimulatedHpcClientTests
{
    private static CaeProjectData SmallProject() =>
        ProjectTemplates.CreatePlateWithHole() with
        {
            Geometry = new PlateWithHoleGeometry { RadialDivisions = 6, AngularDivisions = 24 },
        };

    private static SimulatedHpcClient CreateClient(int failEveryNth = 0) =>
        new(maxSlots: 1, queueDelay: TimeSpan.FromMilliseconds(30), failEveryNth: failEveryNth);

    [Fact]
    public async Task Submit_TransitionsQueuedToRunningToCompleted()
    {
        using var client = CreateClient();
        var states = new List<JobState>();
        using var sub = client.Jobs.Subscribe(jobs =>
        {
            if (jobs.Count > 0)
            {
                states.Add(jobs[0].State);
            }
        });

        var id = await client.SubmitAsync(SmallProject());
        Assert.NotEqual(Guid.Empty, id);

        await WaitForStateAsync(client, id, JobState.Completed, TimeSpan.FromSeconds(60));

        Assert.Contains(JobState.Queued, states);
        Assert.Contains(JobState.Running, states);
        Assert.Equal(JobState.Completed, client.Jobs.CurrentValue.Single(j => j.Id == id).State);
        Assert.Equal(1.0, client.Jobs.CurrentValue.Single(j => j.Id == id).Progress, 3);

        var result = await client.TryGetResultAsync(id);
        Assert.NotNull(result);
        Assert.NotNull(result!.StaticResult);
        Assert.True(result.StaticResult!.Converged);
        Assert.Same(client.Jobs.CurrentValue.Single(j => j.Id == id).Snapshot, result.Snapshot);
    }

    [Fact]
    public async Task Cancel_WhileQueued_TransitionsToCancelled()
    {
        // 長いキュー待ちのままキャンセル
        using var client = new SimulatedHpcClient(
            maxSlots: 1, queueDelay: TimeSpan.FromHours(1), failEveryNth: 0);

        var id = await client.SubmitAsync(SmallProject());
        await WaitForStateAsync(client, id, JobState.Queued, TimeSpan.FromSeconds(2));

        await client.CancelAsync(id);
        await WaitForStateAsync(client, id, JobState.Cancelled, TimeSpan.FromSeconds(5));

        Assert.Null(await client.TryGetResultAsync(id));
    }

    [Fact]
    public async Task FailEveryNth_ProducesDeterministicFailure()
    {
        using var client = CreateClient(failEveryNth: 2);

        var first = await client.SubmitAsync(SmallProject());
        await WaitForStateAsync(client, first, JobState.Completed, TimeSpan.FromSeconds(60));

        var second = await client.SubmitAsync(SmallProject());
        await WaitForStateAsync(client, second, JobState.Failed, TimeSpan.FromSeconds(60));

        var failed = client.Jobs.CurrentValue.Single(j => j.Id == second);
        Assert.Equal(JobState.Failed, failed.State);
        Assert.Contains("模擬ノード障害", failed.ErrorMessage);
        Assert.Null(await client.TryGetResultAsync(second));
    }

    [Fact]
    public void SnapshotMismatch_DetectsEditedProject()
    {
        var original = SmallProject();
        var edited = original with { Name = original.Name + " (edited)" };

        Assert.False(ReferenceEquals(original, edited));
        Assert.True(IsSnapshotMismatch(original, edited));
        Assert.False(IsSnapshotMismatch(original, original));
    }

    /// <summary>MainViewModel と同じ判定(投入スナップショットと現行モデルの参照比較)。</summary>
    private static bool IsSnapshotMismatch(CaeProjectData snapshot, CaeProjectData current) =>
        !ReferenceEquals(snapshot, current);

    private static async Task WaitForStateAsync(
        IJobClient client, Guid id, JobState expected, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var job = client.Jobs.CurrentValue.FirstOrDefault(j => j.Id == id);
            if (job?.State == expected)
            {
                return;
            }

            if (job?.State is JobState.Failed or JobState.Cancelled
                && expected is JobState.Completed or JobState.Running)
            {
                throw new Xunit.Sdk.XunitException($"unexpected terminal state: {job.State}");
            }

            await Task.Delay(20);
        }

        var final = client.Jobs.CurrentValue.FirstOrDefault(j => j.Id == id);
        throw new TimeoutException($"job {id} did not reach {expected} (final={final?.State})");
    }
}
