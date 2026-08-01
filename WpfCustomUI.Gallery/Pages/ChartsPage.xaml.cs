using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfCustomUI.Charts;

namespace WpfCustomUI.Gallery.Pages
{
    public partial class ChartsPage : UserControl
    {
        private readonly ConvergenceSeries _dispResidual = new("Displacement");
        private readonly ConvergenceSeries _forceResidual = new("Force");
        private CancellationTokenSource? _solveCts;

        public ChartsPage()
        {
            InitializeComponent();

            Convergence.SeriesSource = new[] { _dispResidual, _forceResidual };
            History.SeriesSource = CreateHistorySeries();
            Frf.SeriesSource = CreateFrfSeries();
            Histogram.Values = CreateStressSamples();
            SetupFreePlot();

            Unloaded += (_, _) => _solveCts?.Cancel();
        }

        // ---- ConvergenceMonitor: ソルバー実行のシミュレーション ----

        private async void OnSolveClick(object sender, RoutedEventArgs e)
        {
            _solveCts?.Cancel();
            var cts = new CancellationTokenSource();
            _solveCts = cts;

            _dispResidual.Clear();
            _forceResidual.Clear();
            SolveButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            SolveStatus.Text = "解析中...";

            var converged = false;
            try
            {
                // ワーカースレッドから ConvergenceSeries.Append を直接呼ぶ
                converged = await Task.Run(() => RunFakeSolver(cts.Token), cts.Token);
            }
            catch (OperationCanceledException)
            {
            }

            if (_solveCts == cts)
            {
                SolveButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                SolveStatus.Text = converged ? "収束しました" : "停止しました";
            }
        }

        private void OnStopClick(object sender, RoutedEventArgs e) => _solveCts?.Cancel();

        private bool RunFakeSolver(CancellationToken token)
        {
            var random = new Random();
            var disp = 1.0;
            var force = 5.0;

            for (var i = 0; i < 400; i++)
            {
                token.ThrowIfCancellationRequested();

                // ニュートン法風の収束カーブ + ノイズ、途中で荷重ステップ切替(残差ジャンプ)
                var rate = i is > 120 and < 130 ? 1.4 : 0.94;
                disp = disp * rate * (0.85 + random.NextDouble() * 0.3);
                force = force * rate * (0.85 + random.NextDouble() * 0.3);

                _dispResidual.Append(disp);
                _forceResidual.Append(force);

                if (disp < 1e-7 && force < 1e-7)
                {
                    return true;
                }

                Thread.Sleep(25);
            }

            return false;
        }

        // ---- HistoryChart: 荷重-変位曲線 ----

        private static ObservableCollection<ChartSeries> CreateHistorySeries()
        {
            const int n = 120;
            var x1 = new double[n];
            var y1 = new double[n];
            var x2 = new double[n];
            var y2 = new double[n];

            for (var i = 0; i < n; i++)
            {
                var t = i / (double)(n - 1);

                // 弾塑性風の載荷曲線(実測)と弾性解(比較)
                var d = t * 12.0;
                x1[i] = d;
                y1[i] = 48 * (1 - Math.Exp(-d / 3.0)) + 1.2 * d + Math.Sin(i * 0.9) * 0.4;

                x2[i] = d;
                y2[i] = 6.5 * d;
            }

            return
            [
                new ChartSeries { Name = "実験値", X = x1, Y = y1 },
                new ChartSeries { Name = "弾性解", X = x2, Y = y2, LineWidth = 1.5, Color = Color.FromRgb(0xCC, 0xA7, 0x00) },
            ];
        }

        // ---- FrequencyResponsePlot: 2自由度系の FRF ----

        private static ObservableCollection<FrequencyResponseSeries> CreateFrfSeries()
        {
            const int n = 400;
            var freqs = new double[n];
            for (var i = 0; i < n; i++)
            {
                // 1 Hz 〜 1000 Hz を対数等間隔
                freqs[i] = Math.Pow(10, 3.0 * i / (n - 1));
            }

            var drivePoint = ComputeFrf(freqs, [(120, 0.02, 1.0), (450, 0.03, 0.6)]);
            var transfer = ComputeFrf(freqs, [(120, 0.02, 0.7), (450, 0.03, -0.5)]);

            return
            [
                new FrequencyResponseSeries
                {
                    Name = "Drive point",
                    Frequencies = freqs,
                    Magnitudes = drivePoint.Magnitudes,
                    Phases = drivePoint.Phases,
                },
                new FrequencyResponseSeries
                {
                    Name = "Transfer",
                    Frequencies = freqs,
                    Magnitudes = transfer.Magnitudes,
                    Phases = transfer.Phases,
                },
            ];
        }

        private static (double[] Magnitudes, double[] Phases) ComputeFrf(
            double[] freqs,
            (double NaturalHz, double DampingRatio, double ModalGain)[] modes)
        {
            var magnitudes = new double[freqs.Length];
            var phases = new double[freqs.Length];

            for (var i = 0; i < freqs.Length; i++)
            {
                double real = 0;
                double imag = 0;

                foreach (var (fn, zeta, gain) in modes)
                {
                    var r = freqs[i] / fn;
                    var denomReal = 1 - r * r;
                    var denomImag = 2 * zeta * r;
                    var denomSq = denomReal * denomReal + denomImag * denomImag;

                    real += gain * denomReal / denomSq;
                    imag += gain * -denomImag / denomSq;
                }

                magnitudes[i] = Math.Sqrt(real * real + imag * imag) * 1e-3;
                phases[i] = Math.Atan2(imag, real) * 180 / Math.PI;
            }

            return (magnitudes, phases);
        }

        // ---- HistogramChart: von Mises 応力分布 ----

        private static double[] CreateStressSamples()
        {
            var random = new Random(42);
            var samples = new double[3000];

            for (var i = 0; i < samples.Length; i++)
            {
                // Box-Muller 正規乱数(平均 180 MPa, 標準偏差 35 MPa)
                var u1 = 1 - random.NextDouble();
                var u2 = random.NextDouble();
                var normal = Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
                samples[i] = Math.Max(0, 180 + normal * 35);
            }

            return samples;
        }

        // ---- 素の WcuPlot: ScottPlot API を直接使う例 ----

        private void SetupFreePlot()
        {
            var plot = FreePlot.Plot;

            var xs = new double[200];
            var raw = new double[200];
            var smoothed = new double[200];
            var random = new Random(7);

            for (var i = 0; i < xs.Length; i++)
            {
                xs[i] = i * 0.05;
                raw[i] = Math.Sin(xs[i] * 2) + Math.Sin(xs[i] * 7) * 0.3 + (random.NextDouble() - 0.5) * 0.4;
            }

            for (var i = 0; i < xs.Length; i++)
            {
                double sum = 0;
                var count = 0;
                for (var j = Math.Max(0, i - 4); j <= Math.Min(xs.Length - 1, i + 4); j++)
                {
                    sum += raw[j];
                    count++;
                }

                smoothed[i] = sum / count;
            }

            var rawLine = plot.Add.ScatterLine(xs, raw);
            rawLine.LegendText = "センサ生値";
            rawLine.LineWidth = 1;

            var smoothLine = plot.Add.ScatterLine(xs, smoothed);
            smoothLine.LegendText = "移動平均";
            smoothLine.LineWidth = 2.5f;

            plot.XLabel("Time [s]");
            plot.YLabel("Acceleration [m/s²]");
            plot.Legend.IsVisible = true;
            plot.Axes.AutoScale();
            FreePlot.Refresh();
        }
    }
}
