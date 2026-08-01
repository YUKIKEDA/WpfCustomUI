using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WpfCustomUI.Controls;

/// <summary>
/// 現在色のスウォッチを表示し、クリックでポップアップの <see cref="ColorEditor"/> を
/// 開くドロップダウン型カラーピッカー(spec 6.9.4)。
/// </summary>
public class ColorPicker : Control
{
    static ColorPicker()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ColorPicker), new FrameworkPropertyMetadata(typeof(ColorPicker)));
    }

    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(Color), typeof(ColorPicker),
        new FrameworkPropertyMetadata(Colors.White,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorChanged));

    /// <summary>現在選択されている色。</summary>
    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    public static readonly DependencyProperty IsAlphaEnabledProperty = DependencyProperty.Register(
        nameof(IsAlphaEnabled), typeof(bool), typeof(ColorPicker), new PropertyMetadata(true));

    /// <summary>アルファ(不透明度)の編集を許可するか。</summary>
    public bool IsAlphaEnabled
    {
        get => (bool)GetValue(IsAlphaEnabledProperty);
        set => SetValue(IsAlphaEnabledProperty, value);
    }

    public static readonly DependencyProperty PaletteProperty = DependencyProperty.Register(
        nameof(Palette), typeof(IEnumerable<Color>), typeof(ColorPicker),
        new PropertyMetadata(ColorEditor.DefaultPalette));

    /// <summary>ポップアップ内のパレットに表示する色。</summary>
    public IEnumerable<Color> Palette
    {
        get => (IEnumerable<Color>)GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    public static readonly DependencyProperty IsDropDownOpenProperty = DependencyProperty.Register(
        nameof(IsDropDownOpen), typeof(bool), typeof(ColorPicker),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>ポップアップが開いているかどうか。</summary>
    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    private static readonly DependencyPropertyKey PreviewBrushPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(PreviewBrush), typeof(Brush), typeof(ColorPicker), new PropertyMetadata(Brushes.White));

    public static readonly DependencyProperty PreviewBrushProperty = PreviewBrushPropertyKey.DependencyProperty;

    /// <summary>スウォッチに表示する現在色のブラシ(読み取り専用)。</summary>
    public Brush PreviewBrush => (Brush)GetValue(PreviewBrushProperty);

    public static readonly RoutedEvent SelectedColorChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(SelectedColorChanged), RoutingStrategy.Bubble,
        typeof(RoutedPropertyChangedEventHandler<Color>), typeof(ColorPicker));

    public event RoutedPropertyChangedEventHandler<Color> SelectedColorChanged
    {
        add => AddHandler(SelectedColorChangedEvent, value);
        remove => RemoveHandler(SelectedColorChangedEvent, value);
    }

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (ColorPicker)d;

        var brush = new SolidColorBrush((Color)e.NewValue);
        brush.Freeze();
        picker.SetValue(PreviewBrushPropertyKey, brush);

        picker.RaiseEvent(new RoutedPropertyChangedEventArgs<Color>(
            (Color)e.OldValue, (Color)e.NewValue, SelectedColorChangedEvent));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape && IsDropDownOpen)
        {
            SetCurrentValue(IsDropDownOpenProperty, false);
            e.Handled = true;
        }
        else if (e.Key == Key.F4 ||
                 (e.Key == Key.Down && e.KeyboardDevice.Modifiers == ModifierKeys.Alt))
        {
            SetCurrentValue(IsDropDownOpenProperty, !IsDropDownOpen);
            e.Handled = true;
        }
    }
}
