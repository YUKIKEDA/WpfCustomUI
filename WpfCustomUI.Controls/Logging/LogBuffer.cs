using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace WpfCustomUI.Controls;

/// <summary>
/// ログのドキュメントモデル(spec 6.5)。
/// <list type="bullet">
/// <item><see cref="Append(LogEntry)"/> は任意のスレッドから呼べる(内部キューに積むだけ)。</item>
/// <item><see cref="Flush"/> が UI スレッドでキューを <see cref="Entries"/> にまとめて反映し、
/// 最大保持行数(リングバッファ)を超えた古い行を捨てる。LogConsole がタイマーで定期実行する。</item>
/// </list>
/// UI に依存しないため単体テスト可能。
/// </summary>
public sealed class LogBuffer
{
    private readonly ConcurrentQueue<LogEntry> _pending = new();
    private int _clearRequested;

    /// <param name="capacity">最大保持行数。超過分は古い行から捨てられる。</param>
    public LogBuffer(int capacity = 10000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);
        Capacity = capacity;
    }

    public int Capacity { get; }

    /// <summary>反映済みのログ行。UI スレッド(Flush 呼び出し側)からのみ操作される。</summary>
    public ObservableCollection<LogEntry> Entries { get; } = [];

    /// <summary>ログを追加する。任意のスレッドから呼び出し可能。</summary>
    public void Append(LogEntry entry) => _pending.Enqueue(entry);

    /// <summary>現在時刻でログを追加する。任意のスレッドから呼び出し可能。</summary>
    public void Append(LogLevel level, string message) =>
        Append(new LogEntry(DateTime.Now, level, message));

    /// <summary>全ログの消去を要求する。実際の消去は次の <see cref="Flush"/> で行われる。</summary>
    public void Clear()
    {
        Interlocked.Exchange(ref _clearRequested, 1);
        while (_pending.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// 保留中のログを <see cref="Entries"/> に反映する。UI スレッドから呼ぶこと。
    /// </summary>
    /// <returns>変更があったか(自動スクロール判定などに使える)。</returns>
    public bool Flush()
    {
        var changed = false;
        if (Interlocked.Exchange(ref _clearRequested, 0) == 1 && Entries.Count > 0)
        {
            Entries.Clear();
            changed = true;
        }

        if (_pending.IsEmpty)
        {
            return changed;
        }

        var incoming = new List<LogEntry>();
        while (_pending.TryDequeue(out var entry))
        {
            incoming.Add(entry);
        }

        if (incoming.Count >= Capacity)
        {
            // 追加分だけで容量を超える場合は総入れ替えの方が速い
            Entries.Clear();
            for (var i = incoming.Count - Capacity; i < incoming.Count; i++)
            {
                Entries.Add(incoming[i]);
            }
        }
        else
        {
            foreach (var entry in incoming)
            {
                Entries.Add(entry);
            }

            while (Entries.Count > Capacity)
            {
                Entries.RemoveAt(0);
            }
        }

        return true;
    }
}
