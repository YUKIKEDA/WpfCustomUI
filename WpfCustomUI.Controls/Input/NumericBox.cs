using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace WpfCustomUI.Controls;

/// <summary>
/// 単位付き数値入力コントロール(spec 6.1)。
/// <list type="bullet">
/// <item>値は <see cref="Value"/>(double?)。null は「未入力/空欄」。</item>
/// <item>Enter / フォーカス喪失で確定、Esc で編集前の値に復元。</item>
/// <item>指数表記("1e-3")を受け付ける。数式評価はしない。</item>
/// <item>単位換算は <see cref="UnitProvider"/>(IUnitProvider)に委譲。</item>
/// <item>検証エラーは赤枠+ツールチップで表示(spec 4.6)。</item>
/// </list>
/// </summary>
[TemplatePart(Name = PartTextBox, Type = typeof(TextBox))]
[TemplatePart(Name = PartIncreaseButton, Type = typeof(ButtonBase))]
[TemplatePart(Name = PartDecreaseButton, Type = typeof(ButtonBase))]
public class NumericBox : Control
{
    private const string PartTextBox = "PART_TextBox";
    private const string PartIncreaseButton = "PART_IncreaseButton";
    private const string PartDecreaseButton = "PART_DecreaseButton";

    private TextBox? _textBox;
    private ButtonBase? _increaseButton;
    private ButtonBase? _decreaseButton;

