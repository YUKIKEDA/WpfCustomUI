using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace WpfCustomUI.Controls;

/// <summary>
/// 検索アイコン+プレースホルダー+クリアボタン付きの検索入力欄(spec 6.8.1)。
/// <see cref="Text"/> は打鍵ごとに即時更新され、<see cref="SearchText"/> は
/// <see cref="SearchDelay"/>(既定 200ms)のデバウンス後に確定する。
/// フィルタ処理は SearchText にバインドすることで、大規模リストの打鍵ごとの再検索を避けられる。
/// Enter で即時確定、Esc でクリア。
/// </summary>
public class SearchBox : Control
{
    /// <summary>テンプレート内のクリアボタンの名前(クリック判定に使用)。</summary>
    internal const string ClearButtonPartName = "PART_ClearButton";

    private readonly DispatcherTimer _timer;
    private bool _syncingFromSearchText;

    static SearchBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SearchBox), new FrameworkPropertyMetadata(typeof(SearchBox)));
    }

    public SearchBox()
    {
        _timer = new DispatcherTimer();
        _timer.Tick += (_, _) => Commit();
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnClearButtonClick));
    }

    // Control 既定はピアなしで UIA ツリーに現れない。AutomationId / 子 Edit を公開する
    protected override AutomationPeer OnCreateAutomationPeer() =>
        new FrameworkElementAutomationPeer(this);

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(SearchBox),
        new FrameworkPropertyMetadata(string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

    /// <summary>入力中のテキスト(打鍵ごとに即時更新)。</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty SearchTextProperty = DependencyProperty.Register(
        nameof(SearchText), typeof(string), typeof(SearchBox),
        new FrameworkPropertyMetadata(string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSearchTextChanged));

    /// <summary>デバウンス確定後の検索テキスト。フィルタ処理はこちらにバインドする。</summary>
    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public static readonly DependencyProperty SearchDelayProperty = DependencyProperty.Register(
        nameof(SearchDelay), typeof(TimeSpan), typeof(SearchBox),
        new PropertyMetadata(TimeSpan.FromMilliseconds(200)));

    /// <summary>Text の変更が SearchText に反映されるまでの遅延。ゼロなら即時。</summary>
    public TimeSpan SearchDelay
    {
        get => (TimeSpan)GetValue(SearchDelayProperty);
        set => SetValue(SearchDelayProperty, value);
    }

    // 内蔵文字列は差し替え可能にする方針(spec 5)
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(SearchBox), new PropertyMetadata("Search..."));

    /// <summary>未入力時に表示するプレースホルダー(差し替え可能)。</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (SearchBox)d;
        if (box._syncingFromSearchText)
        {
            return;
        }

        var delay = box.SearchDelay;
        if (delay <= TimeSpan.Zero)
        {
            box.Commit();
        }
        else
        {
            box._timer.Stop();
            box._timer.Interval = delay;
            box._timer.Start();
        }
    }

    private static void OnSearchTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // アプリ側から SearchText を直接書き換えた場合は入力欄も同期する
        var box = (SearchBox)d;
        if (!box._timer.IsEnabled && !Equals(box.Text, e.NewValue))
        {
            box._syncingFromSearchText = true;
            try
            {
                box.Text = (string?)e.NewValue ?? string.Empty;
            }
            finally
            {
                box._syncingFromSearchText = false;
            }
        }
    }

    /// <summary>デバウンスを打ち切り、現在の Text を SearchText に反映する。</summary>
    public void Commit()
    {
        _timer.Stop();
        SearchText = Text;
    }

    /// <summary>テキストを消去し、即時確定する。</summary>
    public void Clear()
    {
        Text = string.Empty;
        Commit();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            Commit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && !string.IsNullOrEmpty(Text))
        {
            Clear();
            e.Handled = true;
        }
    }

    private void OnClearButtonClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { Name: ClearButtonPartName })
        {
            Clear();
            e.Handled = true;
        }
    }
}
