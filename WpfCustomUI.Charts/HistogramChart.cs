using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace WpfCustomUI.Charts;

/// <summary>
/// 応力分布などの度数分布を表示するヒストグラム(spec 6.14.3)。
/// <see cref="Values"/> に数値列を渡すと、指定ビン数で自動集計して描画する。
/// </summary>
[TemplatePart(Name = PartPlot, Type = typeof(WcuPlot))]
public class HistogramChart : Control
{
    private const string PartPlot = "PART_Plot";

    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(
            nameof(Values), typeof(IEnumerable), typeof(HistogramChart),
            new PropertyMetadata(null, OnPlotPropertyChanged));

    public static readonly DependencyProperty BinCountProperty =
        DependencyProperty.Register(
            nameof(BinCount), typeof(int), typeof(HistogramChart),
            new PropertyMetadata(20, OnPlotPropertyChanged));

    public static readonly DependencyProperty NormalizeProperty =
        DependencyProperty.Register(
            nameof(Normalize), typeof(bool), typeof(HistogramChart),
            new PropertyMetadata(false, OnPlotPropertyChanged));

    public static readonly DependencyProperty XLabelProperty =
        DependencyProperty.Register(
            nameof(XLabel), typeof(string), typeof(HistogramChart),
            new PropertyMetadata("", OnPlotPropertyChanged));

    private WcuPlot? _plot;

    static HistogramChart()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(HistogramChart), new FrameworkPropertyMetadata(typeof(HistogramChart)));
    }

    /// <summary>集計対象の数値列(double へ変換可能な要素)。</summary>
    public IEnumerable? Values
    {
        get => (IEnumerable?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>ビン数(既定 20)。</summary>
    public int BinCount
    {
        get => (int)GetValue(BinCountProperty);
        set => SetValue(BinCountProperty, value);
    }

    /// <summary>true で確率密度に正規化する(既定 false = 度数)。</summary>
    public bool Normalize
    {
        get => (bool)GetValue(NormalizeProperty);
        set => SetValue(NormalizeProperty, value);
    }

    /// <summary>横軸ラベル。</summary>
    public string XLabel
    {
        get => (string)GetValue(XLabelProperty);
        set => SetValue(XLabelProperty, value);
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

    private static void OnPlotPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((HistogramChart)d).Rebuild();

    private void OnThemeApplied(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        if (_plot is null)
        {
            return;
        }

        var plot = _plot.Plot;
        plot.Clear();

        var values = CollectValues();
        var binCount = Math.Max(1, BinCount);

        if (values.Length > 0)
        {
            var min = values.Min();
            var max = values.Max();

            // 全値が同一の場合も 1 本のバーとして表示できるよう幅を確保する
            var span = max - min;
            if (span <= 0)
            {
                span = Math.Abs(min) > 0 ? Math.Abs(min) * 0.1 : 1;
                min -= span / 2;
                max += span / 2;
            }

            var binWidth = span / binCount;
            var counts = new double[binCount];

            foreach (var value in values)
            {
                var index = (int)((value - min) / binWidth);
                if (index >= binCount)
                {
                    index = binCount - 1; // max ちょうどの値は最後のビンに含める
                }
                else if (index < 0)
                {
                    index = 0;
                }

                counts[index]++;
            }

            if (Normalize)
            {
                var total = values.Length * binWidth;
                for (var i = 0; i < counts.Length; i++)
                {
                    counts[i] /= total;
                }
            }

            var centers = new double[binCount];
            for (var i = 0; i < binCount; i++)
            {
                centers[i] = min + binWidth * (i + 0.5);
            }

            var bars = plot.Add.Bars(centers, counts);
            var accent = ChartHelpers.GetTokenColor("Wcu.Color.Accent.Default", "#007ACC");

            foreach (var bar in bars.Bars)
            {
                bar.Size = binWidth;
                bar.FillColor = accent.WithOpacity(.75);
                bar.LineColor = accent;
                bar.LineWidth = 1;
            }
        }

        plot.XLabel(XLabel);
        plot.YLabel(Normalize ? "Density" : "Count");
        plot.Axes.AutoScale();
        plot.Axes.Margins(bottom: 0);
        _plot.Refresh();
    }

    private double[] CollectValues()
    {
        if (Values is null)
        {
            return [];
        }

        var list = new List<double>();
        foreach (var item in Values)
        {
            if (item is null)
            {
                continue;
            }

            var value = Convert.ToDouble(item, System.Globalization.CultureInfo.InvariantCulture);
            if (!double.IsNaN(value) && !double.IsInfinity(value))
            {
                list.Add(value);
            }
        }

        return [.. list];
    }
}
