using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace WpfCustomUI.Controls;

/// <summary>
/// クリックでドロップダウンメニューを開くボタン(spec 6.9.1)。
/// ドロップダウンの中身は <see cref="DropDownMenu"/> に ContextMenu を指定する
/// (スタイル・キーボード操作・UI Automation が既存の Menu 資産でそのまま効く)。
/// </summary>
public class DropDownButton : Button
{
    private const string PartDropDownButton = "PART_DropDownButton";

    private ButtonBase? _dropDownButton;

    static DropDownButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(DropDownButton), new FrameworkPropertyMetadata(typeof(DropDownButton)));
    }

    public static readonly DependencyProperty DropDownMenuProperty = DependencyProperty.Register(
        nameof(DropDownMenu), typeof(ContextMenu), typeof(DropDownButton),
        new PropertyMetadata(null, OnDropDownMenuChanged));

    /// <summary>ボタン押下で開くメニュー。</summary>
    public ContextMenu? DropDownMenu
    {
        get => (ContextMenu?)GetValue(DropDownMenuProperty);
        set => SetValue(DropDownMenuProperty, value);
    }

    private static readonly DependencyPropertyKey IsDropDownOpenPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsDropDownOpen), typeof(bool), typeof(DropDownButton), new PropertyMetadata(false));

    public static readonly DependencyProperty IsDropDownOpenProperty =
        IsDropDownOpenPropertyKey.DependencyProperty;

    /// <summary>メニューが開いているかどうか(読み取り専用。テンプレートの視覚状態用)。</summary>
    public bool IsDropDownOpen => (bool)GetValue(IsDropDownOpenProperty);

    /// <summary>ボタン本体のクリックでメニューを開くか。SplitButton は矢印部のみで開くため false。</summary>
    protected virtual bool OpensOnClick => true;

    private static void OnDropDownMenuChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var button = (DropDownButton)d;

        if (e.OldValue is ContextMenu oldMenu)
        {
            oldMenu.Opened -= button.OnMenuOpened;
            oldMenu.Closed -= button.OnMenuClosed;
        }

        if (e.NewValue is ContextMenu newMenu)
        {
            newMenu.Opened += button.OnMenuOpened;
            newMenu.Closed += button.OnMenuClosed;
        }

        button.SetValue(IsDropDownOpenPropertyKey, (e.NewValue as ContextMenu)?.IsOpen == true);
    }

    public override void OnApplyTemplate()
    {
        if (_dropDownButton is not null)
        {
            _dropDownButton.Click -= OnDropDownPartClick;
        }

        base.OnApplyTemplate();

        // SplitButton テンプレートの矢印部。DropDownButton のテンプレートには存在しない。
        _dropDownButton = GetTemplateChild(PartDropDownButton) as ButtonBase;

        if (_dropDownButton is not null)
        {
            _dropDownButton.Click += OnDropDownPartClick;
        }
    }

    protected override void OnClick()
    {
        base.OnClick();
        if (OpensOnClick)
        {
            OpenDropDown();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // コンボボックスと同じ定番ジェスチャでメニューを開けるようにする
        if (!e.Handled && (e.Key == Key.F4 ||
            (e.Key == Key.Down && e.KeyboardDevice.Modifiers == ModifierKeys.Alt)))
        {
            OpenDropDown();
            e.Handled = true;
        }
    }

    private void OnDropDownPartClick(object sender, RoutedEventArgs e)
    {
        // 矢印部のクリックはメニューを開くだけ。SplitButton の Click として
        // アプリにバブリングしないよう、ここで止める。
        e.Handled = true;
        OpenDropDown();
    }

    /// <summary>ドロップダウンメニューをこのボタンの直下に開く。</summary>
    protected void OpenDropDown()
    {
        if (DropDownMenu is not { } menu || menu.IsOpen)
        {
            return;
        }

        menu.PlacementTarget = this;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnMenuOpened(object sender, RoutedEventArgs e) =>
        SetValue(IsDropDownOpenPropertyKey, true);

    private void OnMenuClosed(object sender, RoutedEventArgs e) =>
        SetValue(IsDropDownOpenPropertyKey, false);
}
