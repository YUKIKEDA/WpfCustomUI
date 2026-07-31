namespace WpfCustomUI.Controls;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>ログ1行分のデータ(spec 6.5)。</summary>
public readonly record struct LogEntry(DateTime Timestamp, LogLevel Level, string Message);
