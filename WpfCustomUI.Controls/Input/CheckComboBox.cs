using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace WpfCustomUI.Controls;

/// <summary>
/// 複数選択コンボボックス(spec 6.12.1)。結果成分・荷重ケース・レイヤ表示などのフィルタ用。
/// <list type="bullet">
/// <item>選択状態は <see cref="SelectedItems"/>(IList)。アプリが ObservableCollection を渡せば双方向に同期する。</item>
/// <item>閉時表示は既定で選択項目の表示名連結。<see cref="SummaryFormat"/>(例 "{0} selected")を
/// 設定すると件数表示に切り替わる。</item>
/// <item><see cref="SelectAllContent"/> にラベルを与えると「すべて選択」行(トライステート)が現れる。</item>
/// </list>
/// </summary>
[TemplatePart(Name = PartToggleButton, Type = typeof(ToggleButton))]
[TemplatePart(Name = PartPopup, Type = typeof(Popup))]
public class CheckComboBox : ItemsControl
{
    private const string PartToggleButton = "PART_ToggleButton";
    private const string PartPopup = "PART_Popup";

    /// <summary>テンプレート内の「すべて選択」チェックボックスの名前。</summary>
    internal const string SelectAllPartName = "PART_SelectAllCheckBox";

    private ToggleButton? _toggleButton;
    private Popup? _popup;
    private int _popupClosedTick;
    private bool _updating;

