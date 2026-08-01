using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace WpfCustomUI.Charts;

/// <summary>
/// 周波数応答(ボード線図)表示チャート(spec 6.14.3)。
/// 上段に振幅、下段に位相を表示し、横軸(対数周波数)は 2 段で同期する。
/// <see cref="FrequencyResponseSeries"/> のコレクションを
/// <see cref="SeriesSource"/> にバインドして使う。
/// </summary>
[TemplatePart(Name = PartMagnitudePlot, Type = typeof(WcuPlot))]
[TemplatePart(Name = PartPhasePlot, Type = typeof(WcuPlot))]
public class FrequencyResponsePlot : Control
{
    private const string PartMagnitudePlot = "PART_MagnitudePlot";
    private const string PartPhasePlot = "PART_PhasePlot";

    public static readonly DependencyProperty SeriesSourceProperty =
        DependencyProperty.Register(
            nameof(SeriesSource), typeof(IEnumerable), typeof(FrequencyResponsePlot),
            new PropertyMetadata(null, OnSeriesSourceChanged));

    public static readonly DependencyProperty ShowPhaseProperty =
        DependencyProperty.Register(
            nameof(ShowPhase), typeof(bool), typeof(FrequencyResponsePlot),
            new PropertyMetadata(true, OnPlotPropertyChanged));

    public static readonly DependencyProperty MagnitudeInDecibelsProperty =
        DependencyProperty.Register(
            nameof(MagnitudeInDecibels), typeof(bool), typeof(FrequencyResponsePlot),
            new PropertyMetadata(true, OnPlotPropertyChanged));

    public static readonly DependencyProperty XLabelProperty =
        DependencyProperty.Register(
            nameof(XLabel), typeof(string), typeof(FrequencyResponsePlot),
            new PropertyMetadata("Frequency [Hz]", OnPlotPropertyChanged));

    private WcuPlot? _magnitudePlot;
    private WcuPlot? _phasePlot;
    private bool _syncingAxes;

