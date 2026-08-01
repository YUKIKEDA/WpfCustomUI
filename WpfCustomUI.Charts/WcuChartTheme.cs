using System.Windows;

namespace WpfCustomUI.Charts;

/// <summary>
/// ScottPlot の Plot に Wcu デザイントークンの配色を適用する静的ヘルパー(spec 6.14.2)。
/// <para>
/// ScottPlot はラスタ描画のため DynamicResource が使えない。そこで適用時点の
/// トークン値(Application リソースの Wcu.Color.*)を読み取って設定する。
/// テーマ・アクセント変更への追従は <see cref="WcuPlot"/> が
/// <c>ThemeManager.ThemeChanged</c> を購読して再適用することで実現する。
/// </para>
/// </summary>
public static class WcuChartTheme
{
    /// <summary>
    /// シリーズの既定色パレット(先頭はアクセント現在値)。
    /// ダーク背景で判別しやすい色を選んである。
    /// </summary>
    public static ScottPlot.Color[] GetSeriesPalette() =>
    [
        GetColor("Wcu.Color.Accent.Default", "#007ACC"),
        FromHex("#89D185"), // green
        FromHex("#CCA700"), // yellow
        FromHex("#F14C4C"), // red
        FromHex("#75BEFF"), // light blue
        FromHex("#B180D7"), // purple
        FromHex("#4EC9B0"), // teal
        FromHex("#CE9178"), // orange
    ];

    /// <summary>
    /// Plot 全体(背景・軸・グリッド・凡例・シリーズパレット)へ Wcu 配色を適用する。
    /// 素の WpfPlot や画像出力用の Plot にも手動で適用できる。
    /// </summary>
    public static void Apply(ScottPlot.Plot plot)
    {
        ArgumentNullException.ThrowIfNull(plot);

        var text = GetColor("Wcu.Color.Gray.300", "#C5C5C5");
        var border = GetColor("Wcu.Color.Gray.700", "#3F3F46");

        plot.FigureBackground.Color = GetColor("Wcu.Color.Gray.900", "#1E1E1E");
        plot.DataBackground.Color = GetColor("Wcu.Color.Gray.850", "#252526");

        plot.Axes.Color(text);
        plot.Grid.MajorLineColor = border.WithOpacity(.5);
        plot.Grid.MinorLineColor = border.WithOpacity(.2);

        plot.Legend.BackgroundColor = GetColor("Wcu.Color.Gray.800", "#2D2D30");
        plot.Legend.FontColor = text;
        plot.Legend.OutlineColor = border;

        plot.Add.Palette = new WcuPalette(GetSeriesPalette());

        // 日本語などの非 ASCII ラベルでも描画できるフォントを自動選択させる
        plot.Font.Automatic();
    }

    /// <summary>WPF の Color(Wcu トークン)を ScottPlot の Color へ変換する。</summary>
    public static ScottPlot.Color ToScottPlotColor(System.Windows.Media.Color color) =>
        new(color.R, color.G, color.B, color.A);

    private static ScottPlot.Color GetColor(string resourceKey, string fallbackHex) =>
        Application.Current?.TryFindResource(resourceKey) is System.Windows.Media.Color color
            ? ToScottPlotColor(color)
            : FromHex(fallbackHex);

    private static ScottPlot.Color FromHex(string hex) => ScottPlot.Color.FromHex(hex);

    /// <summary>Wcu 配色のシリーズパレット。</summary>
    private sealed class WcuPalette(ScottPlot.Color[] colors) : ScottPlot.IPalette
    {
        public ScottPlot.Color[] Colors { get; } = colors;

        public string Name => "Wcu";

        public string Description => "WpfCustomUI design token palette";

        public ScottPlot.Color GetColor(int index) => Colors[index % Colors.Length];
    }
}
