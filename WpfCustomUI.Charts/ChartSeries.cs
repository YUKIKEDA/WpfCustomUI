using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WpfCustomUI.Charts;

/// <summary>
/// <see cref="HistoryChart"/> に表示する 1 本の折れ線を表す明示アイテムモデル(spec 6.14.3)。
/// データ更新は <see cref="X"/> / <see cref="Y"/> の配列インスタンス差し替えで通知する。
/// </summary>
public class ChartSeries : INotifyPropertyChanged
{
    private string _name = "";
    private double[]? _x;
    private double[]? _y;
    private System.Windows.Media.Color? _color;
    private double _lineWidth = 2;

    /// <summary>凡例に表示するシリーズ名。</summary>
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    /// <summary>X 値の配列。Y と同じ長さであること。</summary>
    public double[]? X
    {
        get => _x;
        set => SetField(ref _x, value);
    }

    /// <summary>Y 値の配列。X と同じ長さであること。</summary>
    public double[]? Y
    {
        get => _y;
        set => SetField(ref _y, value);
    }

    /// <summary>線色。null の場合はテーマパレットから自動割当される。</summary>
    public System.Windows.Media.Color? Color
    {
        get => _color;
        set => SetField(ref _color, value);
    }

    /// <summary>線の太さ(既定 2)。</summary>
    public double LineWidth
    {
        get => _lineWidth;
        set => SetField(ref _lineWidth, value);
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
