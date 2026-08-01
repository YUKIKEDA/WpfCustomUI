using System.Collections.Specialized;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace WpfCustomUI.Controls;

/// <summary>ステップの進行状態(<see cref="StepIndicatorItem"/> の視覚状態)。</summary>
public enum StepState
{
    /// <summary>完了済み(現在より前)。</summary>
    Completed,

    /// <summary>現在のステップ。</summary>
    Current,

    /// <summary>未到達(現在より後)。</summary>
    Upcoming,
}

/// <summary>
/// ステップ進捗の表示専用コントロール(spec 6.12.5)。
/// <see cref="Wizard"/> のテンプレートが内蔵するほか、ソルバー進行状況表示などに単体でも使える。
/// Items にはステップのヘッダー(文字列等)を並べる。
/// </summary>
public class StepIndicator : ItemsControl
{
    static StepIndicator()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(StepIndicator), new FrameworkPropertyMetadata(typeof(StepIndicator)));
    }

    public static readonly DependencyProperty CurrentIndexProperty = DependencyProperty.Register(
        nameof(CurrentIndex), typeof(int), typeof(StepIndicator),
        new FrameworkPropertyMetadata(0,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCurrentIndexChanged));

    /// <summary>現在のステップ(0 始まり)。</summary>
    public int CurrentIndex
    {
        get => (int)GetValue(CurrentIndexProperty);
        set => SetValue(CurrentIndexProperty, value);
    }

    // ItemsControl 既定のピアではなく汎用ピアを使い、番号丸やヘッダーを UIA に露出する
    protected override AutomationPeer OnCreateAutomationPeer() =>
        new FrameworkElementAutomationPeer(this);

    protected override DependencyObject GetContainerForItemOverride() => new StepIndicatorItem();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is StepIndicatorItem;

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is StepIndicatorItem container)
        {
            var index = ItemContainerGenerator.IndexFromContainer(container);
            container.SetStep(index + 1, index == 0, StateFor(index));
        }
    }

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        UpdateStates();
    }

    private static void OnCurrentIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((StepIndicator)d).UpdateStates();

    private StepState StateFor(int index) =>
        index < CurrentIndex ? StepState.Completed
        : index == CurrentIndex ? StepState.Current
        : StepState.Upcoming;

    private void UpdateStates()
    {
        for (var i = 0; i < Items.Count; i++)
        {
            if (ItemContainerGenerator.ContainerFromIndex(i) is StepIndicatorItem container)
            {
                container.SetStep(i + 1, i == 0, StateFor(i));
            }
        }
    }
}

/// <summary>
/// <see cref="StepIndicator"/> の項目コンテナ。番号付きの丸+ヘッダー+接続線で1ステップを表す。
/// </summary>
public class StepIndicatorItem : ContentControl
{
    static StepIndicatorItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(StepIndicatorItem), new FrameworkPropertyMetadata(typeof(StepIndicatorItem)));
    }

    private static readonly DependencyPropertyKey StepNumberPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(StepNumber), typeof(int), typeof(StepIndicatorItem), new PropertyMetadata(1));

    public static readonly DependencyProperty StepNumberProperty = StepNumberPropertyKey.DependencyProperty;

    /// <summary>1 始まりのステップ番号(読み取り専用)。</summary>
    public int StepNumber => (int)GetValue(StepNumberProperty);

    private static readonly DependencyPropertyKey IsFirstPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsFirst), typeof(bool), typeof(StepIndicatorItem), new PropertyMetadata(false));

    public static readonly DependencyProperty IsFirstProperty = IsFirstPropertyKey.DependencyProperty;

    /// <summary>先頭ステップか(接続線の非表示判定。読み取り専用)。</summary>
    public bool IsFirst => (bool)GetValue(IsFirstProperty);

    private static readonly DependencyPropertyKey StatePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(State), typeof(StepState), typeof(StepIndicatorItem), new PropertyMetadata(StepState.Upcoming));

    public static readonly DependencyProperty StateProperty = StatePropertyKey.DependencyProperty;

    /// <summary>進行状態(読み取り専用)。</summary>
    public StepState State => (StepState)GetValue(StateProperty);

    internal void SetStep(int number, bool isFirst, StepState state)
    {
        SetValue(StepNumberPropertyKey, number);
        SetValue(IsFirstPropertyKey, isFirst);
        SetValue(StatePropertyKey, state);
    }
}
