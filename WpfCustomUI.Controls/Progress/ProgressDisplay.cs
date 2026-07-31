using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace WpfCustomUI.Controls;

/// <summary>
/// 単一ジョブの進捗表示複合コントロール(spec 6.6)。
/// 進捗バー+ステータステキスト+経過時間+キャンセルボタン(ICommand)で構成。
/// 経過時間は <see cref="IsRunning"/> を true にすると内部ストップウォッチで自動計測する。
/// 複数ジョブを並べる場合は本コントロールを ItemsControl で並べる(ジョブキュー UI はアプリの領分)。
/// </summary>
public class ProgressDisplay : Control
{
    private readonly Stopwatch _stopwatch = new();
    private readonly DispatcherTimer _timer;

    static ProgressDisplay()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ProgressDisplay), new FrameworkPropertyMetadata(typeof(ProgressDisplay)));
    }

    public ProgressDisplay()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => UpdateElapsedText();
        Loaded += (_, _) =>
        {
            if (IsRunning)
            {
                _timer.Start();
            }
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(ProgressDisplay), new PropertyMetadata(string.Empty));

    /// <summary>ステータステキスト(例: "メッシュ生成中...")。</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(ProgressDisplay),
        new PropertyMetadata(0.0, OnProgressChanged));

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(ProgressDisplay),
        new PropertyMetadata(100.0, OnProgressChanged));

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ProgressDisplay),
        new PropertyMetadata(0.0, OnProgressChanged));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty IsIndeterminateProperty = DependencyProperty.Register(
        nameof(IsIndeterminate), typeof(bool), typeof(ProgressDisplay),
        new PropertyMetadata(false, OnProgressChanged));

    /// <summary>不確定モード(進捗率が算出できないジョブ)。パーセント表示は消える。</summary>
    public bool IsIndeterminate
    {
        get => (bool)GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    public static readonly DependencyProperty IsRunningProperty = DependencyProperty.Register(
        nameof(IsRunning), typeof(bool), typeof(ProgressDisplay),
        new PropertyMetadata(false, OnIsRunningChanged));

    /// <summary>true にすると経過時間の計測を開始し、false で停止する(最終値は表示に残る)。</summary>
    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    public static readonly DependencyProperty CancelCommandProperty = DependencyProperty.Register(
        nameof(CancelCommand), typeof(ICommand), typeof(ProgressDisplay), new PropertyMetadata(null));

    /// <summary>キャンセルボタンのコマンド。null ならボタン非表示。</summary>
    public ICommand? CancelCommand
    {
        get => (ICommand?)GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    public static readonly DependencyProperty CancelCommandParameterProperty = DependencyProperty.Register(
        nameof(CancelCommandParameter), typeof(object), typeof(ProgressDisplay), new PropertyMetadata(null));

    public object? CancelCommandParameter
    {
        get => GetValue(CancelCommandParameterProperty);
        set => SetValue(CancelCommandParameterProperty, value);
    }

    // 内蔵文字列は差し替え可能にする方針(spec 5)
    public static readonly DependencyProperty CancelTextProperty = DependencyProperty.Register(
        nameof(CancelText), typeof(string), typeof(ProgressDisplay), new PropertyMetadata("Cancel"));

    /// <summary>キャンセルボタンの表示文字列(差し替え可能)。</summary>
    public string CancelText
    {
        get => (string)GetValue(CancelTextProperty);
        set => SetValue(CancelTextProperty, value);
    }

    private static readonly DependencyPropertyKey PercentTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(PercentText), typeof(string), typeof(ProgressDisplay), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PercentTextProperty = PercentTextPropertyKey.DependencyProperty;

    /// <summary>進捗率の表示文字列(読み取り専用。不確定モード時は空)。</summary>
    public string PercentText => (string)GetValue(PercentTextProperty);

    private static readonly DependencyPropertyKey ElapsedTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ElapsedText), typeof(string), typeof(ProgressDisplay), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ElapsedTextProperty = ElapsedTextPropertyKey.DependencyProperty;

    /// <summary>経過時間の表示文字列(読み取り専用。計測開始前は空)。</summary>
    public string ElapsedText => (string)GetValue(ElapsedTextProperty);

    /// <summary>現在の経過時間。</summary>
    public TimeSpan Elapsed => _stopwatch.Elapsed;

    private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ProgressDisplay)d).UpdatePercentText();

    private static void OnIsRunningChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var display = (ProgressDisplay)d;
        if ((bool)e.NewValue)
        {
            display._stopwatch.Restart();
            display._timer.Start();
        }
        else
        {
            display._stopwatch.Stop();
            display._timer.Stop();
        }

        display.UpdateElapsedText();
    }

    private void UpdatePercentText()
    {
        var range = Maximum - Minimum;
        var text = IsIndeterminate || range <= 0
            ? string.Empty
            : Math.Clamp((Value - Minimum) / range, 0.0, 1.0).ToString("P0", CultureInfo.CurrentCulture);
        SetValue(PercentTextPropertyKey, text);
    }

    private void UpdateElapsedText()
    {
        var elapsed = _stopwatch.Elapsed;
        var text = elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
            : $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        SetValue(ElapsedTextPropertyKey, text);
    }
}
