using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace WpfCustomUI.Controls;

/// <summary>
/// 結果アニメーションの再生バー(spec 6.11.1)。
/// <list type="bullet">
/// <item>フレームモデルはインデックスベース: <see cref="FrameCount"/> と <see cref="CurrentFrame"/>(TwoWay 既定)。</item>
/// <item>再生タイマー内蔵(<see cref="IsPlaying"/> / <see cref="FramesPerSecond"/> / <see cref="IsLooping"/>)。
/// 外部駆動したいアプリは IsPlaying を使わず CurrentFrame を直接動かせばよい。</item>
/// <item>3D 描画はアプリの領分。本コントロールは現在フレームと再生状態を公開する操作 UI に徹する。</item>
/// <item>キーボード: Space(再生/停止)、← →(ステップ)、Home / End(先頭/末尾)。</item>
/// </list>
/// </summary>
public class PlaybackBar : Control
{
    private readonly DispatcherTimer _timer;

    static PlaybackBar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(PlaybackBar), new FrameworkPropertyMetadata(typeof(PlaybackBar)));

        // テンプレート内のステップボタン(PART_StepBackButton / PART_StepForwardButton)のクリックを拾う
        EventManager.RegisterClassHandler(
            typeof(PlaybackBar), ButtonBase.ClickEvent, new RoutedEventHandler(OnButtonClick));
    }

    public PlaybackBar()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = IntervalFor(10.0) };
        _timer.Tick += OnTimerTick;

        // Unloaded 中はタイマーを止めてリークを防ぐ(IsPlaying 状態は維持し、再表示で再開)
        Loaded += (_, _) => UpdateTimer();
        Unloaded += (_, _) => _timer.Stop();
    }

    #region Dependency properties

    public static readonly DependencyProperty FrameCountProperty = DependencyProperty.Register(
        nameof(FrameCount), typeof(int), typeof(PlaybackBar),
        new PropertyMetadata(0, OnFrameCountChanged), v => (int)v >= 0);

    /// <summary>総フレーム数。0 のときコントロールは無効表示になる。</summary>
    public int FrameCount
    {
        get => (int)GetValue(FrameCountProperty);
        set => SetValue(FrameCountProperty, value);
    }

    public static readonly DependencyProperty CurrentFrameProperty = DependencyProperty.Register(
        nameof(CurrentFrame), typeof(int), typeof(PlaybackBar),
        new FrameworkPropertyMetadata(0,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnCurrentFrameChanged, CoerceCurrentFrame));

    /// <summary>現在フレーム(0 始まり)。0〜FrameCount-1 に強制される。</summary>
    public int CurrentFrame
    {
        get => (int)GetValue(CurrentFrameProperty);
        set => SetValue(CurrentFrameProperty, value);
    }

    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying), typeof(bool), typeof(PlaybackBar),
        new FrameworkPropertyMetadata(false,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsPlayingChanged));

    /// <summary>再生中かどうか。true の間、内蔵タイマーが CurrentFrame を進める。</summary>
    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public static readonly DependencyProperty FramesPerSecondProperty = DependencyProperty.Register(
        nameof(FramesPerSecond), typeof(double), typeof(PlaybackBar),
        new PropertyMetadata(10.0, OnFramesPerSecondChanged),
        v => (double)v > 0 && !double.IsInfinity((double)v));

    /// <summary>再生速度(フレーム/秒)。速度選択 UI は持たない(アプリが隣に置く)。</summary>
    public double FramesPerSecond
    {
        get => (double)GetValue(FramesPerSecondProperty);
        set => SetValue(FramesPerSecondProperty, value);
    }

    public static readonly DependencyProperty IsLoopingProperty = DependencyProperty.Register(
        nameof(IsLooping), typeof(bool), typeof(PlaybackBar), new PropertyMetadata(true));

    /// <summary>末尾到達時に先頭へ戻って継続するか(既定 ON)。OFF なら末尾で自動停止。</summary>
    public bool IsLooping
    {
        get => (bool)GetValue(IsLoopingProperty);
        set => SetValue(IsLoopingProperty, value);
    }

    public static readonly DependencyProperty FrameLabelProperty = DependencyProperty.Register(
        nameof(FrameLabel), typeof(string), typeof(PlaybackBar), new PropertyMetadata(null));

    /// <summary>現在フレームの説明表示(例: "t = 0.24 s")。整形はアプリの責務。</summary>
    public string? FrameLabel
    {
        get => (string?)GetValue(FrameLabelProperty);
        set => SetValue(FrameLabelProperty, value);
    }

    private static readonly DependencyPropertyKey MaxFrameIndexPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(MaxFrameIndex), typeof(int), typeof(PlaybackBar), new PropertyMetadata(0));

    public static readonly DependencyProperty MaxFrameIndexProperty = MaxFrameIndexPropertyKey.DependencyProperty;

    /// <summary>スライダーの最大値(FrameCount-1。読み取り専用、テンプレート用)。</summary>
    public int MaxFrameIndex => (int)GetValue(MaxFrameIndexProperty);

    private static readonly DependencyPropertyKey PositionTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(PositionText), typeof(string), typeof(PlaybackBar), new PropertyMetadata("0 / 0"));

    public static readonly DependencyProperty PositionTextProperty = PositionTextPropertyKey.DependencyProperty;

    /// <summary>「13 / 50」形式の位置表示(1 始まり。読み取り専用、テンプレート用)。</summary>
    public string PositionText => (string)GetValue(PositionTextProperty);

    #endregion

    public static readonly RoutedEvent CurrentFrameChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(CurrentFrameChanged), RoutingStrategy.Bubble,
        typeof(RoutedPropertyChangedEventHandler<int>), typeof(PlaybackBar));

    /// <summary>現在フレームの変更通知(コードビハインドで 3D 表示を更新する用途)。</summary>
    public event RoutedPropertyChangedEventHandler<int> CurrentFrameChanged
    {
        add => AddHandler(CurrentFrameChangedEvent, value);
        remove => RemoveHandler(CurrentFrameChangedEvent, value);
    }

    /// <summary>1 フレーム進める(末尾ではループ設定に従う)。</summary>
    public void StepForward()
    {
        if (FrameCount <= 0)
        {
            return;
        }

        var next = CurrentFrame + 1;
        SetCurrentValue(CurrentFrameProperty, next >= FrameCount && IsLooping ? 0 : next);
    }

    /// <summary>1 フレーム戻す(先頭ではループ設定に従う)。</summary>
    public void StepBackward()
    {
        if (FrameCount <= 0)
        {
            return;
        }

        var prev = CurrentFrame - 1;
        SetCurrentValue(CurrentFrameProperty, prev < 0 && IsLooping ? FrameCount - 1 : prev);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                SetCurrentValue(IsPlayingProperty, !IsPlaying);
                e.Handled = true;
                break;
            case Key.Left:
                StepBackward();
                e.Handled = true;
                break;
            case Key.Right:
                StepForward();
                e.Handled = true;
                break;
            case Key.Home:
                SetCurrentValue(CurrentFrameProperty, 0);
                e.Handled = true;
                break;
            case Key.End:
                SetCurrentValue(CurrentFrameProperty, Math.Max(0, FrameCount - 1));
                e.Handled = true;
                break;
        }
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        var bar = (PlaybackBar)sender;
        switch ((e.OriginalSource as FrameworkElement)?.Name)
        {
            case "PART_StepBackButton":
                e.Handled = true;
                bar.StepBackward();
                break;
            case "PART_StepForwardButton":
                e.Handled = true;
                bar.StepForward();
                break;
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (FrameCount <= 0)
        {
            return;
        }

        var next = CurrentFrame + 1;
        if (next >= FrameCount)
        {
            if (IsLooping)
            {
                SetCurrentValue(CurrentFrameProperty, 0);
            }
            else
            {
                // 末尾で自動停止(位置は末尾のまま)
                SetCurrentValue(IsPlayingProperty, false);
            }

            return;
        }

        SetCurrentValue(CurrentFrameProperty, next);
    }

    private static void OnFrameCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (PlaybackBar)d;
        bar.SetValue(MaxFrameIndexPropertyKey, Math.Max(0, (int)e.NewValue - 1));
        bar.CoerceValue(CurrentFrameProperty);
        bar.UpdatePositionText();
    }

    private static object CoerceCurrentFrame(DependencyObject d, object baseValue)
    {
        var bar = (PlaybackBar)d;
        return Math.Clamp((int)baseValue, 0, Math.Max(0, bar.FrameCount - 1));
    }

    private static void OnCurrentFrameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (PlaybackBar)d;
        bar.UpdatePositionText();
        bar.RaiseEvent(new RoutedPropertyChangedEventArgs<int>(
            (int)e.OldValue, (int)e.NewValue, CurrentFrameChangedEvent));
    }

    private static void OnIsPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PlaybackBar)d).UpdateTimer();

    private static void OnFramesPerSecondChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PlaybackBar)d)._timer.Interval = IntervalFor((double)e.NewValue);

    private static TimeSpan IntervalFor(double fps) => TimeSpan.FromSeconds(1.0 / fps);

    private void UpdateTimer()
    {
        if (IsPlaying && IsLoaded)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void UpdatePositionText() =>
        SetValue(PositionTextPropertyKey, FrameCount > 0
            ? $"{CurrentFrame + 1} / {FrameCount}"
            : "0 / 0");
}
