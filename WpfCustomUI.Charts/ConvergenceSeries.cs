namespace WpfCustomUI.Charts;

/// <summary>
/// <see cref="ConvergenceMonitor"/> に流し込む残差系列(spec 6.14.3)。
/// <para>
/// <see cref="Append"/> / <see cref="Clear"/> はスレッドセーフで、ソルバーの
/// ワーカースレッドから直接呼び出せる。表示側はスロットリングされた
/// タイマーで <see cref="Snapshot"/> を取得して再描画する。
/// </para>
/// </summary>
public class ConvergenceSeries
{
    private readonly Lock _sync = new();
    private readonly List<double> _values = [];

    public ConvergenceSeries(string name = "")
    {
        Name = name;
    }

    /// <summary>凡例に表示するシリーズ名。</summary>
    public string Name { get; set; }

    /// <summary>線色。null の場合はテーマパレットから自動割当される。</summary>
    public System.Windows.Media.Color? Color { get; set; }

    /// <summary>現在の反復数。</summary>
    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _values.Count;
            }
        }
    }

    /// <summary>値が追加・クリアされたときに発火する(呼び出し元スレッドのまま)。</summary>
    public event EventHandler? Changed;

    /// <summary>残差値を末尾に追加する。任意のスレッドから呼び出せる。</summary>
    public void Append(double residual)
    {
        lock (_sync)
        {
            _values.Add(residual);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>系列を空にする。任意のスレッドから呼び出せる。</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _values.Clear();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>現在の値のコピーを取得する。</summary>
    public double[] Snapshot()
    {
        lock (_sync)
        {
            return [.. _values];
        }
    }
}
