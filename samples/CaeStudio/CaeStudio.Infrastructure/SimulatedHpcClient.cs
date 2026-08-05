using CaeStudio.Application;
using CaeStudio.Domain.Models;
using CaeStudio.Domain.Solving;
using R3;
using System.Collections.Concurrent;

namespace CaeStudio.Infrastructure;

/// <summary>
/// 外部 HPC ジョブ投入のインプロセス模擬(spec 6.27.4)。
/// キュー待ち・実行スロット・進捗率・稀な失敗を人工的に再現し、
/// 計算本体は既存 Domain ソルバに委譲する。決定論的で xUnit 容易。
/// </summary>
public sealed class SimulatedHpcClient : IJobClient
{
    private readonly object _gate = new();
    private readonly List<JobEntry> _jobs = [];
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellations = new();
    private readonly ReactiveProperty<IReadOnlyList<JobInfo>> _jobsProperty =
        new(Array.Empty<JobInfo>());
    private readonly SemaphoreSlim _slots;
    private readonly TimeSpan _queueDelay;
    private readonly int _failEveryNth;
    private readonly TimeProvider _time;
    private int _submitCount;
    private bool _disposed;

    /// <param name="maxSlots">同時実行スロット数(既定 1=混雑を見せやすい)。</param>
    /// <param name="queueDelay">キュー待ちの人工遅延(テストでは短縮)。</param>
    /// <param name="failEveryNth">N 件に 1 件失敗(0 で無効。既定 17≈稀)。</param>
    /// <param name="timeProvider">待機の差し替え口(テスト用)。</param>
    public SimulatedHpcClient(
        int maxSlots = 1,
        TimeSpan? queueDelay = null,
        int failEveryNth = 17,
        TimeProvider? timeProvider = null)
    {
        if (maxSlots < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSlots));
        }

        _slots = new SemaphoreSlim(maxSlots, maxSlots);
        _queueDelay = queueDelay ?? TimeSpan.FromSeconds(1.5);
        _failEveryNth = Math.Max(0, failEveryNth);
        _time = timeProvider ?? TimeProvider.System;
    }

    public ReadOnlyReactiveProperty<IReadOnlyList<JobInfo>> Jobs => _jobsProperty;

    public Task<Guid> SubmitAsync(CaeProjectData project, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var id = Guid.NewGuid();
        var order = Interlocked.Increment(ref _submitCount);
        var cts = new CancellationTokenSource();
        _cancellations[id] = cts;

        var entry = new JobEntry
        {
            Id = id,
            Name = $"{project.Name} #{order}",
            AnalysisType = project.AnalysisType,
            State = JobState.Queued,
            Progress = 0,
            SubmittedAt = _time.GetUtcNow(),
            Snapshot = project,
            Order = order,
        };

        lock (_gate)
        {
            _jobs.Add(entry);
            PublishUnlocked();
        }

        // ファイア・アンド・フォーゲットでワーカーを起動(クライアント寿命に紐づく CTS)
        _ = Task.Run(() => RunJobAsync(entry, cts.Token), CancellationToken.None);
        return Task.FromResult(id);
    }

    public Task CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_cancellations.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
        }

        lock (_gate)
        {
            var entry = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (entry is { State: JobState.Queued or JobState.Running })
            {
                entry.State = JobState.Cancelled;
                entry.Progress = 0;
                PublishUnlocked();
            }
        }

        return Task.CompletedTask;
    }

    public Task<JobResult?> TryGetResultAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var entry = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (entry is not { State: JobState.Completed })
            {
                return Task.FromResult<JobResult?>(null);
            }

            return Task.FromResult<JobResult?>(new JobResult(
                entry.Snapshot, entry.StaticResult, entry.ModalResult));
        }
    }

    private async Task RunJobAsync(JobEntry entry, CancellationToken token)
    {
        try
        {
            // キュー待ち(混雑演出)。R3 の Observable.Delay と衝突しないよう Task 拡張を明示する
            await Task.Delay(_queueDelay, _time, token).ConfigureAwait(false);

            await _slots.WaitAsync(token).ConfigureAwait(false);
            try
            {
                // 稀な失敗を決定論的に挿入(N 件目ごと)
                if (_failEveryNth > 0 && entry.Order % _failEveryNth == 0)
                {
                    Update(entry, e =>
                    {
                        e.State = JobState.Failed;
                        e.ErrorMessage = "模擬ノード障害: 計算ノードが応答しませんでした";
                        e.Progress = 0;
                    });
                    return;
                }

                Update(entry, e =>
                {
                    e.State = JobState.Running;
                    e.Progress = 0.05;
                });

                var (staticResult, modalResult) = await Task.Run(() =>
                {
                    Action<CgIteration> onIteration = iter =>
                    {
                        // 残差低下を 5%→95% の進捗にマップ(収束見込みの簡易近似)
                        var progress = 0.05 + 0.9 * (1.0 - Math.Clamp(
                            Math.Log10(Math.Max(iter.RelativeResidual, 1e-16)) / -8.0, 0, 1));
                        Update(entry, e => e.Progress = Math.Clamp(progress, 0.05, 0.95));
                    };

                    if (entry.AnalysisType == AnalysisType.Modal)
                    {
                        var modal = ModalAnalysis.Run(entry.Snapshot, onIteration, token);
                        return ((StaticResult?)null, (ModalResult?)modal);
                    }

                    var staticResultLocal = StaticAnalysis.Run(entry.Snapshot, onIteration, token);
                    return ((StaticResult?)staticResultLocal, (ModalResult?)null);
                }, CancellationToken.None).ConfigureAwait(false);

                Update(entry, e =>
                {
                    e.StaticResult = staticResult;
                    e.ModalResult = modalResult;
                    e.State = JobState.Completed;
                    e.Progress = 1.0;
                });
            }
            finally
            {
                _slots.Release();
            }
        }
        catch (OperationCanceledException)
        {
            Update(entry, e =>
            {
                if (e.State is not (JobState.Completed or JobState.Failed))
                {
                    e.State = JobState.Cancelled;
                    e.Progress = 0;
                }
            });
        }
        catch (Exception exception)
        {
            Update(entry, e =>
            {
                e.State = JobState.Failed;
                e.ErrorMessage = exception.Message;
                e.Progress = 0;
            });
        }
        finally
        {
            if (_cancellations.TryRemove(entry.Id, out var cts))
            {
                cts.Dispose();
            }
        }
    }

    private void Update(JobEntry entry, Action<JobEntry> mutate)
    {
        lock (_gate)
        {
            mutate(entry);
            PublishUnlocked();
        }
    }

    private void PublishUnlocked()
    {
        var now = _time.GetUtcNow();
        _jobsProperty.Value = _jobs
            .Select(j => new JobInfo(
                j.Id, j.Name, j.AnalysisType, j.State, j.Progress,
                j.SubmittedAt, now - j.SubmittedAt, j.ErrorMessage, j.Snapshot))
            .ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var cts in _cancellations.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _cancellations.Clear();
        _slots.Dispose();
        _jobsProperty.Dispose();
    }

    private sealed class JobEntry
    {
        public required Guid Id { get; init; }
        public required string Name { get; init; }
        public required AnalysisType AnalysisType { get; init; }
        public required CaeProjectData Snapshot { get; init; }
        public required int Order { get; init; }
        public required DateTimeOffset SubmittedAt { get; init; }
        public JobState State { get; set; }
        public double Progress { get; set; }
        public string? ErrorMessage { get; set; }
        public StaticResult? StaticResult { get; set; }
        public ModalResult? ModalResult { get; set; }
    }
}
