using CaeStudio.Domain.Models;
using CaeStudio.Domain.Solving;
using R3;

namespace CaeStudio.Application;

/// <summary>外部計算資源へ投入したジョブの状態(spec 6.27.4)。</summary>
public enum JobState
{
    /// <summary>キュー待ち。</summary>
    Queued,

    /// <summary>実行スロットを確保して計算中。</summary>
    Running,

    /// <summary>正常完了(結果取得可)。</summary>
    Completed,

    /// <summary>ユーザによるキャンセル。</summary>
    Cancelled,

    /// <summary>失敗(稀な模擬障害など)。</summary>
    Failed,
}

/// <summary>ジョブの公開スナップショット(UI / テスト向けの不変ビュー)。</summary>
public sealed record JobInfo(
    Guid Id,
    string Name,
    AnalysisType AnalysisType,
    JobState State,
    double Progress,
    DateTimeOffset SubmittedAt,
    TimeSpan Elapsed,
    string? ErrorMessage,
    CaeProjectData Snapshot);

/// <summary>完了ジョブから取り出す解析結果+投入時スナップショット。</summary>
public sealed record JobResult(
    CaeProjectData Snapshot,
    StaticResult? StaticResult,
    ModalResult? ModalResult);

/// <summary>
/// 外部計算資源(スパコン/クラスタ)へのジョブ投入ポート(spec 6.27.4)。
/// 非同期投入 / 状態観測 / 結果取得 / キャンセル。実装は Infrastructure に置き、
/// 本番では SSH/REST クライアントへ差し替え可能にする。
/// </summary>
public interface IJobClient : IDisposable
{
    /// <summary>現在のジョブ一覧(追加・状態更新のたびに全件スナップショットを発行)。</summary>
    ReadOnlyReactiveProperty<IReadOnlyList<JobInfo>> Jobs { get; }

    /// <summary>
    /// 現在の入力をスナップショットしてジョブを投入する。
    /// 戻り値はジョブ ID(キュー投入時点で確定)。
    /// </summary>
    Task<Guid> SubmitAsync(CaeProjectData project, CancellationToken cancellationToken = default);

    /// <summary>キュー待ちまたは実行中のジョブをキャンセルする。</summary>
    Task CancelAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>完了ジョブの結果を取得する。未完了・失敗・未知 ID は null。</summary>
    Task<JobResult?> TryGetResultAsync(Guid jobId, CancellationToken cancellationToken = default);
}
