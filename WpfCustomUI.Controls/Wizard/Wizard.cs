using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace WpfCustomUI.Controls;

/// <summary>ステップ遷移イベント(キャンセル可能)の引数。</summary>
public class WizardNavigatingEventArgs(RoutedEvent routedEvent, object source, int fromIndex, int toIndex)
    : RoutedEventArgs(routedEvent, source)
{
    /// <summary>遷移元のステップ(0 始まり)。</summary>
    public int FromIndex { get; } = fromIndex;

    /// <summary>遷移先のステップ(0 始まり)。</summary>
    public int ToIndex { get; } = toIndex;

    /// <summary>true にすると遷移を中止する。</summary>
    public bool Cancel { get; set; }
}

public delegate void WizardNavigatingEventHandler(object sender, WizardNavigatingEventArgs e);

/// <summary>
/// ウィザード(段階的フロー)のページホスト(spec 6.12.5)。
/// <see cref="WizardStep"/>(Header + Content)を並べ、戻る/次へ/完了/キャンセルの
/// ナビゲーションと <see cref="StepIndicator"/> を内蔵する。
/// ウィンドウはアプリが用意する(WcuDialogWindow に置く分担)。
/// <list type="bullet">
/// <item>「次へ」の可否は <see cref="CanGoNext"/> / <see cref="CanFinish"/>(宣言的)と
/// キャンセル可能な <see cref="Navigating"/> イベント(押下時検証)の両方で制御できる。</item>
/// <item>完了/キャンセルは <see cref="Finished"/> / <see cref="Cancelled"/> をアプリが処理して
/// ダイアログを閉じる。</item>
/// <item>ボタン文字列は既定英語、DP で差し替え(spec 5)。</item>
/// </list>
/// </summary>
public class Wizard : ItemsControl
{
    private const string PartBackButton = "PART_BackButton";
    private const string PartNextButton = "PART_NextButton";
    private const string PartFinishButton = "PART_FinishButton";
    private const string PartCancelButton = "PART_CancelButton";

