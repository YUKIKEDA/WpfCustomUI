using System.Windows;

namespace WpfCustomUI.Charts;

/// <summary>チャート複合コントロール間で共有する内部ユーティリティ。</summary>
internal static class ChartHelpers
{
    /// <summary>
    /// 対数スケール表示用のティックジェネレータを作成する
    /// (データは log10 変換済みで、目盛りラベルに 10^n を表示する方式)。
    /// </summary>
    public static ScottPlot.TickGenerators.NumericAutomatic CreateLogTickGenerator() => new()
    {
        MinorTickGenerator = new ScottPlot.TickGenerators.LogMinorTickGenerator(),
        IntegerTicksOnly = true,
        LabelFormatter = static y => FormatPowerOfTen(y),
    };

    /// <summary>log10 値の目盛りラベルを "1E-6" / "100" のような表記にする。</summary>
    public static string FormatPowerOfTen(double log10Value)
    {
        var exponent = (int)Math.Round(log10Value);
        return exponent is >= -3 and <= 4
            ? Math.Pow(10, exponent).ToString("0.###")
            : $"1E{exponent}";
    }

    /// <summary>ゼロ・負値を避けつつ log10 変換する(残差 0 は最小値として扱う)。</summary>
    public static double SafeLog10(double value) =>
        Math.Log10(Math.Max(value, 1e-300));

    /// <summary>Wcu トークンの Color リソースを取得する(見つからなければ fallback)。</summary>
    public static ScottPlot.Color GetTokenColor(string resourceKey, string fallbackHex) =>
        Application.Current?.TryFindResource(resourceKey) is System.Windows.Media.Color color
            ? WcuChartTheme.ToScottPlotColor(color)
            : ScottPlot.Color.FromHex(fallbackHex);
}
