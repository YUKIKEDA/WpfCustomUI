using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WpfCustomUI.Charts;

/// <summary>
/// <see cref="FrequencyResponsePlot"/> に表示する 1 本の周波数応答を表すモデル(spec 6.14.3)。
/// </summary>
public class FrequencyResponseSeries : INotifyPropertyChanged
{
    private string _name = "";
    private double[]? _frequencies;
    private double[]? _magnitudes;
    private double[]? _phases;
    private System.Windows.Media.Color? _color;

    /// <summary>凡例に表示するシリーズ名。</summary>
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    /// <summary>周波数 [Hz]。正の値のみ(対数軸のため)。</summary>
    public double[]? Frequencies
    {
        get => _frequencies;
        set => SetField(ref _frequencies, value);
    }

    /// <summary>振幅(リニア値)。dB 変換は表示側で行う。</summary>
    public double[]? Magnitudes
    {
        get => _magnitudes;
        set => SetField(ref _magnitudes, value);
    }

    /// <summary>位相 [deg]。null の場合、位相パネルにはこのシリーズを描画しない。</summary>
    public double[]? Phases
    {
        get => _phases;
        set => SetField(ref _phases, value);
    }

    /// <summary>線色。null の場合はテーマパレットから自動割当される。</summary>
    public System.Windows.Media.Color? Color
    {
        get => _color;
        set => SetField(ref _color, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