    static FrequencyResponsePlot()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(FrequencyResponsePlot), new FrameworkPropertyMetadata(typeof(FrequencyResponsePlot)));
    }

    /// <summary><see cref="FrequencyResponseSeries"/> のコレクション。</summary>
    public IEnumerable? SeriesSource
    {
        get => (IEnumerable?)GetValue(SeriesSourceProperty);
        set => SetValue(SeriesSourceProperty, value);
    }

    /// <summary>位相パネルを表示するか(既定 true)。</summary>
    public bool ShowPhase
    {
        get => (bool)GetValue(ShowPhaseProperty);
        set => SetValue(ShowPhaseProperty, value);
    }

    /// <summary>振幅を dB(20 log10)で表示するか(既定 true。false でリニア表示)。</summary>
    public bool MagnitudeInDecibels
    {
        get => (bool)GetValue(MagnitudeInDecibelsProperty);
        set => SetValue(MagnitudeInDecibelsProperty, value);
    }

    /// <summary>横軸ラベル(既定 "Frequency [Hz]")。</summary>
    public string XLabel
    {
        get => (string)GetValue(XLabelProperty);
        set => SetValue(XLabelProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_magnitudePlot is not null)
        {
            _magnitudePlot.ThemeApplied -= OnThemeApplied;
        }

        _magnitudePlot = GetTemplateChild(PartMagnitudePlot) as WcuPlot;
        _phasePlot = GetTemplateChild(PartPhasePlot) as WcuPlot;

        if (_magnitudePlot is not null)
        {
            _magnitudePlot.ThemeApplied += OnThemeApplied;
        }

        if (_magnitudePlot is not null && _phasePlot is not null)
        {
            // ズーム・パン時に上下の X 軸範囲を同期する
            _magnitudePlot.Plot.RenderManager.AxisLimitsChanged +=
                (_, _) => SyncAxes(_magnitudePlot, _phasePlot);
            _phasePlot.Plot.RenderManager.AxisLimitsChanged +=
                (_, _) => SyncAxes(_phasePlot, _magnitudePlot);
        }

        Rebuild();
    }

    private void SyncAxes(WcuPlot source, WcuPlot target)
    {
        if (_syncingAxes || !ShowPhase)
        {
            return;
        }

        var sourceLimits = source.Plot.Axes.GetLimits();
        var targetLimits = target.Plot.Axes.GetLimits();

        if (sourceLimits.Left == targetLimits.Left && sourceLimits.Right == targetLimits.Right)
        {
            return;
        }

        _syncingAxes = true;
        try
        {
            target.Plot.Axes.SetLimitsX(sourceLimits.Left, sourceLimits.Right);
            target.Refresh();
        }
        finally
        {
            _syncingAxes = false;
        }
    }

    private static void OnSeriesSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (FrequencyResponsePlot)d;

        if (e.OldValue is INotifyCollectionChanged oldIncc)
        {
            oldIncc.CollectionChanged -= chart.OnCollectionChanged;
        }

        chart.DetachSeriesHandlers(e.OldValue as IEnumerable);

        if (e.NewValue is INotifyCollectionChanged newIncc)
        {
            newIncc.CollectionChanged += chart.OnCollectionChanged;
        }

        chart.AttachSeriesHandlers(e.NewValue as IEnumerable);
        chart.Rebuild();
    }

    private static void OnPlotPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((FrequencyResponsePlot)d).Rebuild();

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
            if (item is INotifyPropertyChanged inpc)
            {
                inpc.PropertyChanged += OnSeriesPropertyChanged;
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
            if (item is INotifyPropertyChanged inpc)
            {
                inpc.PropertyChanged -= OnSeriesPropertyChanged;
            }
        }
    }

    private void OnSeriesPropertyChanged(object? sender, PropertyChangedEventArgs e) => Rebuild();

    private void OnThemeApplied(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        if (_magnitudePlot is null)
        {
            return;
        }

        var magnitudePlot = _magnitudePlot.Plot;
        var phasePlot = _phasePlot?.Plot;

        magnitudePlot.Clear();
        phasePlot?.Clear();

        var hasNamedSeries = false;

        if (SeriesSource is not null)
        {
            var palette = WcuChartTheme.GetSeriesPalette();
            var paletteIndex = 0;

            foreach (var item in SeriesSource)
            {
                if (item is not FrequencyResponseSeries series ||
                    series.Frequencies is null || series.Magnitudes is null ||
                    series.Frequencies.Length == 0)
                {
                    continue;
                }

                var color = series.Color is { } c
                    ? WcuChartTheme.ToScottPlotColor(c)
                    : palette[paletteIndex % palette.Length];
                paletteIndex++;

                var logFreqs = new double[series.Frequencies.Length];
                for (var i = 0; i < logFreqs.Length; i++)
                {
                    logFreqs[i] = ChartHelpers.SafeLog10(series.Frequencies[i]);
                }

                var magnitudes = new double[series.Magnitudes.Length];
                for (var i = 0; i < magnitudes.Length; i++)
                {
                    magnitudes[i] = MagnitudeInDecibels
                        ? 20 * ChartHelpers.SafeLog10(series.Magnitudes[i])
                        : series.Magnitudes[i];
                }

                var magnitudeLine = magnitudePlot.Add.ScatterLine(logFreqs, magnitudes);
                magnitudeLine.LineWidth = 2;
                magnitudeLine.Color = color;

                if (!string.IsNullOrEmpty(series.Name))
                {
                    magnitudeLine.LegendText = series.Name;
                    hasNamedSeries = true;
                }

                if (phasePlot is not null && series.Phases is not null &&
                    series.Phases.Length == logFreqs.Length)
                {
                    var phaseLine = phasePlot.Add.ScatterLine(logFreqs, series.Phases);
                    phaseLine.LineWidth = 2;
                    phaseLine.Color = color;
                }
            }
        }

        var showPhase = ShowPhase && phasePlot is not null;

        ConfigureAxes(magnitudePlot, showXLabel: !showPhase);
        magnitudePlot.YLabel(MagnitudeInDecibels ? "Magnitude [dB]" : "Magnitude");
        magnitudePlot.Legend.IsVisible = hasNamedSeries;
        magnitudePlot.Axes.AutoScale();
        _magnitudePlot.Refresh();

        if (phasePlot is not null)
        {
            ConfigureAxes(phasePlot, showXLabel: true);
            phasePlot.YLabel("Phase [deg]");
            phasePlot.Axes.AutoScale();

            if (showPhase)
            {
                // 初期表示から X 範囲を揃える
                var limits = magnitudePlot.Axes.GetLimits();
                phasePlot.Axes.SetLimitsX(limits.Left, limits.Right);
            }

            _phasePlot!.Refresh();
        }
    }

    private void ConfigureAxes(ScottPlot.Plot plot, bool showXLabel)
    {
        plot.Axes.Bottom.TickGenerator = ChartHelpers.CreateLogTickGenerator();
        plot.XLabel(showXLabel ? XLabel : "");
    }
}
