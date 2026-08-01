using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace WpfCustomUI.Controls;

/// <summary>InfoBar の重要度。アイコンとアクセント色が変わる。</summary>
public enum InfoBarSeverity
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// インライン通知バー(spec 6.10.2)。WinUI InfoBar の縮小版 API。
/// Toast(自動で消える一時通知)と違い、ユーザーが閉じるまで残る。
/// IsOpen は既定で TwoWay バインドされるため、閉じたことを VM 側で検知できる。
/// </summary>
public class InfoBar : Control
{
    static InfoBar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(InfoBar), new FrameworkPropertyMetadata(typeof(InfoBar)));

        // テンプレート内の閉じるボタン(PART_CloseButton)のクリックを拾う
        EventManager.RegisterClassHandler(
            typeof(InfoBar), ButtonBase.ClickEvent, new RoutedEventHandler(OnButtonClick));
    }

    public static readonly DependencyProperty SeverityProperty = DependencyProperty.Register(
        nameof(Severity), typeof(InfoBarSeverity), typeof(InfoBar),
        new PropertyMetadata(InfoBarSeverity.Info));

    public InfoBarSeverity Severity
    {
        get => (InfoBarSeverity)GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(InfoBar), new PropertyMetadata(null));

    /// <summary>太字で表示される見出し。null なら非表示。</summary>
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(InfoBar), new PropertyMetadata(null));

    public string? Message
    {
        get => (string?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(InfoBar),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>false で非表示(Collapsed)。閉じるボタンで false になる。</summary>
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public static readonly DependencyProperty IsClosableProperty = DependencyProperty.Register(
        nameof(IsClosable), typeof(bool), typeof(InfoBar), new PropertyMetadata(true));

    /// <summary>false にすると閉じるボタンを隠す(アプリ側が消すまで残る通知)。</summary>
    public bool IsClosable
    {
        get => (bool)GetValue(IsClosableProperty);
        set => SetValue(IsClosableProperty, value);
    }

    public static readonly DependencyProperty ActionContentProperty = DependencyProperty.Register(
        nameof(ActionContent), typeof(object), typeof(InfoBar), new PropertyMetadata(null));

    /// <summary>メッセージ右側に置くアクション(Button や Hyperlink 等)。</summary>
    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    public static readonly RoutedEvent ClosedEvent = EventManager.RegisterRoutedEvent(
        nameof(Closed), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(InfoBar));

    /// <summary>閉じるボタンで閉じられたときに発火する。</summary>
    public event RoutedEventHandler Closed
    {
        add => AddHandler(ClosedEvent, value);
        remove => RemoveHandler(ClosedEvent, value);
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { Name: "PART_CloseButton" } &&
            sender is InfoBar bar)
        {
            e.Handled = true;
            bar.SetCurrentValue(IsOpenProperty, false);
            bar.RaiseEvent(new RoutedEventArgs(ClosedEvent, bar));
        }
    }
}
