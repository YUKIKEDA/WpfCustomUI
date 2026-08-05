using CaeStudio.App.ViewModels;
using Microsoft.Xaml.Behaviors;
using System.Windows;
using WpfCustomUI.Charts;

namespace CaeStudio.App.Behaviors;

/// <summary>
/// VM の <see cref="PathPlotData"/> を WcuPlot(ScottPlot)の描画呼び出しへ変換するアダプタ。
/// WcuPlot は即時 API のため、データレコードの差し替えを再描画に写像する。
/// </summary>
public sealed class PathPlotBehavior : Behavior<WcuPlot>
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(PathPlotData), typeof(PathPlotBehavior),
        new PropertyMetadata(null, OnSourceChanged));

    public PathPlotData? Source
    {
        get => (PathPlotData?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        Render();
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PathPlotBehavior)d).Render();

    private void Render()
    {
        if (AssociatedObject is not { } plotControl)
        {
            return;
        }

        var plot = plotControl.Plot;
        plot.Clear();

        if (Source is { } data)
        {
            var exact = plot.Add.ScatterLine(data.ExactX, data.ExactY);
            exact.LegendText = data.ExactName;
            exact.LineWidth = 2f;

            var fem = plot.Add.ScatterPoints(data.FemX, data.FemY);
            fem.LegendText = data.FemName;
            fem.MarkerSize = 6f;

            plot.XLabel(data.XLabel);
            plot.YLabel(data.YLabel);
            plot.Legend.IsVisible = true;
            plot.Axes.AutoScale();
        }

        plotControl.Refresh();
    }
}
