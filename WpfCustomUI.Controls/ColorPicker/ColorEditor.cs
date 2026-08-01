using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WpfCustomUI.Controls.Theming;

namespace WpfCustomUI.Controls;

/// <summary>
/// 色選択 UI 本体(spec 6.9.4)。HSV 面+色相スライダー+アルファスライダー+
/// Hex/RGB 入力+パレットで構成される。ダイアログへの埋め込みにも、
/// <see cref="ColorPicker"/> のポップアップ内にも使える。
/// </summary>
[TemplatePart(Name = PartSvCanvas, Type = typeof(Canvas))]
[TemplatePart(Name = PartSvThumb, Type = typeof(FrameworkElement))]
[TemplatePart(Name = PartHexBox, Type = typeof(TextBox))]
public class ColorEditor : Control
{
    private const string PartSvCanvas = "PART_SvCanvas";
    private const string PartSvThumb = "PART_SvThumb";
    private const string PartHexBox = "PART_HexBox";

    /// <summary>既定パレット(汎用の16色)。</summary>
    public static IReadOnlyList<Color> DefaultPalette { get; } =
    [
        Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xC8, 0xC8, 0xC8),
        Color.FromRgb(0x80, 0x80, 0x80), Color.FromRgb(0x40, 0x40, 0x40),
        Color.FromRgb(0x00, 0x00, 0x00), Color.FromRgb(0xE8, 0x11, 0x23),
        Color.FromRgb(0xF7, 0x63, 0x0C), Color.FromRgb(0xFF, 0xD8, 0x00),
        Color.FromRgb(0x16, 0xC6, 0x0C), Color.FromRgb(0x10, 0x7C, 0x10),
        Color.FromRgb(0x00, 0xB7, 0xC3), Color.FromRgb(0x00, 0x78, 0xD7),
        Color.FromRgb(0x00, 0x3E, 0x92), Color.FromRgb(0x88, 0x6C, 0xE4),
        Color.FromRgb(0xE3, 0x00, 0x8C), Color.FromRgb(0x8E, 0x56, 0x2E),
    ];

    private Canvas? _svCanvas;
    private FrameworkElement? _svThumb;
    private TextBox? _hexBox;

    // HSV の正準状態。RGB(byte)への量子化で色相・彩度が失われないよう別途保持する
    // (例: 黒に向かうと RGB からは色相が復元できない)。
    private double _hue;
    private double _saturation;
    private double _value = 1.0;
    private bool _updating;

    static ColorEditor()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ColorEditor), new FrameworkPropertyMetadata(typeof(ColorEditor)));
    }

    public ColorEditor()
    {
        // パレットのスウォッチはテンプレート内の ItemsControl が生成するため、
        // クラスハンドラでクリックを拾う(SearchBox と同じ流儀)。
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnPaletteSwatchClick));
    }

    #region Dependency properties

    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(Color), typeof(ColorEditor),
        new FrameworkPropertyMetadata(Colors.White,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorChanged));

    /// <summary>現在選択されている色。</summary>
    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    public static readonly DependencyProperty IsAlphaEnabledProperty = DependencyProperty.Register(
        nameof(IsAlphaEnabled), typeof(bool), typeof(ColorEditor),
        new PropertyMetadata(true, OnIsAlphaEnabledChanged));

    /// <summary>アルファ(不透明度)の編集 UI を表示するか。false なら常に不透明。</summary>
    public bool IsAlphaEnabled
    {
        get => (bool)GetValue(IsAlphaEnabledProperty);
        set => SetValue(IsAlphaEnabledProperty, value);
    }

    public static readonly DependencyProperty PaletteProperty = DependencyProperty.Register(
        nameof(Palette), typeof(IEnumerable<Color>), typeof(ColorEditor),
        new PropertyMetadata(DefaultPalette));

    /// <summary>パレットに表示する色。既定は <see cref="DefaultPalette"/>。</summary>
    public IEnumerable<Color> Palette
    {
        get => (IEnumerable<Color>)GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    public static readonly DependencyProperty HueProperty = DependencyProperty.Register(
        nameof(Hue), typeof(double), typeof(ColorEditor),
        new FrameworkPropertyMetadata(0.0,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHueChanged));

    /// <summary>色相(0-360)。テンプレートの色相スライダーがバインドする。</summary>
    public double Hue
    {
        get => (double)GetValue(HueProperty);
        set => SetValue(HueProperty, value);
    }

    public static readonly DependencyProperty RedProperty = DependencyProperty.Register(
        nameof(Red), typeof(double?), typeof(ColorEditor),
        new FrameworkPropertyMetadata(255.0d as double?,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnRgbaComponentChanged));

    /// <summary>R 成分(0-255)。テンプレートの NumericBox がバインドする。</summary>
    public double? Red
    {
        get => (double?)GetValue(RedProperty);
        set => SetValue(RedProperty, value);
    }

    public static readonly DependencyProperty GreenProperty = DependencyProperty.Register(
        nameof(Green), typeof(double?), typeof(ColorEditor),
        new FrameworkPropertyMetadata(255.0d as double?,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnRgbaComponentChanged));

    /// <summary>G 成分(0-255)。</summary>
    public double? Green
    {
        get => (double?)GetValue(GreenProperty);
        set => SetValue(GreenProperty, value);
    }

    public static readonly DependencyProperty BlueProperty = DependencyProperty.Register(
        nameof(Blue), typeof(double?), typeof(ColorEditor),
        new FrameworkPropertyMetadata(255.0d as double?,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnRgbaComponentChanged));

    /// <summary>B 成分(0-255)。</summary>
    public double? Blue
    {
        get => (double?)GetValue(BlueProperty);
        set => SetValue(BlueProperty, value);
    }

    public static readonly DependencyProperty AlphaProperty = DependencyProperty.Register(
        nameof(Alpha), typeof(double), typeof(ColorEditor),
        new FrameworkPropertyMetadata(255.0,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnRgbaComponentChanged));

    /// <summary>アルファ成分(0-255)。テンプレートのアルファスライダーがバインドする。</summary>
    public double Alpha
    {
        get => (double)GetValue(AlphaProperty);
        set => SetValue(AlphaProperty, value);
    }

    private static readonly DependencyPropertyKey HueBrushPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HueBrush), typeof(Brush), typeof(ColorEditor), new PropertyMetadata(Brushes.Red));

    public static readonly DependencyProperty HueBrushProperty = HueBrushPropertyKey.DependencyProperty;

    /// <summary>現在の色相の純色ブラシ(SV 面の背景用。読み取り専用)。</summary>
    public Brush HueBrush => (Brush)GetValue(HueBrushProperty);

    private static readonly DependencyPropertyKey AlphaTrackBrushPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(AlphaTrackBrush), typeof(Brush), typeof(ColorEditor), new PropertyMetadata(null));

    public static readonly DependencyProperty AlphaTrackBrushProperty = AlphaTrackBrushPropertyKey.DependencyProperty;

    /// <summary>アルファスライダーのトラック用グラデーション(透明→現在色。読み取り専用)。</summary>
    public Brush? AlphaTrackBrush => (Brush?)GetValue(AlphaTrackBrushProperty);

    private static readonly DependencyPropertyKey PreviewBrushPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(PreviewBrush), typeof(Brush), typeof(ColorEditor), new PropertyMetadata(Brushes.White));

    public static readonly DependencyProperty PreviewBrushProperty = PreviewBrushPropertyKey.DependencyProperty;

    /// <summary>現在色のプレビュー用ブラシ(アルファ込み。読み取り専用)。</summary>
    public Brush PreviewBrush => (Brush)GetValue(PreviewBrushProperty);

    public static readonly RoutedEvent SelectedColorChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(SelectedColorChanged), RoutingStrategy.Bubble,
        typeof(RoutedPropertyChangedEventHandler<Color>), typeof(ColorEditor));

    public event RoutedPropertyChangedEventHandler<Color> SelectedColorChanged
    {
        add => AddHandler(SelectedColorChangedEvent, value);
        remove => RemoveHandler(SelectedColorChangedEvent, value);
    }

    #endregion

    public override void OnApplyTemplate()
    {
        if (_svCanvas is not null)
        {
            _svCanvas.MouseLeftButtonDown -= OnSvMouseDown;
            _svCanvas.MouseMove -= OnSvMouseMove;
            _svCanvas.MouseLeftButtonUp -= OnSvMouseUp;
            _svCanvas.SizeChanged -= OnSvSizeChanged;
        }

        if (_hexBox is not null)
        {
            _hexBox.LostKeyboardFocus -= OnHexBoxLostFocus;
            _hexBox.PreviewKeyDown -= OnHexBoxKeyDown;
        }

        base.OnApplyTemplate();

        _svCanvas = GetTemplateChild(PartSvCanvas) as Canvas;
        _svThumb = GetTemplateChild(PartSvThumb) as FrameworkElement;
        _hexBox = GetTemplateChild(PartHexBox) as TextBox;

        if (_svCanvas is not null)
        {
            _svCanvas.MouseLeftButtonDown += OnSvMouseDown;
            _svCanvas.MouseMove += OnSvMouseMove;
            _svCanvas.MouseLeftButtonUp += OnSvMouseUp;
            _svCanvas.SizeChanged += OnSvSizeChanged;
        }

        if (_hexBox is not null)
        {
            _hexBox.LostKeyboardFocus += OnHexBoxLostFocus;
            _hexBox.PreviewKeyDown += OnHexBoxKeyDown;
        }

        SyncFromColor(SelectedColor, preserveHsv: false);
    }

    #region Color synchronization

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (ColorEditor)d;

        // 内部更新中(HSV 操作由来)は HSV 状態を保持したまま表示だけ更新する。
        // 外部からの変更は HSV も色から再計算する。
        editor.SyncFromColor((Color)e.NewValue, preserveHsv: editor._updating);

        editor.RaiseEvent(new RoutedPropertyChangedEventArgs<Color>(
            (Color)e.OldValue, (Color)e.NewValue, SelectedColorChangedEvent));
    }

    private static void OnIsAlphaEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (ColorEditor)d;
        if (!(bool)e.NewValue && editor.SelectedColor.A != 255)
        {
            var color = editor.SelectedColor;
            editor.SetCurrentValue(SelectedColorProperty, Color.FromArgb(255, color.R, color.G, color.B));
        }
        else
        {
            editor.UpdateHexText();
        }
    }

    private static void OnHueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (ColorEditor)d;
        if (editor._updating)
        {
            return;
        }

        editor._hue = Math.Clamp((double)e.NewValue, 0, 360);
        editor.ApplyHsv();
    }

    private static void OnRgbaComponentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (ColorEditor)d;
        if (editor._updating)
        {
            return;
        }

        var color = Color.FromArgb(
            editor.IsAlphaEnabled ? ToByte(editor.Alpha) : (byte)255,
            ToByte(editor.Red ?? 0),
            ToByte(editor.Green ?? 0),
            ToByte(editor.Blue ?? 0));
        editor.SetCurrentValue(SelectedColorProperty, color);
    }

    /// <summary>現在の HSV 状態(+アルファ)から SelectedColor を更新する。</summary>
    private void ApplyHsv()
    {
        var rgb = ColorMath.FromHsv(_hue, _saturation, _value);
        var alpha = IsAlphaEnabled ? ToByte(Alpha) : (byte)255;

        _updating = true;
        try
        {
            SetCurrentValue(SelectedColorProperty, Color.FromArgb(alpha, rgb.R, rgb.G, rgb.B));
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>SelectedColor から各編集 UI の表示を更新する。</summary>
    private void SyncFromColor(Color color, bool preserveHsv)
    {
        if (!preserveHsv)
        {
            var (h, s, v) = ColorMath.ToHsv(color);

            // 黒(V=0)からは彩度・色相、無彩色(S=0)からは色相が復元できないため保持する
            if (v > 0)
            {
                _saturation = s;
                if (s > 0)
                {
                    _hue = h;
                }
            }

            _value = v;
        }

        var wasUpdating = _updating;
        _updating = true;
        try
        {
            SetCurrentValue(HueProperty, _hue);
            SetCurrentValue(RedProperty, (double?)color.R);
            SetCurrentValue(GreenProperty, (double?)color.G);
            SetCurrentValue(BlueProperty, (double?)color.B);
            SetCurrentValue(AlphaProperty, (double)color.A);
        }
        finally
        {
            _updating = wasUpdating;
        }

        UpdateBrushes(color);
        UpdateHexText();
        UpdateSvThumbPosition();
    }

    private void UpdateBrushes(Color color)
    {
        var hueBrush = new SolidColorBrush(ColorMath.FromHsv(_hue, 1, 1));
        hueBrush.Freeze();
        SetValue(HueBrushPropertyKey, hueBrush);

        var opaque = Color.FromRgb(color.R, color.G, color.B);
        var alphaTrack = new LinearGradientBrush(
            Color.FromArgb(0, opaque.R, opaque.G, opaque.B), opaque, 0.0);
        alphaTrack.Freeze();
        SetValue(AlphaTrackBrushPropertyKey, alphaTrack);

        var preview = new SolidColorBrush(color);
        preview.Freeze();
        SetValue(PreviewBrushPropertyKey, preview);
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

    #endregion

    #region SV area (saturation / value square)

    private void OnSvMouseDown(object sender, MouseButtonEventArgs e)
    {
        _svCanvas?.CaptureMouse();
        UpdateSvFromPoint(e.GetPosition(_svCanvas));
        e.Handled = true;
    }

    private void OnSvMouseMove(object sender, MouseEventArgs e)
    {
        if (_svCanvas is { IsMouseCaptured: true })
        {
            UpdateSvFromPoint(e.GetPosition(_svCanvas));
        }
    }

    private void OnSvMouseUp(object sender, MouseButtonEventArgs e) => _svCanvas?.ReleaseMouseCapture();

    private void OnSvSizeChanged(object sender, SizeChangedEventArgs e) => UpdateSvThumbPosition();

    private void UpdateSvFromPoint(Point point)
    {
        if (_svCanvas is null || _svCanvas.ActualWidth <= 0 || _svCanvas.ActualHeight <= 0)
        {
            return;
        }

        _saturation = Math.Clamp(point.X / _svCanvas.ActualWidth, 0, 1);
        _value = Math.Clamp(1 - point.Y / _svCanvas.ActualHeight, 0, 1);
        ApplyHsv();
    }

    private void UpdateSvThumbPosition()
    {
        if (_svCanvas is null || _svThumb is null)
        {
            return;
        }

        Canvas.SetLeft(_svThumb, _saturation * _svCanvas.ActualWidth - _svThumb.ActualWidth / 2);
        Canvas.SetTop(_svThumb, (1 - _value) * _svCanvas.ActualHeight - _svThumb.ActualHeight / 2);
    }

    #endregion

    #region Hex input

    private void OnHexBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitHexText();
            _hexBox?.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            UpdateHexText();
            e.Handled = true;
        }
    }

    private void OnHexBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitHexText();

    private void CommitHexText()
    {
        if (_hexBox is null)
        {
            return;
        }

        if (TryParseHex(_hexBox.Text, out var color))
        {
            if (!IsAlphaEnabled)
            {
                color = Color.FromArgb(255, color.R, color.G, color.B);
            }

            SetCurrentValue(SelectedColorProperty, color);
        }
        else
        {
            // 不正な入力は現在色の表記に戻す
            UpdateHexText();
        }
    }

    private void UpdateHexText()
    {
        if (_hexBox is not null)
        {
            var color = SelectedColor;
            _hexBox.Text = IsAlphaEnabled
                ? $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}"
                : $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
    }

    /// <summary>"#RRGGBB" / "#AARRGGBB"(# は省略可)をパースする。</summary>
    internal static bool TryParseHex(string? text, out Color color)
    {
        color = default;
        var hex = text?.Trim().TrimStart('#');
        if (hex is null || (hex.Length != 6 && hex.Length != 8))
        {
            return false;
        }

        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        if (hex.Length == 6)
        {
            value |= 0xFF000000;
        }

        color = Color.FromArgb(
            (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    #endregion

    private void OnPaletteSwatchClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { Name: "PaletteSwatch", DataContext: Color color })
        {
            // パレットは RGB のみを適用し、現在のアルファは維持する
            var alpha = IsAlphaEnabled ? SelectedColor.A : (byte)255;
            SetCurrentValue(SelectedColorProperty, Color.FromArgb(alpha, color.R, color.G, color.B));
            e.Handled = true;
        }
    }
}
