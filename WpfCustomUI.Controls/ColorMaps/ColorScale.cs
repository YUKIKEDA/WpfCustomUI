using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace WpfCustomUI.Controls;

/// <summary>
/// 「物理量の値 → 色」の変換モデル(spec 6.4)。
/// カラーマップに値域(Min/Max)、対数スケール、離散レベル分割、範囲外色の扱いを加えたもの。
/// UI に依存しないため単体テスト可能。ColorMapLegend はこのモデルを描画するだけ。
/// </summary>
public sealed class ColorScale : INotifyPropertyChanged
{
    private ColorMap _colorMap = ColorMap.Jet;
    private double _minimum;
    private double _maximum = 1.0;
    private bool _isLogarithmic;
    private int? _levelCount;
    private Color? _belowRangeColor;
    private Color? _aboveRangeColor;
    private Color _naNColor = Colors.Transparent;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ColorMap ColorMap
    {
        get => _colorMap;
        set => SetField(ref _colorMap, value);
    }

    public double Minimum
    {
        get => _minimum;
        set => SetField(ref _minimum, value);
    }

    public double Maximum
    {
        get => _maximum;
        set => SetField(ref _maximum, value);
    }

    /// <summary>対数スケール。Minimum/Maximum がともに正の場合のみ有効(それ以外は線形として動作)。</summary>
    public bool IsLogarithmic
    {
        get => _isLogarithmic;
        set => SetField(ref _isLogarithmic, value);
    }

    /// <summary>離散レベル数。null なら連続グラデーション。</summary>
    public int? LevelCount
    {
        get => _levelCount;
        set => SetField(ref _levelCount, value);
    }

    /// <summary>Minimum 未満の値に使う色。null ならカラーマップの下端色にクランプ。</summary>
    public Color? BelowRangeColor
    {
        get => _belowRangeColor;
        set => SetField(ref _belowRangeColor, value);
    }

    /// <summary>Maximum 超過の値に使う色。null ならカラーマップの上端色にクランプ。</summary>
    public Color? AboveRangeColor
    {
        get => _aboveRangeColor;
        set => SetField(ref _aboveRangeColor, value);
    }

    /// <summary>NaN に使う色(既定は透明)。</summary>
    public Color NaNColor
    {
        get => _naNColor;
        set => SetField(ref _naNColor, value);
    }

    /// <summary>
    /// 全設定を複製する。ダイアログの適用/キャンセルパターン
    /// (コピーを編集させて OK 時に <see cref="CopyFrom"/> で書き戻す)を支援する(spec 6.11.2)。
    /// </summary>
    public ColorScale Clone() => new()
    {
        ColorMap = _colorMap,
        Minimum = _minimum,
        Maximum = _maximum,
        IsLogarithmic = _isLogarithmic,
        LevelCount = _levelCount,
        BelowRangeColor = _belowRangeColor,
        AboveRangeColor = _aboveRangeColor,
        NaNColor = _naNColor,
    };

    /// <summary>他のインスタンスの全設定を取り込む(変更された項目だけ通知される)。</summary>
    public void CopyFrom(ColorScale other)
    {
        ArgumentNullException.ThrowIfNull(other);
        ColorMap = other._colorMap;
        Minimum = other._minimum;
        Maximum = other._maximum;
        IsLogarithmic = other._isLogarithmic;
        LevelCount = other._levelCount;
        BelowRangeColor = other._belowRangeColor;
        AboveRangeColor = other._aboveRangeColor;
        NaNColor = other._naNColor;
    }

    private bool UseLog => _isLogarithmic && _minimum > 0 && _maximum > 0;

    /// <summary>値を 0〜1 に正規化する(範囲外はクランプせずそのまま返す)。</summary>
    public double Normalize(double value)
    {
        if (UseLog)
        {
            var logMin = Math.Log10(_minimum);
            var logMax = Math.Log10(_maximum);
            return logMax == logMin ? 0.0 : (Math.Log10(value) - logMin) / (logMax - logMin);
        }

        var range = _maximum - _minimum;
        return range == 0.0 ? 0.0 : (value - _minimum) / range;
    }

    /// <summary>正規化値 t(0〜1)を値域上の値に戻す(凡例の目盛ラベル用)。</summary>
    public double Denormalize(double t)
    {
        if (UseLog)
        {
            var logMin = Math.Log10(_minimum);
            var logMax = Math.Log10(_maximum);
            return Math.Pow(10.0, logMin + t * (logMax - logMin));
        }

        return _minimum + t * (_maximum - _minimum);
    }

    /// <summary>値に対応する色を返す(離散レベル・対数・範囲外・NaN を考慮)。</summary>
    public Color GetColor(double value)
    {
        if (double.IsNaN(value))
        {
            return _naNColor;
        }

        var t = Normalize(value);
        if (t < 0.0)
        {
            return _belowRangeColor ?? Sample(0.0);
        }

        if (t > 1.0)
        {
            return _aboveRangeColor ?? Sample(1.0);
        }

        return Sample(t);
    }

    /// <summary>正規化値からの色サンプリング(離散レベル分割を考慮)。</summary>
    public Color Sample(double t)
    {
        if (_levelCount is int levels and > 0)
        {
            var band = Math.Min((int)(t * levels), levels - 1);
            return _colorMap.GetColor((band + 0.5) / levels);
        }

        return _colorMap.GetColor(t);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
