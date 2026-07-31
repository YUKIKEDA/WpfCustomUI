using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace WpfCustomUI.Controls;

/// <summary>トースト通知の種別。表示色(左端のストライプ)に反映される。</summary>
public enum ToastLevel
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>表示中のトースト 1 件分のデータ。</summary>
public sealed class ToastItem
{
    internal ToastItem(string message, ToastLevel level)
    {
        Message = message;
        Level = level;
    }

    public string Message { get; }
    public ToastLevel Level { get; }
}

/// <summary>
/// トースト通知のホスト(spec 6.8.6)。
/// アプリがレイアウト上の任意の場所(通常はルート Grid の右下)に明示的に配置し、
/// <see cref="Show(string, ToastLevel, TimeSpan?)"/> で通知を積む。
/// 一定時間(<see cref="DefaultDuration"/>)経過または × クリックで消える。
/// </summary>
public class ToastHost : Control
{
    /// <summary>トーストの閉じるボタンをテンプレート内で識別するための名前。</summary>
    public const string ClosePartName = "PART_ToastClose";

    private readonly ObservableCollection<ToastItem> _items = [];

    static ToastHost()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ToastHost),
            new FrameworkPropertyMetadata(typeof(ToastHost)));
    }

    public ToastHost()
    {
        SetValue(ItemsPropertyKey, new ReadOnlyObservableCollection<ToastItem>(_items));

        // テンプレート内の閉じるボタンの Click をまとめて拾う
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnCloseButtonClick));
    }

    private static readonly DependencyPropertyKey ItemsPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(Items), typeof(ReadOnlyObservableCollection<ToastItem>), typeof(ToastHost),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ItemsProperty = ItemsPropertyKey.DependencyProperty;

    /// <summary>表示中のトースト一覧(テンプレートバインド用)。</summary>
    public ReadOnlyObservableCollection<ToastItem> Items =>
        (ReadOnlyObservableCollection<ToastItem>)GetValue(ItemsProperty);

    public static readonly DependencyProperty DefaultDurationProperty =
        DependencyProperty.Register(
            nameof(DefaultDuration), typeof(TimeSpan), typeof(ToastHost),
            new PropertyMetadata(TimeSpan.FromSeconds(4)));

    /// <summary>duration 省略時の表示時間。既定 4 秒。</summary>
    public TimeSpan DefaultDuration
    {
        get => (TimeSpan)GetValue(DefaultDurationProperty);
        set => SetValue(DefaultDurationProperty, value);
    }

    /// <summary>トーストを 1 件表示する。UI スレッドから呼ぶこと。</summary>
    public void Show(string message, ToastLevel level = ToastLevel.Info, TimeSpan? duration = null)
    {
        var item = new ToastItem(message, level);
        _items.Add(item);

        var timer = new DispatcherTimer { Interval = duration ?? DefaultDuration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _items.Remove(item);
        };
        timer.Start();
    }

    /// <summary>表示中のトーストをすべて消す。</summary>
    public void Clear() => _items.Clear();

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { Name: ClosePartName, DataContext: ToastItem item })
        {
            _items.Remove(item);
            e.Handled = true;
        }
    }
}
