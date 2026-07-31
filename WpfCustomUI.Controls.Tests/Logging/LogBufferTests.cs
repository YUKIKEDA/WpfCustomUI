using WpfCustomUI.Controls;
using Xunit;

namespace WpfCustomUI.Controls.Tests.Logging;

public class LogBufferTests
{
    [Fact]
    public void Flush_MovesPendingEntriesInOrder()
    {
        var buffer = new LogBuffer();
        buffer.Append(LogLevel.Info, "one");
        buffer.Append(LogLevel.Warning, "two");

        Assert.Empty(buffer.Entries); // Flush までは反映されない
        var changed = buffer.Flush();

        Assert.True(changed);
        Assert.Equal(["one", "two"], buffer.Entries.Select(e => e.Message));
        Assert.Equal(LogLevel.Warning, buffer.Entries[1].Level);
    }

    [Fact]
    public void Flush_WithoutPending_ReturnsFalse()
    {
        var buffer = new LogBuffer();

        Assert.False(buffer.Flush());
    }

    [Fact]
    public void RingBuffer_DropsOldestEntries()
    {
        var buffer = new LogBuffer(capacity: 3);
        for (var i = 1; i <= 5; i++)
        {
            buffer.Append(LogLevel.Info, $"m{i}");
            buffer.Flush();
        }

        Assert.Equal(["m3", "m4", "m5"], buffer.Entries.Select(e => e.Message));
    }

    [Fact]
    public void RingBuffer_BurstLargerThanCapacity_KeepsOnlyNewest()
    {
        var buffer = new LogBuffer(capacity: 3);
        for (var i = 1; i <= 10; i++)
        {
            buffer.Append(LogLevel.Info, $"m{i}");
        }

        buffer.Flush();

        Assert.Equal(["m8", "m9", "m10"], buffer.Entries.Select(e => e.Message));
    }

    [Fact]
    public void Clear_TakesEffectOnNextFlush()
    {
        var buffer = new LogBuffer();
        buffer.Append(LogLevel.Info, "old");
        buffer.Flush();

        buffer.Clear();
        Assert.Single(buffer.Entries); // Flush までは残っている

        buffer.Flush();
        Assert.Empty(buffer.Entries);
    }

    [Fact]
    public void Clear_AlsoDiscardsPendingEntries()
    {
        var buffer = new LogBuffer();
        buffer.Append(LogLevel.Info, "pending");
        buffer.Clear();
        buffer.Flush();

        Assert.Empty(buffer.Entries);
    }

    [Fact]
    public async Task Append_FromMultipleThreads_LosesNothing()
    {
        const int threads = 4;
        const int perThread = 1000;
        var buffer = new LogBuffer(capacity: threads * perThread);

        await Task.WhenAll(Enumerable.Range(0, threads).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < perThread; i++)
            {
                buffer.Append(LogLevel.Info, $"{t}-{i}");
            }
        })));

        buffer.Flush();

        Assert.Equal(threads * perThread, buffer.Entries.Count);
    }

    [Fact]
    public void Capacity_MustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogBuffer(0));
    }
}
