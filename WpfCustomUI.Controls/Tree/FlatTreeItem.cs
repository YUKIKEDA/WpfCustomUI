using System.ComponentModel;

namespace WpfCustomUI.Controls;

/// <summary>
/// フラット化リストの1行。ノード本体に階層情報(深さ・親)を付与したもの。
/// <see cref="FlatTreeSource"/> が生成・管理する。
/// </summary>
public sealed class FlatTreeItem : INotifyPropertyChanged
{
    private bool _hasChildren;

    internal FlatTreeItem(ITreeNode node, ITreeNode? parent, int level)
    {
        Node = node;
        Parent = parent;
        Level = level;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ITreeNode Node { get; }

    /// <summary>親ノード。ルートは null。</summary>
    public ITreeNode? Parent { get; }

    /// <summary>ルートを 0 とする深さ。インデント幅の計算に使う。</summary>
    public int Level { get; }

    /// <summary>子を持つか(展開シェブロンの表示判定)。子の増減で更新される。</summary>
    public bool HasChildren
    {
        get => _hasChildren;
        internal set
        {
            if (_hasChildren != value)
            {
                _hasChildren = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasChildren)));
            }
        }
    }
}
