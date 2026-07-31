using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace WpfCustomUI.Controls;

/// <summary>
/// ツリー階層をフラットなリストに射影するロジック(spec 6.3)。
/// <list type="bullet">
/// <item>展開中のノードの子孫のみをリストに載せる。IsExpanded の変更に追従。</item>
/// <item>Children が INotifyCollectionChanged なら子の増減にも追従。</item>
/// <item>表示/非表示(目アイコン)の三状態伝播(<see cref="ToggleVisibility"/>)を内蔵。</item>
/// </list>
/// UI に依存しないため単体テスト可能。ModelTree はこのクラスの Items を ListBox に流すだけ。
/// </summary>
public sealed class FlatTreeSource : IDisposable
{
    private readonly ObservableCollection<FlatTreeItem> _items = [];
    private readonly Dictionary<ITreeNode, FlatTreeItem> _map = [];
    private readonly Dictionary<INotifyCollectionChanged, ITreeNode> _childCollectionOwners = [];
    private readonly IEnumerable<ITreeNode> _roots;

    public FlatTreeSource(IEnumerable<ITreeNode> roots)
    {
        _roots = roots;
        Items = new ReadOnlyObservableCollection<FlatTreeItem>(_items);
        if (roots is INotifyCollectionChanged observableRoots)
        {
            observableRoots.CollectionChanged += OnRootsChanged;
        }

        Rebuild();
    }

    /// <summary>現在表示中(祖先が全て展開されている)のノードのフラットリスト。</summary>
    public ReadOnlyObservableCollection<FlatTreeItem> Items { get; }

    public void Dispose()
    {
        if (_roots is INotifyCollectionChanged observableRoots)
        {
            observableRoots.CollectionChanged -= OnRootsChanged;
        }

        DetachAll();
        _items.Clear();
    }

    // ---------------- 表示/非表示の三状態伝播 ----------------

    /// <summary>
    /// 目アイコンのクリック動作。非表示(false)なら表示に、それ以外(表示/混在)なら非表示にする。
    /// </summary>
    public void ToggleVisibility(ITreeNode node) => SetVisibility(node, node.IsVisible == false);

    /// <summary>
    /// ノードと全子孫の可視状態を設定し、祖先の混在状態(null)を再計算する。
    /// </summary>
    public void SetVisibility(ITreeNode node, bool isVisible)
    {
        SetVisibilityRecursive(node, isVisible);

        var current = _map.TryGetValue(node, out var item) ? item.Parent : null;
        while (current is not null)
        {
            current.IsVisible = AggregateVisibility(current);
            current = _map.TryGetValue(current, out var parentItem) ? parentItem.Parent : null;
        }
    }

    private static void SetVisibilityRecursive(ITreeNode node, bool isVisible)
    {
        node.IsVisible = isVisible;
        foreach (var child in node.Children)
        {
            SetVisibilityRecursive(child, isVisible);
        }
    }

    /// <summary>子の可視状態から親の状態を求める(全 true→true / 全 false→false / 混在→null)。</summary>
    private static bool? AggregateVisibility(ITreeNode parent)
    {
        var anyVisible = false;
        var anyHidden = false;
        foreach (var child in parent.Children)
        {
            switch (child.IsVisible)
            {
                case true:
                    anyVisible = true;
                    break;
                case false:
                    anyHidden = true;
                    break;
                default:
                    return null;
            }

            if (anyVisible && anyHidden)
            {
                return null;
            }
        }

        return !anyHidden;
    }

    // ---------------- フラット化 ----------------

    private void OnRootsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        DetachAll();
        _items.Clear();

        var index = 0;
        foreach (var root in _roots)
        {
            index = InsertSubtree(root, parent: null, level: 0, index);
        }
    }

    /// <summary>ノード(と展開されている子孫)を index 位置から挿入し、次の挿入位置を返す。</summary>
    private int InsertSubtree(ITreeNode node, ITreeNode? parent, int level, int index)
    {
        var item = new FlatTreeItem(node, parent, level) { HasChildren = node.Children.Any() };
        _map[node] = item;
        Subscribe(node);
        _items.Insert(index, item);
        index++;

        if (node.IsExpanded)
        {
            foreach (var child in node.Children)
            {
                index = InsertSubtree(child, node, level + 1, index);
            }
        }

        return index;
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ITreeNode node || e.PropertyName != nameof(ITreeNode.IsExpanded))
        {
            return;
        }

        if (!_map.TryGetValue(node, out var item))
        {
            return;
        }

        if (node.IsExpanded)
        {
            ExpandNode(item);
        }
        else
        {
            CollapseNode(item);
        }
    }

    private void ExpandNode(FlatTreeItem item)
    {
        var index = _items.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        // すでに子が実体化済みなら何もしない(冪等)
        if (index + 1 < _items.Count && ReferenceEquals(_items[index + 1].Parent, item.Node))
        {
            return;
        }

        var insert = index + 1;
        foreach (var child in item.Node.Children)
        {
            insert = InsertSubtree(child, item.Node, item.Level + 1, insert);
        }
    }

    private void CollapseNode(FlatTreeItem item)
    {
        var index = _items.IndexOf(item);
        if (index >= 0)
        {
            RemoveDescendants(index, item.Level);
        }
    }

    /// <summary>index 位置のノードの直後に続く「Level がより深い行」を全て取り除く。</summary>
    private void RemoveDescendants(int index, int level)
    {
        while (index + 1 < _items.Count && _items[index + 1].Level > level)
        {
            var removed = _items[index + 1];
            Unsubscribe(removed.Node);
            _map.Remove(removed.Node);
            _items.RemoveAt(index + 1);
        }
    }

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (sender is not INotifyCollectionChanged collection
            || !_childCollectionOwners.TryGetValue(collection, out var node)
            || !_map.TryGetValue(node, out var item))
        {
            return;
        }

        item.HasChildren = node.Children.Any();

        if (!node.IsExpanded)
        {
            return;
        }

        // 展開中の子リスト変更は「取り除いて挿入し直す」で一律に扱う
        // (Move/Replace 含む全パターンを単純・確実に処理するため)
        var index = _items.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        RemoveDescendants(index, item.Level);
        var insert = index + 1;
        foreach (var child in node.Children)
        {
            insert = InsertSubtree(child, node, item.Level + 1, insert);
        }
    }

    // ---------------- 購読管理 ----------------

    private void Subscribe(ITreeNode node)
    {
        node.PropertyChanged += OnNodePropertyChanged;
        if (node.Children is INotifyCollectionChanged observableChildren)
        {
            _childCollectionOwners[observableChildren] = node;
            observableChildren.CollectionChanged += OnChildrenChanged;
        }
    }

    private void Unsubscribe(ITreeNode node)
    {
        node.PropertyChanged -= OnNodePropertyChanged;
        if (node.Children is INotifyCollectionChanged observableChildren)
        {
            observableChildren.CollectionChanged -= OnChildrenChanged;
            _childCollectionOwners.Remove(observableChildren);
        }
    }

    private void DetachAll()
    {
        foreach (var item in _items)
        {
            Unsubscribe(item.Node);
        }

        _map.Clear();
        _childCollectionOwners.Clear();
    }
}
