namespace WpfCustomUI.Viewport3D;

/// <summary>
/// 非同期ジオメトリ構築の進捗通知(spec 6.24.2)。
/// <see cref="WcuViewport.GeometryBuildProgressChanged"/> の引数。UI スレッドで発火する。
/// </summary>
public sealed class ViewportBuildProgressEventArgs(string stage, double progress) : EventArgs
{
    /// <summary>現在の段階名(例:「ジオメトリ構築 (1/2)」)。</summary>
    public string Stage { get; } = stage;

    /// <summary>全体進捗(0〜1)。</summary>
    public double Progress { get; } = progress;
}
