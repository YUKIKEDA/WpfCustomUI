using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace WpfCustomUI.Charts;

/// <summary>
/// ソルバー残差のストリーミング表示チャート(spec 6.14.3)。
/// <para>
/// <see cref="ConvergenceSeries"/> のコレクションを <see cref="SeriesSource"/> に
/// バインドし、ワーカースレッドから <see cref="ConvergenceSeries.Append"/> を
/// 呼ぶだけでよい。再描画は内部タイマーで約 100ms にスロットリングされる。
/// </para>
/// </summary>
[TemplatePart(Name = PartPlot, Type = typeof(WcuPlot))]
public class ConvergenceMonitor : Control
{
    private const string PartPlot = "PART_Plot";

    public static readonly DependencyProperty SeriesSourceProperty =
        DependencyProperty.Register(
            nameof(SeriesSource), typeof(IEnumerable), typeof(ConvergenceMonitor),
            new PropertyMetadata(null, OnSeriesSourceChanged));

    public static readonly DependencyProperty ThresholdProperty =
        DependencyProperty.Register(
            nameof(Threshold), typeof(double), typeof(ConvergenceMonitor),
            new PropertyMetadata(double.NaN, OnPlotPropertyChanged));

    public static readonly DependencyProperty IsLogScaleProperty =
        DependencyProperty.Register(
            nameof(IsLogScale), typeof(bool), typeof(ConvergenceMonitor),
            new PropertyMetadata(true, OnPlotPropertyChanged));

    public static readonly DependencyProperty XLabelProperty =
        DependencyProperty.Register(
            nameof(XLabel), typeof(string), typeof(ConvergenceMonitor),
            new PropertyMetadata("Iteration", OnPlotPropertyChanged));

    public static readonly DependencyProperty YLabelProperty =
        DependencyProperty.Register(
            nameof(YLabel), typeof(string), typeof(ConvergenceMonitor),
            new PropertyMetadata("Residual", OnPlotPropertyChanged));

    private readonly DispatcherTimer _timer;
    private WcuPlot? _plot;
    private volatile bool _dirty;

    static ConvergenceMonitor()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ConvergenceMonitor), new FrameworkPropertyMetadata(typeof(ConvergenceMonitor)));
    }

    public ConvergenceMonitor()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _timer.Tick += OnTimerTick;

        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    /// <summary><see cref="ConvergenceSeries"/> のコレクション。</summary>
    public IEnumerable? SeriesSource
    {
        get => (IEnumerable?)GetValue(SeriesSourceProperty);
        set => SetValue(SeriesSourceProperty, value);
    }

    /// <summary>収束判定値。NaN(既定)の場合は基準線を表示しない。</summary>
    public double Threshold
    {
        get => (double)GetValue(ThresholdProperty);
        set => SetValue(ThresholdProperty, value);
    }

    /// <summary>縦軸を対数スケールにするか(既定 true)。</summary>
    public bool IsLogScale
    {
        get => (bool)GetValue(IsLogScaleProperty);
        set => SetValue(IsLogScaleProperty, value);
    }

    /// <summary>横軸ラベル(既定 "Iteration")。</summary>
    public string XLabel
    {
        get => (string)GetValue(XLabelProperty);
        set => SetValue(XLabelProperty, value);
    }

    /// <summary>縦軸ラベル(既定 "Residual")。</summary>
    public string YLabel
    {
        get => (string)GetValue(YLabelProperty);
        set => SetValue(YLabelProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_plot is not null)
        {
            _plot.ThemeApplied -= OnThemeApplied;
        }

        _plot = GetTemplateChild(PartPlot) as WcuPlot;

        if (_plot is not null)
        {
            _plot.ThemeApplied += OnThemeApplied;
            Rebuild();
        }
    }

    private static void OnSeriesSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var monitor = (ConvergenceMonitor)d;

        if (e.OldValue is INotifyCollectionChanged oldIncc)
        {
            oldIncc.CollectionChanged -= monitor.OnCollectionChanged;
        }

        monitor.DetachSeriesHandlers(e.OldValue as IEnumerable);

        if (e.NewValue is INotifyCollectionChanged newIncc)
        {
            newIncc.CollectionChanged += monitor.OnCollectionChanged;
        }

        monitor.AttachSeriesHandlers(e.NewValue as IEnumerable);
        monitor.Rebuild();
    }

    private static void OnPlotPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ConvergenceMonitor)d).Rebuild();

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DetachSeriesHandlers(SeriesSource);
        AttachSeriesHandlers(SeriesSource);
        Rebuild();
    }

    private void AttachSeriesHandlers(IEnumerable? source)
    {
        if (source is null)
        {
            return;
        }

        foreach (var item in source)
        {
            if (item is ConvergenceSeries series)
            {
                series.Changed += OnSeriesChanged;
            }
        }
    }

    private void DetachSeriesHandlers(IEnumerable? source)
    {
        if (source is null)
        {
            return;
        }

        foreach (var item in source)
        {
            if (item is ConvergenceSeries series)
            {
                series.Changed -= OnSeriesChanged;
            }
        }
    }

    // ワーカースレッドから呼ばれ得るため、フラグを立てるだけにする
    private void OnSeriesChanged(object? sender, EventArgs e) => _dirty = true;

    private void OnThemeApplied(object? sender, EventArgs e) => Rebuild();

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_dirty)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        _dirty = false;

        if (_plot is null)
        {
            return;
        }

        var plot = _plot.Plot;
        plot.Clear();

        var hasNamedSeries = false;

        if (SeriesSource is not null)
        {
            foreach (var item in SeriesSource)
            {
                if (item is not ConvergenceSeries series)
                {
                    continue;
                }

                var values = series.Snapshot();
                if (values.Length == 0)
                {
                    continue;
                }

                var xs = new double[values.Length];
                var ys = new double[values.Length];
                for (var i = 0; i < values.Length; i++)
                {
                    xs[i] = i + 1;
                    ys[i] = IsLogScale ? ChartHelpers.SafeLog10(values[i]) : values[i];
                }

                var line = plot.Add.ScatterLine(xs, ys);
                line.LineWidth = 2;

                if (series.Color is { } color)
                {
                    line.Color = WcuChartTheme.ToScottPlotColor(color);
                }

                if (!string.IsNullOrEmpty(series.Name))
                {
                    line.LegendText = series.Name;
                    hasNamedSeries = true;
                }
            }
        }

        if (!double.IsNaN(Threshold))
        {
            var y = IsLogScale ? ChartHelpers.SafeLog10(Threshold) : Threshold;
            var line = plot.Add.HorizontalLine(y);
            line.LinePattern = ScottPlot.LinePattern.Dashed;
            line.LineWidth = 1.5f;
            line.Color = ChartHelpers.GetTokenColor("Wcu.Color.Warning", "#CCA700");
        }

        plot.Axes.Left.TickGenerator = IsLogScale
            ? ChartHelpers.CreateLogTickGenerator()
            : new ScottPlot.TickGenerators.NumericAutomatic();

        plot.Legend.IsVisible = hasNamedSeries;
        plot.XLabel(XLabel);
        plot.YLabel(YLabel);
        plot.Axes.AutoScale();
        _plot.Refresh();
    }
}
