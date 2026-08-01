namespace WpfCustomUI.Viewport3D;

/// <summary>
/// 非同期ジオメトリ構築の世代管理(spec 6.24.2)。
/// <para>
/// 構築要求のたびに世代番号を進め、前の世代の CancellationToken をキャンセルする。
/// 完了した構築は <see cref="IsCurrent"/> が true のときだけ反映してよい
/// (古い世代の結果は破棄する)。UI スレッド専用。
/// </para>
/// </summary>
internal sealed class GeometryBuildCoordinator
{
    private CancellationTokenSource? _cts;

    /// <summary>最新の構築世代(0 = 未開始)。</summary>
    public int CurrentGeneration { get; private set; }

    /// <summary>
    /// 新しい構築を開始する。進行中の古い構築はキャンセルされる。
    /// 戻り値の世代番号を完了時の <see cref="IsCurrent"/> 判定に使う。
    /// </summary>
    public (int Generation, CancellationToken Token) Begin()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        return (++CurrentGeneration, _cts.Token);
    }

    /// <summary>指定世代がまだ最新か(false なら結果を破棄する)。</summary>
    public bool IsCurrent(int generation) => generation == CurrentGeneration;

    /// <summary>
    /// 進行中の構築をキャンセルし、以後どの世代も最新でなくする
    /// (アンロード・レンダラー破棄時に呼ぶ)。
    /// </summary>
    public void CancelAll()
    {
        _cts?.Cancel();
        _cts = null;
        CurrentGeneration++;
    }
}
