using CaeStudio.Domain.Models;
using CaeStudio.Domain.Solving;
using R3;

namespace CaeStudio.Application;

/// <summary>解析実行の状態。</summary>
public enum AnalysisState
{
    /// <summary>未実行(または結果破棄済み)。</summary>
    Idle,

    /// <summary>実行中。</summary>
    Running,

    /// <summary>正常完了(結果あり)。</summary>
    Completed,

    /// <summary>ユーザによるキャンセル。</summary>
    Cancelled,

    /// <summary>エラー終了。</summary>
    Failed,
}

/// <summary>
/// 解析実行ユースケース。Domain のソルバ(同期・コールバック)をバックグラウンド
/// スレッドで走らせ、状態・残差・結果を R3 ストリームとして公開する。
/// スレッド遷移(UI ディスパッチャへの ObserveOn)は購読側の責務。
/// </summary>
public sealed class AnalysisRunner : IDisposable
{
    private readonly ReactiveProperty<AnalysisState> _state = new(AnalysisState.Idle);
    private readonly ReactiveProperty<StaticResult?> _staticResult = new(null);
    private readonly ReactiveProperty<ModalResult?> _modalResult = new(null);
    private readonly ReactiveProperty<string?> _errorMessage = new(null);
    private readonly Subject<CgIteration> _residuals = new();
    private CancellationTokenSource? _cancellation;

    /// <summary>実行状態。</summary>
    public ReadOnlyReactiveProperty<AnalysisState> State => _state;

    /// <summary>直近の静解析結果(未実行時は null)。</summary>
    public ReadOnlyReactiveProperty<StaticResult?> StaticResult => _staticResult;

    /// <summary>直近の固有値解析結果(未実行時は null)。</summary>
    public ReadOnlyReactiveProperty<ModalResult?> ModalResult => _modalResult;

    /// <summary>エラーメッセージ(Failed 時)。</summary>
    public ReadOnlyReactiveProperty<string?> ErrorMessage => _errorMessage;

    /// <summary>CG 残差ストリーム(ソルバスレッドから発行される)。</summary>
    public Observable<CgIteration> Residuals => _residuals;

    /// <summary>
    /// プロジェクトの解析タイプに応じて静解析または固有値解析を実行する。
    /// 実行中の呼び出しは無視される(コマンド側で CanExecute 制御する前提の防御)。
    /// </summary>
    public Task RunAsync(CaeProjectData project) => project.AnalysisType switch
    {
        AnalysisType.Modal => RunModalAsync(project),
        _ => RunStaticAsync(project),
    };

    /// <summary>静解析を実行する。</summary>
    public Task RunStaticAsync(CaeProjectData project) =>
        RunCoreAsync(project, static (p, onIteration, token) =>
        {
            var result = StaticAnalysis.Run(p, onIteration, token);
            return ((StaticResult?)result, (ModalResult?)null);
        });

    /// <summary>固有値解析を実行する。</summary>
    public Task RunModalAsync(CaeProjectData project) =>
        RunCoreAsync(project, static (p, onIteration, token) =>
        {
            var result = ModalAnalysis.Run(p, onIteration, token);
            return ((StaticResult?)null, (ModalResult?)result);
        });

    private async Task RunCoreAsync(
        CaeProjectData project,
        Func<CaeProjectData, Action<CgIteration>, CancellationToken, (StaticResult?, ModalResult?)> solve)
    {
        if (_state.Value == AnalysisState.Running)
        {
            return;
        }

        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;

        _errorMessage.Value = null;
        _state.Value = AnalysisState.Running;

        try
        {
            var (staticResult, modalResult) = await Task.Run(
                () => solve(project, _residuals.OnNext, token),
                CancellationToken.None).ConfigureAwait(false);

            _staticResult.Value = staticResult;
            _modalResult.Value = modalResult;
            _state.Value = AnalysisState.Completed;
        }
        catch (OperationCanceledException)
        {
            _state.Value = AnalysisState.Cancelled;
        }
        catch (Exception exception)
        {
            _errorMessage.Value = exception.Message;
            _state.Value = AnalysisState.Failed;
        }
    }

    /// <summary>実行中の解析をキャンセルする。</summary>
    public void Cancel() => _cancellation?.Cancel();

    /// <summary>結果を破棄して Idle に戻す(入力変更で結果が古くなったときなど)。</summary>
    public void Invalidate()
    {
        if (_state.Value != AnalysisState.Running)
        {
            _staticResult.Value = null;
            _modalResult.Value = null;
            _state.Value = AnalysisState.Idle;
        }
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _residuals.Dispose();
        _state.Dispose();
        _staticResult.Dispose();
        _modalResult.Dispose();
        _errorMessage.Dispose();
    }
}
