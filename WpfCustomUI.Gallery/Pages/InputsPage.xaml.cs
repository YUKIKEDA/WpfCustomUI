using System.Windows.Controls;

namespace WpfCustomUI.Gallery.Pages
{
    public partial class InputsPage : UserControl
    {
        public InputsPage()
        {
            InitializeComponent();
            DataContext = new ValidationDemoModel();
        }
    }

    /// <summary>検証エラー表示(赤枠+ツールチップ)のデモ用モデル。</summary>
    public class ValidationDemoModel
    {
        private double _thickness = 3.2;

        public double Thickness
        {
            get => _thickness;
            set
            {
                if (value is < 0 or > 100)
                {
                    throw new ArgumentException("0〜100 の範囲で入力してください。");
                }

                _thickness = value;
            }
        }
    }
}
