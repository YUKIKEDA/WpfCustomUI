using Microsoft.Xaml.Behaviors;
using System.Windows;
using WpfCustomUI.Controls;

namespace CaeStudio.App.Behaviors;

/// <summary>
/// Wizard の完了/キャンセルでホストダイアログを閉じる View 専用アダプタ。
/// ライブラリの Wizard はイベントのみ公開でダイアログを閉じる責務を持たないため、
/// コードビハインドの代わりに Behavior で吸収する(spec 6.26.3)。
/// </summary>
public sealed class WizardDialogBehavior : Behavior<Wizard>
{
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.Finished += OnFinished;
        AssociatedObject.Cancelled += OnCancelled;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.Finished -= OnFinished;
        AssociatedObject.Cancelled -= OnCancelled;
        base.OnDetaching();
    }

    private void OnFinished(object sender, RoutedEventArgs e) => Close(true);

    private void OnCancelled(object sender, RoutedEventArgs e) => Close(false);

    private void Close(bool result)
    {
        if (Window.GetWindow(AssociatedObject) is { } window)
        {
            window.DialogResult = result;
        }
    }
}
