using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace WpfCustomUI.Controls;

/// <summary>
/// キーボードショートカット入力欄(spec 6.12.4)。設定ダイアログ用。
/// フォーカス中のキー押下をキャプチャして <see cref="Gesture"/>(KeyGesture)に設定する。
/// <list type="bullet">
/// <item>文字・数字キーは修飾キー必須(WPF の KeyGesture 仕様に準拠)。F1〜F12 や Delete 等は単独可。</item>
/// <item>右端の×ボタンでクリア(null)、Esc でフォーカス取得時の値に復元。</item>
/// <item>永続化は WPF 標準の KeyGestureConverter で文字列往復できる。</item>
/// </list>
/// </summary>
public class KeyGestureBox : Control
{
    /// <summary>テンプレート内のクリアボタンの名前。</summary>
    internal const string ClearButtonPartName = "PART_ClearButton";

    private KeyGesture? _valueOnFocus;

    static KeyGestureBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(KeyGestureBox), new FrameworkPropertyMetadata(typeof(KeyGestureBox)));

        EventManager.RegisterClassHandler(
            typeof(KeyGestureBox), ButtonBase.ClickEvent, new RoutedEventHandler(OnButtonClick));
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new FrameworkElementAutomationPeer(this);

    public static readonly DependencyProperty GestureProperty = DependencyProperty.Register(
        nameof(Gesture), typeof(KeyGesture), typeof(KeyGestureBox),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnGestureChanged));

    /// <summary>設定されたショートカット。null は未割り当て。</summary>
    public KeyGesture? Gesture
    {
        get => (KeyGesture?)GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText), typeof(string), typeof(KeyGestureBox), new PropertyMetadata(null));

    /// <summary>未割り当て時に淡色表示するテキスト(例 "Press shortcut keys")。</summary>
    public string? PlaceholderText
    {
        get => (string?)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    private static readonly DependencyPropertyKey DisplayTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayText), typeof(string), typeof(KeyGestureBox), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DisplayTextProperty = DisplayTextPropertyKey.DependencyProperty;

    /// <summary>ジェスチャの表示文字列(例 "Ctrl+Shift+S"。読み取り専用)。</summary>
    public string DisplayText => (string)GetValue(DisplayTextProperty);

    private static void OnGestureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (KeyGestureBox)d;
        box.SetValue(DisplayTextPropertyKey,
            (e.NewValue as KeyGesture)?.GetDisplayStringForCulture(CultureInfo.CurrentCulture) ?? string.Empty);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (e.NewFocus == this)
        {
            _valueOnFocus = Gesture;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Tab はフォーカス移動として素通しする
        if (key is Key.Tab)
        {
            return;
        }

        // 修飾キー単独の押下は無視(組み合わせ待ち)
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;

        // Esc はフォーカス取得時の値に復元
        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            SetCurrentValue(GestureProperty, _valueOnFocus);
            return;
        }

        try
        {
            SetCurrentValue(GestureProperty, new KeyGesture(key, Keyboard.Modifiers));
        }
        catch (NotSupportedException)
        {
            // 修飾なしの文字キーなど、ショートカットとして無効な組み合わせは無視
        }
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { Name: ClearButtonPartName } &&
            sender is KeyGestureBox box)
        {
            e.Handled = true;
            box.SetCurrentValue(GestureProperty, null);
            box.Focus();
        }
    }
}
