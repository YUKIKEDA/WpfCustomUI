using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace WpfCustomUI.Controls;

/// <summary>
/// 範囲(下限〜上限)を選択するスライダー(spec 6.9.3)。
/// 両端の Thumb に加え、中央の選択範囲バー自体をドラッグすると
/// 範囲幅を保ったまま両端を同時に移動できる(コンター範囲の「幅そのままスライド」)。
/// </summary>
[TemplatePart(Name = PartCanvas, Type = typeof(Canvas))]
[TemplatePart(Name = PartLowerThumb, Type = typeof(Thumb))]
[TemplatePart(Name = PartUpperThumb, Type = typeof(Thumb))]
[TemplatePart(Name = PartRangeThumb, Type = typeof(Thumb))]
public class RangeSlider : Control
{
    private const string PartCanvas = "PART_Canvas";
    private const string PartLowerThumb = "PART_LowerThumb";
    private const string PartUpperThumb = "PART_UpperThumb";
    private const string PartRangeThumb = "PART_RangeThumb";

    private Canvas? _canvas;
    private Thumb? _lowerThumb;
    private Thumb? _upperThumb;
    private Thumb? _rangeThumb;

    static RangeSlider()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(RangeSlider), new FrameworkPropertyMetadata(typeof(RangeSlider)));
    }

    public RangeSlider()
    {
        // 初回レイアウト完了前は Thumb の ActualWidth が 0 のため、Loaded 後に再配置する
        Loaded += (_, _) => UpdateThumbPositions();
    }

    #region Dependency properties

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(RangeSlider),
        new FrameworkPropertyMetadata(0.0, OnRangeChanged));

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(RangeSlider),
        new FrameworkPropertyMetadata(1.0, OnRangeChanged));

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public static readonly DependencyProperty LowerValueProperty = DependencyProperty.Register(
        nameof(LowerValue), typeof(double), typeof(RangeSlider),
        new FrameworkPropertyMetadata(0.0,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceLowerValue));

    /// <summary>選択範囲の下限。</summary>
    public double LowerValue
    {
        get => (double)GetValue(LowerValueProperty);
        set => SetValue(LowerValueProperty, value);
    }

    public static readonly DependencyProperty UpperValueProperty = DependencyProperty.Register(
        nameof(UpperValue), typeof(double), typeof(RangeSlider),
        new FrameworkPropertyMetadata(1.0,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceUpperValue));

    /// <summary>選択範囲の上限。</summary>
    public double UpperValue
    {
        get => (double)GetValue(UpperValueProperty);
        set => SetValue(UpperValueProperty, value);
    }

    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(RangeSlider),
        new FrameworkPropertyMetadata(Orientation.Horizontal));

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    #endregion

    private static object CoerceLowerValue(DependencyObject d, object baseValue)
    {
        var slider = (RangeSlider)d;
        var value = (double)baseValue;
        return Math.Clamp(value, slider.Minimum, Math.Min(slider.UpperValue, slider.Maximum));
    }

    private static object CoerceUpperValue(DependencyObject d, object baseValue)
    {
        var slider = (RangeSlider)d;
        var value = (double)baseValue;
        return Math.Clamp(value, Math.Max(slider.LowerValue, slider.Minimum), slider.Maximum);
    }

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var slider = (RangeSlider)d;
        slider.CoerceValue(LowerValueProperty);
        slider.CoerceValue(UpperValueProperty);
        slider.UpdateThumbPositions();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var slider = (RangeSlider)d;

        // 上限を下限より下に(またはその逆に)動かした場合に相方を再クランプする
        if (e.Property == UpperValueProperty)
        {
            slider.CoerceValue(LowerValueProperty);
        }
        else
        {
            slider.CoerceValue(UpperValueProperty);
        }

        slider.UpdateThumbPositions();
    }

    public override void OnApplyTemplate()
    {
        if (_lowerThumb is not null)
        {
            _lowerThumb.DragDelta -= OnLowerDragDelta;
        }

        if (_upperThumb is not null)
        {
            _upperThumb.DragDelta -= OnUpperDragDelta;
        }

        if (_rangeThumb is not null)
        {
            _rangeThumb.DragDelta -= OnRangeDragDelta;
        }

        if (_canvas is not null)
        {
            _canvas.SizeChanged -= OnCanvasSizeChanged;
        }

        base.OnApplyTemplate();

        _canvas = GetTemplateChild(PartCanvas) as Canvas;
        _lowerThumb = GetTemplateChild(PartLowerThumb) as Thumb;
        _upperThumb = GetTemplateChild(PartUpperThumb) as Thumb;
        _rangeThumb = GetTemplateChild(PartRangeThumb) as Thumb;

        if (_lowerThumb is not null)
        {
            _lowerThumb.DragDelta += OnLowerDragDelta;
        }

        if (_upperThumb is not null)
        {
            _upperThumb.DragDelta += OnUpperDragDelta;
        }

        if (_rangeThumb is not null)
        {
            _rangeThumb.DragDelta += OnRangeDragDelta;
        }

        if (_canvas is not null)
        {
            _canvas.SizeChanged += OnCanvasSizeChanged;
        }

        UpdateThumbPositions();
    }

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e) => UpdateThumbPositions();

    private void OnLowerDragDelta(object sender, DragDeltaEventArgs e) =>
        SetCurrentValue(LowerValueProperty, LowerValue + DragToValueDelta(e));

    private void OnUpperDragDelta(object sender, DragDeltaEventArgs e) =>
        SetCurrentValue(UpperValueProperty, UpperValue + DragToValueDelta(e));

    private void OnRangeDragDelta(object sender, DragDeltaEventArgs e)
    {
        // 範囲幅を保ったまま両端を同時に移動する。端に当たったら止める。
        var delta = Math.Clamp(DragToValueDelta(e), Minimum - LowerValue, Maximum - UpperValue);
        if (delta == 0)
        {
            return;
        }

        // 片方ずつ設定すると Coerce で相方に食い込むため、動かす方向の外側から先に設定する
        if (delta > 0)
        {
            SetCurrentValue(UpperValueProperty, UpperValue + delta);
            SetCurrentValue(LowerValueProperty, LowerValue + delta);
        }
        else
        {
            SetCurrentValue(LowerValueProperty, LowerValue + delta);
            SetCurrentValue(UpperValueProperty, UpperValue + delta);
        }
    }

    /// <summary>ドラッグのピクセル移動量を値の変化量に換算する(縦向きは下が最小)。</summary>
    private double DragToValueDelta(DragDeltaEventArgs e)
    {
        var usable = UsableLength();
        if (usable <= 0)
        {
            return 0;
        }

        var pixels = Orientation == Orientation.Horizontal ? e.HorizontalChange : -e.VerticalChange;
        return pixels / usable * (Maximum - Minimum);
    }

    /// <summary>Thumb の中心が動ける距離(トラック長 − Thumb 1個分)。</summary>
    private double UsableLength()
    {
        if (_canvas is null || _lowerThumb is null)
        {
            return 0;
        }

        var thumbSize = Orientation == Orientation.Horizontal
            ? _lowerThumb.ActualWidth
            : _lowerThumb.ActualHeight;
        var length = Orientation == Orientation.Horizontal
            ? _canvas.ActualWidth
            : _canvas.ActualHeight;
        return Math.Max(0, length - thumbSize);
    }

    private void UpdateThumbPositions()
    {
        if (_canvas is null || _lowerThumb is null || _upperThumb is null || _rangeThumb is null)
        {
            return;
        }

        var usable = UsableLength();
        var range = Maximum - Minimum;
        var tLower = range <= 0 ? 0 : (LowerValue - Minimum) / range;
        var tUpper = range <= 0 ? 0 : (UpperValue - Minimum) / range;

        if (Orientation == Orientation.Horizontal)
        {
            var yCenter = (_canvas.ActualHeight - _lowerThumb.ActualHeight) / 2;
            var xLower = tLower * usable;
            var xUpper = tUpper * usable;

            Canvas.SetLeft(_lowerThumb, xLower);
            Canvas.SetTop(_lowerThumb, yCenter);
            Canvas.SetLeft(_upperThumb, xUpper);
            Canvas.SetTop(_upperThumb, yCenter);

            // 範囲バーは Thumb の中心間を埋める(掴みやすいよう高さはトラック全体)
            var halfThumb = _lowerThumb.ActualWidth / 2;
            Canvas.SetLeft(_rangeThumb, xLower + halfThumb);
            Canvas.SetTop(_rangeThumb, 0);
            _rangeThumb.Width = Math.Max(0, xUpper - xLower);
            _rangeThumb.Height = _canvas.ActualHeight;
        }
        else
        {
            var xCenter = (_canvas.ActualWidth - _lowerThumb.ActualWidth) / 2;

            // 縦向きは下が最小(標準 Slider と同じ)
            var yLower = (1 - tLower) * usable;
            var yUpper = (1 - tUpper) * usable;

            Canvas.SetLeft(_lowerThumb, xCenter);
            Canvas.SetTop(_lowerThumb, yLower);
            Canvas.SetLeft(_upperThumb, xCenter);
            Canvas.SetTop(_upperThumb, yUpper);

            var halfThumb = _lowerThumb.ActualHeight / 2;
            Canvas.SetLeft(_rangeThumb, 0);
            Canvas.SetTop(_rangeThumb, yUpper + halfThumb);
            _rangeThumb.Width = _canvas.ActualWidth;
            _rangeThumb.Height = Math.Max(0, yLower - yUpper);
        }
    }
}
