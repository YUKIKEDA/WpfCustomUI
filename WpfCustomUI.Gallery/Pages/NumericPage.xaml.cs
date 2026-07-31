using System.Windows.Controls;
using WpfCustomUI.Controls;

namespace WpfCustomUI.Gallery.Pages
{
    public partial class NumericPage : UserControl
    {
        public NumericPage()
        {
            InitializeComponent();
            LengthBox.UnitProvider = new LengthUnitProvider();
        }
    }

    /// <summary>内部値 m、表示 mm の長さ単位プロバイダー(デモ用)。</summary>
    public class LengthUnitProvider : IUnitProvider
    {
        private static readonly Dictionary<string, double> FactorsToMeter = new(StringComparer.OrdinalIgnoreCase)
        {
            ["mm"] = 1e-3,
            ["cm"] = 1e-2,
            ["m"] = 1.0,
            ["µm"] = 1e-6,
            ["um"] = 1e-6,
        };

        public string DisplayUnit => "mm";

        public double ToDisplay(double baseValue) => baseValue * 1000;

        public double FromDisplay(double displayValue) => displayValue / 1000;

        public bool TryConvertFrom(double value, string unitSymbol, out double baseValue)
        {
            if (FactorsToMeter.TryGetValue(unitSymbol, out var factor))
            {
                baseValue = value * factor;
                return true;
            }

            baseValue = 0;
            return false;
        }
    }
}