    static CheckComboBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(CheckComboBox), new FrameworkPropertyMetadata(typeof(CheckComboBox)));
    }

    public CheckComboBox()
    {
        // 項目チェックボックスはテンプレート内にあるため、バブリングイベントで一括処理する。
        // Click ではなく Checked/Unchecked を使う(UI Automation の Toggle は Click を発生させない)。
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnItemToggled));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(OnItemToggled));
    }

    #region Dependency properties

    public static readonly DependencyProperty SelectedItemsProperty = DependencyProperty.Register(
        nameof(SelectedItems), typeof(IList), typeof(CheckComboBox),
        new PropertyMetadata(null, OnSelectedItemsChanged));

    /// <summary>
    /// 選択中の項目。INotifyCollectionChanged 実装(ObservableCollection 推奨)なら
    /// 外部からの増減にも表示が追従する。
    /// </summary>
    public IList? SelectedItems
    {
        get => (IList?)GetValue(SelectedItemsProperty);
        set => SetValue(SelectedItemsProperty, value);
    }

    public static readonly DependencyProperty IsDropDownOpenProperty = DependencyProperty.Register(
        nameof(IsDropDownOpen), typeof(bool), typeof(CheckComboBox),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    public static readonly DependencyProperty MaxDropDownHeightProperty = DependencyProperty.Register(
        nameof(MaxDropDownHeight), typeof(double), typeof(CheckComboBox), new PropertyMetadata(300.0));

    public double MaxDropDownHeight
    {
        get => (double)GetValue(MaxDropDownHeightProperty);
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    public static readonly DependencyProperty SummaryFormatProperty = DependencyProperty.Register(
        nameof(SummaryFormat), typeof(string), typeof(CheckComboBox),
        new PropertyMetadata(null, OnSummaryAffectingChanged));

    /// <summary>
    /// 件数表示の書式(例 "{0} selected"、"{0} 件選択")。
    /// null(既定)なら選択項目の表示名を連結して表示する。
    /// </summary>
    public string? SummaryFormat
    {
        get => (string?)GetValue(SummaryFormatProperty);
        set => SetValue(SummaryFormatProperty, value);
    }

    public static readonly DependencyProperty SelectAllContentProperty = DependencyProperty.Register(
        nameof(SelectAllContent), typeof(object), typeof(CheckComboBox), new PropertyMetadata(null));

    /// <summary>「すべて選択」行のラベル。null(既定)なら行そのものを表示しない。</summary>
    public object? SelectAllContent
    {
        get => GetValue(SelectAllContentProperty);
        set => SetValue(SelectAllContentProperty, value);
    }

    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText), typeof(string), typeof(CheckComboBox), new PropertyMetadata(null));

    /// <summary>未選択時に淡色表示するテキスト。</summary>
    public string? PlaceholderText
    {
        get => (string?)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    private static readonly DependencyPropertyKey SummaryTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(SummaryText), typeof(string), typeof(CheckComboBox), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SummaryTextProperty = SummaryTextPropertyKey.DependencyProperty;

    /// <summary>閉時表示のテキスト(読み取り専用)。</summary>
    public string SummaryText => (string)GetValue(SummaryTextProperty);

    private static readonly DependencyPropertyKey SelectAllStatePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(SelectAllState), typeof(bool?), typeof(CheckComboBox), new PropertyMetadata(false));

    public static readonly DependencyProperty SelectAllStateProperty = SelectAllStatePropertyKey.DependencyProperty;

    /// <summary>「すべて選択」の三状態(true=全選択 / false=未選択 / null=一部。読み取り専用)。</summary>
    public bool? SelectAllState => (bool?)GetValue(SelectAllStateProperty);

    #endregion

    // ItemsControl 既定のピアはアイテムだけを公開し、テンプレート内の
    // トグルボタンや「すべて選択」行が UIA から見えなくなるため、汎用ピアに差し替える
    protected override AutomationPeer OnCreateAutomationPeer() =>
        new FrameworkElementAutomationPeer(this);

    protected override DependencyObject GetContainerForItemOverride() => new CheckComboBoxItem();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is CheckComboBoxItem;

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is CheckComboBoxItem container)
        {
            var updating = _updating;
            _updating = true;
            container.IsChecked = SelectedItems?.Contains(item) == true;
            _updating = updating;
        }
    }

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        UpdateSummary();
    }

    public override void OnApplyTemplate()
    {
        if (_popup is not null)
        {
            _popup.Closed -= OnPopupClosed;
        }

        if (_toggleButton is not null)
        {
            _toggleButton.PreviewMouseLeftButtonDown -= OnToggleButtonPreviewMouseDown;
        }

        base.OnApplyTemplate();
        _popup = GetTemplateChild(PartPopup) as Popup;
        _toggleButton = GetTemplateChild(PartToggleButton) as ToggleButton;

        if (_popup is not null)
        {
            _popup.Closed += OnPopupClosed;
        }

        if (_toggleButton is not null)
        {
            _toggleButton.PreviewMouseLeftButtonDown += OnToggleButtonPreviewMouseDown;
        }

        UpdateSummary();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.F4 ||
            (e.Key == Key.Down && e.KeyboardDevice.Modifiers == ModifierKeys.Alt))
        {
            SetCurrentValue(IsDropDownOpenProperty, !IsDropDownOpen);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && IsDropDownOpen)
        {
            SetCurrentValue(IsDropDownOpenProperty, false);
            e.Handled = true;
        }
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        _popupClosedTick = Environment.TickCount;
    }

    private void OnToggleButtonPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // ポップアップが開いた状態でトグルボタンをクリックすると、
        // キャプチャ喪失で閉じた直後に同じクリックで再度開いてしまう(WPF Popup の定番の罠)。
        // 「閉じた直後」のクリックを無視して、クリックで閉じられるようにする。
        if (Environment.TickCount - _popupClosedTick < 200 && !IsDropDownOpen)
        {
            e.Handled = true;
        }
    }

    private void OnItemToggled(object sender, RoutedEventArgs e)
    {
        if (_updating || SelectedItems is not IList selected)
        {
            return;
        }

        // 「すべて選択」行: 全選択済みなら解除、それ以外(未選択・一部選択)なら全選択。
        // チェックボックス自身の新しい状態ではなく、操作前の SelectAllState で判定する
        // (三状態からのトグル遷移に依存しないため)。
        if (e.OriginalSource is FrameworkElement { Name: SelectAllPartName })
        {
            e.Handled = true;
            var selectAll = SelectAllState != true;
            _updating = true;
            try
            {
                selected.Clear();
                if (selectAll)
                {
                    foreach (var it in Items)
                    {
                        selected.Add(it);
                    }
                }

                foreach (var it in Items)
                {
                    if (ItemContainerGenerator.ContainerFromItem(it) is CheckComboBoxItem c)
                    {
                        c.IsChecked = selectAll;
                    }
                }
            }
            finally
            {
                _updating = false;
            }

            UpdateSummary();
            return;
        }

        if (e.OriginalSource is not CheckComboBoxItem container)
        {
            return;
        }

        var item = ItemContainerGenerator.ItemFromContainer(container);
        if (item is null || item == DependencyProperty.UnsetValue)
        {
            return;
        }

        if (container.IsChecked == true)
        {
            if (!selected.Contains(item))
            {
                selected.Add(item);
            }
        }
        else if (selected.Contains(item))
        {
            selected.Remove(item);
        }

        // SelectedItems が INotifyCollectionChanged でない場合に備えて明示更新
        UpdateSummary();
    }

    private static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (CheckComboBox)d;
        if (e.OldValue is INotifyCollectionChanged oldIncc)
        {
            oldIncc.CollectionChanged -= box.OnSelectedItemsCollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged newIncc)
        {
            newIncc.CollectionChanged += box.OnSelectedItemsCollectionChanged;
        }

        box.RefreshFromSelection();
    }

    private void OnSelectedItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_updating)
        {
            RefreshFromSelection();
        }
        else
        {
            UpdateSummary();
        }
    }

    private static void OnSummaryAffectingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CheckComboBox)d).UpdateSummary();

    /// <summary>SelectedItems の現状を、実体化済みコンテナのチェック状態とサマリに反映する。</summary>
    private void RefreshFromSelection()
    {
        _updating = true;
        try
        {
            foreach (var item in Items)
            {
                if (ItemContainerGenerator.ContainerFromItem(item) is CheckComboBoxItem container)
                {
                    container.IsChecked = SelectedItems?.Contains(item) == true;
                }
            }
        }
        finally
        {
            _updating = false;
        }

        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var selected = SelectedItems;
        var count = selected?.Count ?? 0;

        // SelectAllState の更新は OneWay バインド経由で「すべて選択」チェックボックスの
        // Checked/Unchecked を再発火させるため、ユーザー操作と誤認しないようガードする
        var wasUpdating = _updating;
        _updating = true;
        try
        {
            SetValue(SelectAllStatePropertyKey,
                count == 0 ? false
                : Items.Count > 0 && count >= Items.Count ? true
                : (bool?)null);
        }
        finally
        {
            _updating = wasUpdating;
        }

        string summary;
        if (count == 0)
        {
            summary = string.Empty;
        }
        else if (SummaryFormat is { Length: > 0 } format)
        {
            summary = string.Format(CultureInfo.CurrentCulture, format, count);
        }
        else
        {
            // Items の並び順で表示名を連結(選択順に依存しない安定表示)
            summary = string.Join(", ", Items.Cast<object>()
                .Where(i => selected!.Contains(i))
                .Select(GetItemText));
        }

        SetValue(SummaryTextPropertyKey, summary);
    }

    private string GetItemText(object? item)
    {
        if (item is null)
        {
            return string.Empty;
        }

        if (DisplayMemberPath is { Length: > 0 } path)
        {
            object? current = item;
            foreach (var part in path.Split('.'))
            {
                current = current?.GetType().GetProperty(part)?.GetValue(current);
            }

            return current?.ToString() ?? string.Empty;
        }

        return item.ToString() ?? string.Empty;
    }
}

/// <summary>
/// <see cref="CheckComboBox"/> の項目コンテナ。行全体がクリック可能なチェックボックス。
/// </summary>
public class CheckComboBoxItem : CheckBox
{
    static CheckComboBoxItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(CheckComboBoxItem), new FrameworkPropertyMetadata(typeof(CheckComboBoxItem)));
    }
}
