using Microsoft.Xaml.Behaviors;
using System.Windows;
using WpfCustomUI.Viewport3D;

namespace CaeStudio.App.Behaviors;

/// <summary>
/// VM からビューポートの Fit を要求するためのアダプタ(spec 6.26.3)。
/// <see cref="WcuViewport.FitToView"/> はメソッドのため直接バインドできない。
/// VM 側のカウンタ(<see cref="FitRequest"/>)の増加を Fit 実行に変換する。
/// </summary>
public sealed class ViewportFitBehavior : Behavior<WcuViewport>
{
    public static readonly DependencyProperty FitRequestProperty = DependencyProperty.Register(
        nameof(FitRequest), typeof(int), typeof(ViewportFitBehavior),
        new PropertyMetadata(0, OnFitRequestChanged));

    /// <summary>VM がインクリメントするたびに FitToView を呼ぶ。</summary>
    public int FitRequest
    {
        get => (int)GetValue(FitRequestProperty);
        set => SetValue(FitRequestProperty, value);
    }

    private static void OnFitRequestChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ViewportFitBehavior { AssociatedObject: { } viewport } && e.NewValue is int value && value != 0)
        {
            viewport.FitToView();
        }
    }
}
