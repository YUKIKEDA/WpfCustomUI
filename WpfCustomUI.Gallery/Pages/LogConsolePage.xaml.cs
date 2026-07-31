using System.Windows;
using System.Windows.Controls;
using WpfCustomUI.Controls;

namespace WpfCustomUI.Gallery.Pages
{
    public partial class LogConsolePage : UserControl
    {
        private readonly LogBuffer _buffer = new(capacity: 5000);
        private CancellationTokenSource? _cts;

        public LogConsolePage()
        {
            InitializeComponent();
            Console.Source = _buffer;
            Progress.CancelCommand = new RelayCommand(() => _cts?.Cancel());
            _buffer.Append(LogLevel.Info, "ログコンソール初期化完了");
        }

        private async void OnStart(object sender, RoutedEventArgs e)
        {
            if (_cts is not null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            StartButton.IsEnabled = false;
            Progress.Value = 0;
            Progress.Text = "ソルバー実行中...";
            Progress.IsRunning = true;
            _buffer.Append(LogLevel.Info, "解析開始: 静解析 (Case 1)");

            var progress = new Progress<double>(v => Progress.Value = v);
            try
            {
                await Task.Run(() => RunSolverSimulation(progress, token), token);
                _buffer.Append(LogLevel.Info, "解析が正常に完了しました");
                Progress.Text = "完了";
                Progress.Value = 100;
            }
            catch (OperationCanceledException)
            {
                _buffer.Append(LogLevel.Error, "ユーザー操作により解析がキャンセルされました");
                Progress.Text = "キャンセル済み";
            }
            finally
            {
                Progress.IsRunning = false;
                _cts.Dispose();
                _cts = null;
                StartButton.IsEnabled = true;
            }
        }

        /// <summary>バックグラウンドスレッドで走るソルバー風のログ・進捗生成。</summary>
        private void RunSolverSimulation(IProgress<double> progress, CancellationToken token)
        {
            const int iterations = 200;
            for (var i = 1; i <= iterations; i++)
            {
                token.ThrowIfCancellationRequested();
                Thread.Sleep(25);

                var residual = Math.Exp(-i / 25.0) * (1.0 + 0.1 * Math.Sin(i));
                _buffer.Append(LogLevel.Debug, $"iteration {i,4}: residual = {residual:E3}");

                if (i % 50 == 0)
                {
                    _buffer.Append(LogLevel.Info, $"チェックポイント {i}/{iterations} を保存しました");
                }

                if (i == 120)
                {
                    _buffer.Append(LogLevel.Warning, "収束速度が低下しています(緩和係数を調整)");
                }

                progress.Report(100.0 * i / iterations);
            }
        }

        private void OnBurst(object sender, RoutedEventArgs e)
        {
            // 複数スレッドから同時に Append してスレッド安全性とリングバッファを確認する
            _ = Task.Run(() =>
            {
                Parallel.For(0, 10000, i =>
                    _buffer.Append(LogLevel.Debug, $"バースト出力 {i:D5} (thread {Environment.CurrentManagedThreadId})"));
                _buffer.Append(LogLevel.Info, "バースト完了: 10,000 行を出力しました");
            });
        }

        private void OnClear(object sender, RoutedEventArgs e) => _buffer.Clear();
    }
}
