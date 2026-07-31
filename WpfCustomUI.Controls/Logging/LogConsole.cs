using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WpfCustomUI.Controls;

/// <summary>
/// ログコンソール(spec 6.5)。<see cref="LogBuffer"/> を Source に設定して使う。
/// <list type="bullet">
/// <item>タイマーによるバッチ反映(既定 100ms)で秒間数千行の出力に耐える。</item>
/// <item>自動スクロールは「最下部にいる時だけ追従、上にスクロールしたら停止」。</item>
/// <item>複数行選択+Ctrl+C でタイムスタンプ・レベル付きテキストをコピー。</item>
/// </list>
/// </summary>
[TemplatePart(Name = PartList, Type = typeof(ListBox))]
public class LogConsole : Control
{
    private const string PartList = "PART_List";

    private readonly DispatcherTimer _timer;
    private ListBox? _list;
    private ScrollViewer? _scrollViewer;

    static LogConsole()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(LogConsole), new FrameworkPropertyMetadata(typeof(LogConsole)));
    }

    public LogConsole()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FlushInterval };
        _timer.Tick += OnTimerTick;
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();

        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Copy,
            (_, _) => CopySelection(),
            (_, e) => e.CanExecute = _list?.SelectedItems.Count > 0));
    }

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(LogBuffer), typeof(LogConsole), new PropertyMetadata(null));

    /// <summary>表示するログバッファ。</summary>
    public LogBuffer? Source
    {
        get => (LogBuffer?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public static readonly DependencyProperty FlushIntervalProperty = DependencyProperty.Register(
        nameof(FlushInterval), typeof(TimeSpan), typeof(LogConsole),
        new PropertyMetadata(TimeSpan.FromMilliseconds(100), OnFlushIntervalChanged));

    /// <summary>バッチ反映の間隔。</summary>
    public TimeSpan FlushInterval
    {
        get => (TimeSpan)GetValue(FlushIntervalProperty);
        set => SetValue(FlushIntervalProperty, value);
    }

    public static readonly DependencyProperty ShowTimestampsProperty = DependencyProperty.Register(
        nameof(ShowTimestamps), typeof(bool), typeof(LogConsole), new PropertyMetadata(true));

    /// <summary>タイムスタンプ列を表示するか。</summary>
    public bool ShowTimestamps
    {
        get => (bool)GetValue(ShowTimestampsProperty);
        set => SetValue(ShowTimestampsProperty, value);
    }

    private static void OnFlushIntervalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((LogConsole)d)._timer.Interval = (TimeSpan)e.NewValue;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _list = GetTemplateChild(PartList) as ListBox;
        _scrollViewer = null;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        var source = Source;
        if (source is null)
        {
            return;
        }

        var follow = IsAtBottom();
        if (source.Flush() && follow)
        {
            _list?.UpdateLayout();
            _scrollViewer?.ScrollToEnd();
        }
    }

    private bool IsAtBottom()
    {
        _scrollViewer ??= _list is null ? null : FindDescendant<ScrollViewer>(_list);
        return _scrollViewer is null || _scrollViewer.ScrollableHeight - _scrollViewer.VerticalOffset < 1.0;
    }

    private void CopySelection()
    {
        if (_list is null)
        {
            return;
        }

        var builder = new StringBuilder();
        foreach (var entry in _list.SelectedItems.Cast<LogEntry>().OrderBy(e => e.Timestamp))
        {
            builder.AppendLine($"{entry.Timestamp:HH:mm:ss.fff} [{entry.Level}] {entry.Message}");
        }

        try
        {
            Clipboard.SetText(builder.ToString());
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // クリップボードが他プロセスに掴まれている場合は諦める
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found)
            {
                return found;
            }

            if (FindDescendant<T>(child) is T descendant)
            {
                return descendant;
            }
        }

        return null;
    }
}
