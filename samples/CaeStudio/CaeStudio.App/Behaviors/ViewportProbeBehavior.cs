using Microsoft.Xaml.Behaviors;
using System.Windows;
using System.Windows.Input;
using WpfCustomUI.Viewport3D;

namespace CaeStudio.App.Behaviors;

/// <summary>
/// ProbePicked イベント(CLR)を VM のコマンドへ、VM のフォーマッターを
/// <see cref="WcuViewport.ProbeLabelFormatter"/>(CLR プロパティ)へ橋渡しするアダプタ。
/// ライブラリ既定の注釈追加はそのまま活かす(Handled にしない)。
/// </summary>
public sealed class ViewportProbeBehavior : Behavior<WcuViewport>
{
    public static readonly DependencyProperty ProbeCommandProperty = DependencyProperty.Register(
        nameof(ProbeCommand), typeof(ICommand), typeof(ViewportProbeBehavior),
        new PropertyMetadata(null));

    public static readonly DependencyProperty LabelFormatterProperty = DependencyProperty.Register(
        nameof(LabelFormatter), typeof(Func<ProbeResult, string>), typeof(ViewportProbeBehavior),
        new PropertyMetadata(null, OnLabelFormatterChanged));

    public static readonly DependencyProperty ClearRequestProperty = DependencyProperty.Register(
        nameof(ClearRequest), typeof(int), typeof(ViewportProbeBehavior),
        new PropertyMetadata(0, OnClearRequestChanged));

    /// <summary>プローブ確定時に <see cref="ProbeResult"/> を引数に実行されるコマンド。</summary>
    public ICommand? ProbeCommand
    {
        get => (ICommand?)GetValue(ProbeCommandProperty);
        set => SetValue(ProbeCommandProperty, value);
    }

    /// <summary>注釈ラベルの書式(単位付けはアプリ責務)。</summary>
    public Func<ProbeResult, string>? LabelFormatter
    {
        get => (Func<ProbeResult, string>?)GetValue(LabelFormatterProperty);
        set => SetValue(LabelFormatterProperty, value);
    }

    /// <summary>インクリメントされるたびに注釈を全削除するカウンタ(VM からの要求)。</summary>
    public int ClearRequest
    {
        get => (int)GetValue(ClearRequestProperty);
        set => SetValue(ClearRequestProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.ProbePicked += OnProbePicked;
        AssociatedObject.ProbeLabelFormatter = LabelFormatter;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.ProbePicked -= OnProbePicked;
        AssociatedObject.ProbeLabelFormatter = null;
        base.OnDetaching();
    }

    private static void OnLabelFormatterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ViewportProbeBehavior { AssociatedObject: { } viewport })
        {
            viewport.ProbeLabelFormatter = (Func<ProbeResult, string>?)e.NewValue;
        }
    }

    private static void OnClearRequestChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ViewportProbeBehavior { AssociatedObject: { } viewport } && e.OldValue is int old &&
            (int)e.NewValue != old)
        {
            viewport.Annotations.Clear();
        }
    }

    private void OnProbePicked(object? sender, ProbePickedEventArgs e)
    {
        if (ProbeCommand?.CanExecute(e.Result) == true)
        {
            ProbeCommand.Execute(e.Result);
        }
    }
}