    static Wizard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(Wizard), new FrameworkPropertyMetadata(typeof(Wizard)));

        EventManager.RegisterClassHandler(
            typeof(Wizard), ButtonBase.ClickEvent, new RoutedEventHandler(OnButtonClick));
    }

    #region Dependency properties

    public static readonly DependencyProperty CurrentIndexProperty = DependencyProperty.Register(
        nameof(CurrentIndex), typeof(int), typeof(Wizard),
        new FrameworkPropertyMetadata(0,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnCurrentIndexChanged, CoerceCurrentIndex));

    /// <summary>現在のステップ(0 始まり、TwoWay)。0〜ステップ数-1 に強制される。</summary>
    public int CurrentIndex
    {
        get => (int)GetValue(CurrentIndexProperty);
        set => SetValue(CurrentIndexProperty, value);
    }

    public static readonly DependencyProperty CanGoNextProperty = DependencyProperty.Register(
        nameof(CanGoNext), typeof(bool), typeof(Wizard), new PropertyMetadata(true));

    /// <summary>「次へ」ボタンの有効状態(VM が現在ステップの妥当性をバインドする)。</summary>
    public bool CanGoNext
    {
        get => (bool)GetValue(CanGoNextProperty);
        set => SetValue(CanGoNextProperty, value);
    }

    public static readonly DependencyProperty CanFinishProperty = DependencyProperty.Register(
        nameof(CanFinish), typeof(bool), typeof(Wizard), new PropertyMetadata(true));

    /// <summary>「完了」ボタンの有効状態。</summary>
    public bool CanFinish
    {
        get => (bool)GetValue(CanFinishProperty);
        set => SetValue(CanFinishProperty, value);
    }

    public static readonly DependencyProperty BackButtonTextProperty = DependencyProperty.Register(
        nameof(BackButtonText), typeof(string), typeof(Wizard), new PropertyMetadata("Back"));

    public string BackButtonText
    {
        get => (string)GetValue(BackButtonTextProperty);
        set => SetValue(BackButtonTextProperty, value);
    }

    public static readonly DependencyProperty NextButtonTextProperty = DependencyProperty.Register(
        nameof(NextButtonText), typeof(string), typeof(Wizard), new PropertyMetadata("Next"));

    public string NextButtonText
    {
        get => (string)GetValue(NextButtonTextProperty);
        set => SetValue(NextButtonTextProperty, value);
    }

    public static readonly DependencyProperty FinishButtonTextProperty = DependencyProperty.Register(
        nameof(FinishButtonText), typeof(string), typeof(Wizard), new PropertyMetadata("Finish"));

    public string FinishButtonText
    {
        get => (string)GetValue(FinishButtonTextProperty);
        set => SetValue(FinishButtonTextProperty, value);
    }

    public static readonly DependencyProperty CancelButtonTextProperty = DependencyProperty.Register(
        nameof(CancelButtonText), typeof(string), typeof(Wizard), new PropertyMetadata("Cancel"));

    public string CancelButtonText
    {
        get => (string)GetValue(CancelButtonTextProperty);
        set => SetValue(CancelButtonTextProperty, value);
    }

    private static readonly DependencyPropertyKey IsFirstStepPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsFirstStep), typeof(bool), typeof(Wizard), new PropertyMetadata(true));

    public static readonly DependencyProperty IsFirstStepProperty = IsFirstStepPropertyKey.DependencyProperty;

    /// <summary>先頭ステップか(「戻る」の無効化判定。読み取り専用)。</summary>
    public bool IsFirstStep => (bool)GetValue(IsFirstStepProperty);

    private static readonly DependencyPropertyKey IsLastStepPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsLastStep), typeof(bool), typeof(Wizard), new PropertyMetadata(true));

    public static readonly DependencyProperty IsLastStepProperty = IsLastStepPropertyKey.DependencyProperty;

    /// <summary>最終ステップか(「次へ」⇔「完了」の切り替え判定。読み取り専用)。</summary>
    public bool IsLastStep => (bool)GetValue(IsLastStepProperty);

    private static readonly DependencyPropertyKey StepHeadersPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(StepHeaders), typeof(ReadOnlyCollection<object?>), typeof(Wizard), new PropertyMetadata(null));

    public static readonly DependencyProperty StepHeadersProperty = StepHeadersPropertyKey.DependencyProperty;

    /// <summary>各ステップのヘッダー一覧(テンプレートの StepIndicator が使用。読み取り専用)。</summary>
    public ReadOnlyCollection<object?>? StepHeaders =>
        (ReadOnlyCollection<object?>?)GetValue(StepHeadersProperty);

    #endregion

    #region Events

    public static readonly RoutedEvent NavigatingEvent = EventManager.RegisterRoutedEvent(
        nameof(Navigating), RoutingStrategy.Bubble, typeof(WizardNavigatingEventHandler), typeof(Wizard));

    /// <summary>戻る/次へによる遷移前に発火する(Cancel=true で中止)。</summary>
    public event WizardNavigatingEventHandler Navigating
    {
        add => AddHandler(NavigatingEvent, value);
        remove => RemoveHandler(NavigatingEvent, value);
    }

    public static readonly RoutedEvent FinishedEvent = EventManager.RegisterRoutedEvent(
        nameof(Finished), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(Wizard));

    /// <summary>「完了」押下で発火する。ダイアログを閉じるのはアプリの責務。</summary>
    public event RoutedEventHandler Finished
    {
        add => AddHandler(FinishedEvent, value);
        remove => RemoveHandler(FinishedEvent, value);
    }

    public static readonly RoutedEvent CancelledEvent = EventManager.RegisterRoutedEvent(
        nameof(Cancelled), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(Wizard));

    /// <summary>「キャンセル」押下で発火する。</summary>
    public event RoutedEventHandler Cancelled
    {
        add => AddHandler(CancelledEvent, value);
        remove => RemoveHandler(CancelledEvent, value);
    }

    #endregion

    // ItemsControl 既定のピアはアイテムだけを公開し、ナビゲーションボタンや
    // StepIndicator が UIA から見えなくなるため、汎用ピアに差し替える
    protected override AutomationPeer OnCreateAutomationPeer() =>
        new FrameworkElementAutomationPeer(this);

    protected override DependencyObject GetContainerForItemOverride() => new WizardStep();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is WizardStep;

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is WizardStep step)
        {
            var index = ItemContainerGenerator.IndexFromContainer(step);
            step.Visibility = index == CurrentIndex ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        CoerceValue(CurrentIndexProperty);
        UpdateState();
    }

    /// <summary>「次へ」相当の遷移をプログラムから実行する(Navigating 検証込み)。</summary>
    public bool GoNext() => TryNavigate(CurrentIndex + 1);

    /// <summary>「戻る」相当の遷移をプログラムから実行する(Navigating 検証込み)。</summary>
    public bool GoBack() => TryNavigate(CurrentIndex - 1);

    private bool TryNavigate(int toIndex)
    {
        if (toIndex < 0 || toIndex >= Items.Count)
        {
            return false;
        }

        var args = new WizardNavigatingEventArgs(NavigatingEvent, this, CurrentIndex, toIndex);
        RaiseEvent(args);
        if (args.Cancel)
        {
            return false;
        }

        SetCurrentValue(CurrentIndexProperty, toIndex);
        return true;
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        var wizard = (Wizard)sender;
        switch ((e.OriginalSource as FrameworkElement)?.Name)
        {
            case PartBackButton:
                e.Handled = true;
                wizard.GoBack();
                break;
            case PartNextButton:
                e.Handled = true;
                if (wizard.CanGoNext)
                {
                    wizard.GoNext();
                }

                break;
            case PartFinishButton:
                e.Handled = true;
                if (wizard.CanFinish)
                {
                    wizard.RaiseEvent(new RoutedEventArgs(FinishedEvent, wizard));
                }

                break;
            case PartCancelButton:
                e.Handled = true;
                wizard.RaiseEvent(new RoutedEventArgs(CancelledEvent, wizard));
                break;
        }
    }

    private static object CoerceCurrentIndex(DependencyObject d, object baseValue)
    {
        var wizard = (Wizard)d;
        return Math.Clamp((int)baseValue, 0, Math.Max(0, wizard.Items.Count - 1));
    }

    private static void OnCurrentIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((Wizard)d).UpdateState();

    private void UpdateState()
    {
        var count = Items.Count;
        var current = CurrentIndex;

        SetValue(IsFirstStepPropertyKey, current <= 0);
        SetValue(IsLastStepPropertyKey, current >= count - 1);
        SetValue(StepHeadersPropertyKey, new ReadOnlyCollection<object?>(
            [.. Items.Cast<object?>().Select(i => i is HeaderedContentControl h ? h.Header : i)]));

        for (var i = 0; i < count; i++)
        {
            if (ItemContainerGenerator.ContainerFromIndex(i) is WizardStep step)
            {
                step.Visibility = i == current ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}