    static NumericBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(NumericBox), new FrameworkPropertyMetadata(typeof(NumericBox)));
    }

    #region Dependency properties

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double?), typeof(NumericBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    /// <summary>内部値(基準単位)。null は未入力。</summary>
    public double? Value
    {
        get => (double?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(NumericBox), new PropertyMetadata(double.NegativeInfinity));

    /// <summary>内部値(基準単位)での下限。</summary>
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(NumericBox), new PropertyMetadata(double.PositiveInfinity));

    /// <summary>内部値(基準単位)での上限。</summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public static readonly DependencyProperty IncrementProperty = DependencyProperty.Register(
        nameof(Increment), typeof(double), typeof(NumericBox), new PropertyMetadata(1.0));

    /// <summary>増減ボタン・↑↓キー・ホイールの1ステップ量(表示単位での量)。</summary>
    public double Increment
    {
        get => (double)GetValue(IncrementProperty);
        set => SetValue(IncrementProperty, value);
    }

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(NumericBox), new PropertyMetadata(null, OnUnitChanged));

    /// <summary>単位ラベルの文字列(換算なしの表示専用)。UnitProvider があればそちらが優先。</summary>
    public string? Unit
    {
        get => (string?)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public static readonly DependencyProperty UnitProviderProperty = DependencyProperty.Register(
        nameof(UnitProvider), typeof(IUnitProvider), typeof(NumericBox), new PropertyMetadata(null, OnUnitChanged));

    /// <summary>表示単位と内部値の換算を担うプロバイダー(spec 6.1)。</summary>
    public IUnitProvider? UnitProvider
    {
        get => (IUnitProvider?)GetValue(UnitProviderProperty);
        set => SetValue(UnitProviderProperty, value);
    }

    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(
        nameof(Format), typeof(string), typeof(NumericBox), new PropertyMetadata("G", OnFormatChanged));

    /// <summary>表示用の数値書式(例: "G6"、"F2")。</summary>
    public string Format
    {
        get => (string)GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(NumericBox), new PropertyMetadata(false));

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    // 内蔵文字列は極力持たない方針(spec 5)のため、エラーメッセージは差し替え可能にする。
    public static readonly DependencyProperty ParseErrorTextProperty = DependencyProperty.Register(
        nameof(ParseErrorText), typeof(string), typeof(NumericBox),
        new PropertyMetadata("Invalid numeric value."));

    /// <summary>パース不能時のエラーメッセージ(差し替え可能)。</summary>
    public string ParseErrorText
    {
        get => (string)GetValue(ParseErrorTextProperty);
        set => SetValue(ParseErrorTextProperty, value);
    }

    public static readonly DependencyProperty OutOfRangeErrorFormatProperty = DependencyProperty.Register(
        nameof(OutOfRangeErrorFormat), typeof(string), typeof(NumericBox),
        new PropertyMetadata("Value must be between {0} and {1}."));

    /// <summary>範囲外エラーメッセージの書式。{0}=下限、{1}=上限(差し替え可能)。</summary>
    public string OutOfRangeErrorFormat
    {
        get => (string)GetValue(OutOfRangeErrorFormatProperty);
        set => SetValue(OutOfRangeErrorFormatProperty, value);
    }

    private static readonly DependencyPropertyKey HasErrorPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasError), typeof(bool), typeof(NumericBox), new PropertyMetadata(false));

    public static readonly DependencyProperty HasErrorProperty = HasErrorPropertyKey.DependencyProperty;

    /// <summary>現在の入力テキストが検証エラーかどうか(読み取り専用)。</summary>
    public bool HasError => (bool)GetValue(HasErrorProperty);

    private static readonly DependencyPropertyKey ErrorTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ErrorText), typeof(string), typeof(NumericBox), new PropertyMetadata(null));

    public static readonly DependencyProperty ErrorTextProperty = ErrorTextPropertyKey.DependencyProperty;

    /// <summary>検証エラーの内容(読み取り専用。ツールチップに表示される)。</summary>
    public string? ErrorText => (string?)GetValue(ErrorTextProperty);

    private static readonly DependencyPropertyKey UnitTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(UnitText), typeof(string), typeof(NumericBox), new PropertyMetadata(null));

    public static readonly DependencyProperty UnitTextProperty = UnitTextPropertyKey.DependencyProperty;

    /// <summary>単位ラベルに表示する文字列(UnitProvider.DisplayUnit または Unit)。</summary>
    public string? UnitText => (string?)GetValue(UnitTextProperty);

    #endregion

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (NumericBox)d;

        // ユーザーの編集中(フォーカスあり)は外部からの値変更でテキストを潰さない。
        // 確定時は Commit 側で明示的に表示を更新する。
        if (box._textBox is { IsKeyboardFocusWithin: false })
        {
            box.UpdateDisplayText();
            box.ClearError();
        }
    }

    private static void OnUnitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (NumericBox)d;
        box.UpdateUnitText();
        if (box._textBox is { IsKeyboardFocusWithin: false })
        {
            box.UpdateDisplayText();
        }
    }

    private static void OnFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (NumericBox)d;
        if (box._textBox is { IsKeyboardFocusWithin: false })
        {
            box.UpdateDisplayText();
        }
    }

    public override void OnApplyTemplate()
    {
        if (_textBox != null)
        {
            _textBox.PreviewKeyDown -= OnTextBoxPreviewKeyDown;
            _textBox.LostKeyboardFocus -= OnTextBoxLostKeyboardFocus;
        }

        if (_increaseButton != null)
        {
            _increaseButton.Click -= OnIncreaseClick;
        }

        if (_decreaseButton != null)
        {
            _decreaseButton.Click -= OnDecreaseClick;
        }

        base.OnApplyTemplate();

        _textBox = GetTemplateChild(PartTextBox) as TextBox;
        _increaseButton = GetTemplateChild(PartIncreaseButton) as ButtonBase;
        _decreaseButton = GetTemplateChild(PartDecreaseButton) as ButtonBase;

        if (_textBox != null)
        {
            _textBox.PreviewKeyDown += OnTextBoxPreviewKeyDown;
            _textBox.LostKeyboardFocus += OnTextBoxLostKeyboardFocus;
        }

        if (_increaseButton != null)
        {
            _increaseButton.Click += OnIncreaseClick;
        }

        if (_decreaseButton != null)
        {
            _decreaseButton.Click += OnDecreaseClick;
        }

        UpdateUnitText();
        UpdateDisplayText();
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);

        if (!IsReadOnly && _textBox is { IsKeyboardFocusWithin: true })
        {
            Spin(e.Delta > 0 ? +1 : -1);
            e.Handled = true;
        }
    }

    private void OnTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Commit();
                _textBox?.SelectAll();
                e.Handled = true;
                break;
            case Key.Escape:
                RevertText();
                e.Handled = true;
                break;
            case Key.Up when !IsReadOnly:
                Spin(+1);
                e.Handled = true;
                break;
            case Key.Down when !IsReadOnly:
                Spin(-1);
                e.Handled = true;
                break;
        }
    }

    private void OnTextBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => Commit();

    private void OnIncreaseClick(object sender, RoutedEventArgs e) => Spin(+1);

    private void OnDecreaseClick(object sender, RoutedEventArgs e) => Spin(-1);

    /// <summary>現在のテキストをパース・検証して Value に確定する。</summary>
    private void Commit()
    {
        if (_textBox is null)
        {
            return;
        }

        var text = _textBox.Text;

        if (string.IsNullOrWhiteSpace(text))
        {
            ClearError();
            SetCurrentValue(ValueProperty, null);
            UpdateDisplayText();
            return;
        }

        if (!NumericText.TryParse(text, CultureInfo.CurrentCulture, out var number, out var unitSymbol))
        {
            SetError(ParseErrorText);
            return;
        }

        double baseValue;
        if (unitSymbol is not null)
        {
            // 明示的な単位付き入力("500 mm")はプロバイダーが解釈できた場合のみ有効
            if (UnitProvider is null || !UnitProvider.TryConvertFrom(number, unitSymbol, out baseValue))
            {
                SetError(ParseErrorText);
                return;
            }
        }
        else
        {
            baseValue = UnitProvider?.FromDisplay(number) ?? number;
        }

        if (baseValue < Minimum || baseValue > Maximum)
        {
            // エラーメッセージの上下限はユーザーが入力している表示単位で見せる
            var displayMin = UnitProvider?.ToDisplay(Minimum) ?? Minimum;
            var displayMax = UnitProvider?.ToDisplay(Maximum) ?? Maximum;
            SetError(string.Format(CultureInfo.CurrentCulture, OutOfRangeErrorFormat, FormatNumber(displayMin), FormatNumber(displayMax)));
            return;
        }

        ClearError();
        SetCurrentValue(ValueProperty, baseValue);
        UpdateDisplayText();
    }

    /// <summary>編集内容を破棄し、現在の Value の表示に戻す(Esc)。</summary>
    private void RevertText()
    {
        ClearError();
        UpdateDisplayText();
        _textBox?.SelectAll();
    }

    /// <summary>表示単位で Increment × direction だけ増減する。</summary>
    private void Spin(int direction)
    {
        if (IsReadOnly)
        {
            return;
        }

        var provider = UnitProvider;
        var currentBase = Value ?? 0;
        var display = provider?.ToDisplay(currentBase) ?? currentBase;
        var newDisplay = display + Increment * direction;
        var newBase = provider?.FromDisplay(newDisplay) ?? newDisplay;

        newBase = Math.Clamp(newBase, Minimum, Maximum);

        ClearError();
        SetCurrentValue(ValueProperty, newBase);
        UpdateDisplayText();
        _textBox?.SelectAll();
    }

    private void UpdateDisplayText()
    {
        if (_textBox is null)
        {
            return;
        }

        var value = Value;
        if (value is null)
        {
            _textBox.Text = string.Empty;
            return;
        }

        var display = UnitProvider?.ToDisplay(value.Value) ?? value.Value;
        _textBox.Text = FormatNumber(display);
    }

    private void UpdateUnitText() =>
        SetValue(UnitTextPropertyKey, UnitProvider?.DisplayUnit ?? Unit);

    private string FormatNumber(double value) =>
        double.IsNegativeInfinity(value) ? "-∞"
        : double.IsPositiveInfinity(value) ? "+∞"
        : value.ToString(Format, CultureInfo.CurrentCulture);

    private void SetError(string message)
    {
        SetValue(HasErrorPropertyKey, true);
        SetValue(ErrorTextPropertyKey, message);
    }

    private void ClearError()
    {
        SetValue(HasErrorPropertyKey, false);
        SetValue(ErrorTextPropertyKey, null);
    }
}
