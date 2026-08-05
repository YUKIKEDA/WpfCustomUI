using Microsoft.Xaml.Behaviors;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfCustomUI.Controls;

namespace CaeStudio.App.Behaviors;

/// <summary>
/// ModelTree の選択変更(CLR イベント)を VM のコマンドへ変換するアダプタ(spec 6.26.3)。
/// コマンド引数は選択中ノードのリスト(IReadOnlyList&lt;ITreeNode&gt;)。
/// </summary>
public sealed class ModelTreeSelectionBehavior : Behavior<ModelTree>
{
    public static readonly DependencyProperty SelectionChangedCommandProperty = DependencyProperty.Register(
        nameof(SelectionChangedCommand), typeof(ICommand), typeof(ModelTreeSelectionBehavior),
        new PropertyMetadata(null));

    public ICommand? SelectionChangedCommand
    {
        get => (ICommand?)GetValue(SelectionChangedCommandProperty);
        set => SetValue(SelectionChangedCommandProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.SelectionChanged += OnSelectionChanged;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.SelectionChanged -= OnSelectionChanged;
        base.OnDetaching();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = AssociatedObject.GetSelectedNodes();
        if (SelectionChangedCommand?.CanExecute(selected) == true)
        {
            SelectionChangedCommand.Execute(selected);
        }
    }
}
