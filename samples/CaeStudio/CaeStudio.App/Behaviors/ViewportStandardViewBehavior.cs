using Microsoft.Xaml.Behaviors;
using System.Windows;
using WpfCustomUI.Viewport3D;

namespace CaeStudio.App.Behaviors;

/// <summary>VM からの標準視点変更要求(同じ視点の再要求を区別するため連番付き)。</summary>
public sealed record ViewRequest(int Sequence, ViewportStandardView View);

/// <summary>
/// VM の視点変更要求を <see cref="WcuViewport.SetStandardView"/> 呼び出しへ変換するアダプタ。
/// </summary>
public sealed class ViewportStandardViewBehavior : Behavior<WcuViewport>
{
    public static readonly DependencyProperty RequestProperty = DependencyProperty.Register(
        nameof(Request), typeof(ViewRequest), typeof(ViewportStandardViewBehavior),
        new PropertyMetadata(null, OnRequestChanged));

    public ViewRequest? Request
    {
        get => (ViewRequest?)GetValue(RequestProperty);
        set => SetValue(RequestProperty, value);
    }

    private static void OnRequestChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ViewportStandardViewBehavior { AssociatedObject: { } viewport } &&
            e.NewValue is ViewRequest request)
        {
            viewport.SetStandardView(request.View);
        }
    }
}
