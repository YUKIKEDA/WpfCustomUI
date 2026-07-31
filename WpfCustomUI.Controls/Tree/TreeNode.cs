using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace WpfCustomUI.Controls;

/// <summary>
/// <see cref="ITreeNode"/> の通知実装済み基底クラス。
/// アプリはこれを継承(またはそのまま使用)すれば ModelTree に即接続できる。
/// </summary>
public class TreeNode : ITreeNode
{
    private string _name = string.Empty;
    private bool _isExpanded;
    private bool? _isVisible = true;
    private bool _isSelected;
    private Geometry? _icon;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ITreeNode> Children { get; } = [];

    IEnumerable<ITreeNode> ITreeNode.Children => Children;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public bool? IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public Geometry? Icon
    {
        get => _icon;
        set => SetField(ref _icon, value);
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
