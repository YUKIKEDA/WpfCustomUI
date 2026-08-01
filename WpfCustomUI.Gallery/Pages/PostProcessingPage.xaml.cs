using System.Windows;
using System.Windows.Controls;
using WpfCustomUI.Controls;

namespace WpfCustomUI.Gallery.Pages
{
    public partial class PostProcessingPage : UserControl
    {
        private const double ModeFrequencyHz = 45.2;

        public PostProcessingPage()
        {
            InitializeComponent();

            ScaleEditor.Scale = EditorLegend.Scale = new ColorScale
            {
                ColorMap = ColorMap.Jet,
                Minimum = 0,
                Maximum = 350,
                LevelCount = 10,
            };

            ModeCanvas.SizeChanged += (_, _) => DrawModeShape(ModePlayback.CurrentFrame);
            Loaded += (_, _) => DrawModeShape(ModePlayback.CurrentFrame);
        }

        private void ModePlayback_CurrentFrameChanged(object sender, RoutedPropertyChangedEventArgs<int> e)
        {
            DrawModeShape(e.NewValue);
        }

        /// <summary>はりの1次モード形状(半波正弦)を、フレームに応じた振幅係数で描画する。</summary>
        private void DrawModeShape(int frame)
        {
            var width = ModeCanvas.ActualWidth;
            var height = ModeCanvas.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            var frameCount = ModePlayback.FrameCount;
            var phase = frameCount > 0 ? 2.0 * Math.PI * frame / frameCount : 0.0;
            var amplitude = Math.Sin(phase);

            ModePlayback.FrameLabel = $"位相 {phase * 180.0 / Math.PI:F0}°";

            var midY = height / 2.0;
            BaseLine.X1 = 0;
            BaseLine.X2 = width;
            BaseLine.Y1 = BaseLine.Y2 = midY;

            var points = new System.Windows.Media.PointCollection();
            const int segments = 48;
            for (var i = 0; i <= segments; i++)
            {
                var x = (double)i / segments;
                var y = amplitude * Math.Sin(Math.PI * x) * (height * 0.42);
                points.Add(new Point(x * width, midY - y));
            }

            points.Freeze();
            ModeShape.Points = points;
        }

        private void SpeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModePlayback is null)
            {
                return;
            }

            ModePlayback.FramesPerSecond = SpeedCombo.SelectedIndex switch
            {
                0 => 15,
                2 => 60,
                _ => 30,
            };
        }
    }
}
