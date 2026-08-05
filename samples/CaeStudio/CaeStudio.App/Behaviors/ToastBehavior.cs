using Microsoft.Xaml.Behaviors;
using R3;
using System.Windows;
using WpfCustomUI.Controls;

namespace CaeStudio.App.Behaviors;

/// <summary>VM からのトースト通知要求。</summary>
public sealed record ToastRequest(string Message, ToastLevel Level = ToastLevel.Info);

/// <summary>
/// VM の R3 ストリーム(<see cref="Observable{ToastRequest}"/>)を
/// <see cref="ToastHost.Show(string, ToastLevel, TimeSpan?)"/> 呼び出しへ変換するアダプタ。
/// </summary>
public sealed class ToastBehavior : Behavior<ToastHost>
{
    public static readonly DependencyProperty RequestsProperty = DependencyProperty.Register(
        nameof(Requests), typeof(Observable<ToastRequest>), typeof(ToastBehavior),
        new PropertyMetadata(null, OnRequestsChanged));

    private IDisposable? _subscription;

    public Observable<ToastRequest>? Requests
    {
        get => (Observable<ToastRequest>?)GetValue(RequestsProperty);
        set => SetValue(RequestsProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        Subscribe();
    }

    protected override void OnDetaching()
    {
        _subscription?.Dispose();
        _subscription = null;
        base.OnDetaching();
    }

    private static void OnRequestsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ToastBehavior)d).Subscribe();

    private void Subscribe()
    {
        _subscription?.Dispose();
        _subscription = null;

        if (AssociatedObject is { } host && Requests is { } requests)
        {
            _subscription = requests
                .ObserveOnCurrentDispatcher()
                .Subscribe(host, static (request, host) => host.Show(request.Message, request.Level));
        }
    }
}
