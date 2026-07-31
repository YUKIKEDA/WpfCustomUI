using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WpfCustomUI.Controls;

/// <summary>
/// カラーマップ凡例(spec 6.4)。<see cref="ColorScale"/> モデルを縦方向に描画する。
/// カラーバーはグラデーションブラシ(離散レベル時は帯分割)、
/// 目盛は正規化位置の等分割点に値ラベルを配置する(対数スケールでも位置は等間隔、値が対数系列になる)。
/// </summary>
[TemplatePart(Name = PartTickHost, Type = typeof(Canvas))]
public class ColorMapLegend : Control
{
    private const string PartTickHost = "PART_TickHost";

    private Canvas? _tickHost;

    static ColorMapLegend()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ColorMapLegend), new FrameworkPropertyMetadata(typeof(ColorMapLegend)));
    }

    public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(
        nameof(Scale), typeof(ColorScale), typeof(ColorMapLegend),
        new PropertyMetadata(null, OnScaleChanged));

    /// <summary>描画対象の値→色変換モデル。プロパティ変更に追従して再描画する。</summary>
    public ColorScale? Scale
    {
        get => (ColorScale?)GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    public static readonly DependencyProperty TickCountProperty = DependencyProperty.Register(
        nameof(TickCount), typeof(int), typeof(ColorMapLegend),
        new PropertyMetadata(5, OnAppearanceChanged), v => (int)v >= 2);

    /// <summary>目盛ラベルの個数(最小 2。両端を含む)。</summary>
    public int TickCount
    {
        get => (int)GetValue(TickCountProperty);
        set => SetValue(TickCountProperty, value);
    }

    public static readonly DependencyProperty LabelFormatProperty = DependencyProperty.Register(
        nameof(LabelFormat), typeof(string), typeof(ColorMapLegend),
        new PropertyMetadata("G4", OnAppearanceChanged));

    /// <summary>目盛ラベルの数値書式。</summary>
    public string LabelFormat
    {
        get => (string)GetValue(LabelFormatProperty);
        set => SetValue(LabelFormatProperty, value);
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ColorMapLegend), new PropertyMetadata(null));

    /// <summary>凡例タイトル(例: "応力 [MPa]")。null なら非表示。</summary>
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    private static readonly DependencyPropertyKey LegendBrushPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(LegendBrush), typeof(Brush), typeof(ColorMapLegend), new PropertyMetadata(null));

    public static readonly DependencyProperty LegendBrushProperty = LegendBrushPropertyKey.DependencyProperty;

    /// <summary>カラーバー描画用ブラシ(テンプレートが使用。読み取り専用)。</summary>
    public Brush? LegendBrush => (Brush?)GetValue(LegendBrushProperty);

    private static void OnScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var legend = (ColorMapLegend)d;
        if (e.OldValue is ColorScale oldScale)
        {
            oldScale.PropertyChanged -= legend.OnScalePropertyChanged;
        }

        if (e.NewValue is ColorScale newScale)
        {
            newScale.PropertyChanged += legend.OnScalePropertyChanged;
        }

        legend.Refresh();
    }

    private static void OnAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ColorMapLegend)d).Refresh();

    private void OnScalePropertyChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    public override void OnApplyTemplate()
    {
        if (_tickHost is not null)
        {
            _tickHost.SizeChanged -= OnTickHostSizeChanged;
        }

        base.OnApplyTemplate();
        _tickHost = GetTemplateChild(PartTickHost) as Canvas;
        if (_tickHost is not null)
        {
            _tickHost.SizeChanged += OnTickHostSizeChanged;
        }

        Refresh();
    }

    private void OnTickHostSizeChanged(object sender, SizeChangedEventArgs e) => RebuildTicks();

    private void Refresh()
    {
        RebuildBrush();
        RebuildTicks();
    }

    private void RebuildBrush()
    {
        var scale = Scale;
        if (scale is null)
        {
            SetValue(LegendBrushPropertyKey, null);
            return;
        }

        // 下端が t=0 になる縦グラデーション
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 1),
            EndPoint = new Point(0, 0),
        };

        if (scale.LevelCount is int levels and > 0)
        {
            for (var i = 0; i < levels; i++)
            {
                var color = scale.Sample((i + 0.5) / levels);
                brush.GradientStops.Add(new GradientStop(color, (double)i / levels));
                brush.GradientStops.Add(new GradientStop(color, (double)(i + 1) / levels));
            }
        }
        else
        {
            foreach (var (offset, color) in scale.ColorMap.Stops)
            {
                brush.GradientStops.Add(new GradientStop(color, offset));
            }
        }

        brush.Freeze();
        SetValue(LegendBrushPropertyKey, brush);
    }

    private void RebuildTicks()
    {
        if (_tickHost is null)
        {
            return;
        }

        _tickHost.Children.Clear();

        var scale = Scale;
        var height = _tickHost.ActualHeight;
        if (scale is null || height <= 0)
        {
            return;
        }

        var count = TickCount;
        for (var i = 0; i < count; i++)
        {
            var t = 1.0 - (double)i / (count - 1); // 上端が t=1
            var y = (1.0 - t) * height;
            var value = scale.Denormalize(t);

            var tickLine = new Rectangle { Width = 5, Height = 1 };
            tickLine.SetResourceReference(Shape.FillProperty, "Wcu.Brush.Border.Strong");
            Canvas.SetLeft(tickLine, 0);
            Canvas.SetTop(tickLine, Math.Clamp(y - 0.5, 0, height - 1));
            _tickHost.Children.Add(tickLine);

            var label = new TextBlock
            {
                Text = value.ToString(LabelFormat, CultureInfo.CurrentCulture),
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, 8);
            Canvas.SetTop(label, Math.Clamp(
                y - label.DesiredSize.Height / 2, 0, Math.Max(0, height - label.DesiredSize.Height)));
            _tickHost.Children.Add(label);
        }
    }
}
