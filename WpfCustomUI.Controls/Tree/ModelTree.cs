using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace WpfCustomUI.Controls;

/// <summary>
/// CAE モデルツリー(spec 6.3)。
/// 階層を <see cref="FlatTreeSource"/> でフラットなリストに射影し、
/// 仮想化・複数選択(Shift 範囲選択含む)・キーボード操作を内部 ListBox の標準機能で実現する。
/// 表示/非表示トグル(目アイコン)は三状態の自動伝播を内蔵。
/// </summary>
[TemplatePart(Name = PartList, Type = typeof(ListBox))]
public class ModelTree : Control
{
    private const string PartList = "PART_List";

    /// <summary>テンプレート内の目アイコンボタンの名前(クリック判定に使用)。</summary>
    internal const string VisibilityTogglePartName = "PART_VisibilityToggle";

    private ListBox? _list;
    private FlatTreeSource? _source;

    static ModelTree()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ModelTree), new FrameworkPropertyMetadata(typeof(ModelTree)));
    }

    public ModelTree()
    {
        // 目アイコンは仮想化される行テンプレート内にあるため、個別購読ではなく
        // バブリングする Click をコントロール側で一括処理する
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnItemButtonClick));
    }

    /// <summary>内部 ListBox の選択変更をそのまま転送するイベント。</summary>
    public event SelectionChangedEventHandler? SelectionChanged;

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable<ITreeNode>), typeof(ModelTree),
        new PropertyMetadata(null, OnItemsSourceChanged));

    /// <summary>ルートノードのコレクション。INotifyCollectionChanged 実装ならルート増減にも追従する。</summary>
    public IEnumerable<ITreeNode>? ItemsSource
    {
        get => (IEnumerable<ITreeNode>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty SelectionModeProperty =
        ListBox.SelectionModeProperty.AddOwner(typeof(ModelTree),
            new FrameworkPropertyMetadata(SelectionMode.Extended));

    public SelectionMode SelectionMode
    {
        get => (SelectionMode)GetValue(SelectionModeProperty);
        set => SetValue(SelectionModeProperty, value);
    }

    public static readonly DependencyProperty ShowVisibilityTogglesProperty = DependencyProperty.Register(
        nameof(ShowVisibilityToggles), typeof(bool), typeof(ModelTree), new PropertyMetadata(true));

    /// <summary>表示/非表示トグル(目アイコン)列を表示するか。</summary>
    public bool ShowVisibilityToggles
    {
        get => (bool)GetValue(ShowVisibilityTogglesProperty);
        set => SetValue(ShowVisibilityTogglesProperty, value);
    }

    private static readonly DependencyPropertyKey FlatItemsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(FlatItems), typeof(ReadOnlyObservableCollection<FlatTreeItem>), typeof(ModelTree),
        new PropertyMetadata(null));

    public static readonly DependencyProperty FlatItemsProperty = FlatItemsPropertyKey.DependencyProperty;

    /// <summary>フラット化された表示行(テンプレートの ListBox が使用。読み取り専用)。</summary>
    public ReadOnlyObservableCollection<FlatTreeItem>? FlatItems =>
        (ReadOnlyObservableCollection<FlatTreeItem>?)GetValue(FlatItemsProperty);

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var tree = (ModelTree)d;
        tree._source?.Dispose();
        tree._source = e.NewValue is IEnumerable<ITreeNode> roots ? new FlatTreeSource(roots) : null;
        tree.SetValue(FlatItemsPropertyKey, tree._source?.Items);
    }

    /// <summary>ノードと全子孫の表示/非表示をトグルし、祖先の混在状態を再計算する。</summary>
    public void ToggleVisibility(ITreeNode node) => _source?.ToggleVisibility(node);

    /// <summary>ノードと全子孫の表示/非表示を設定し、祖先の混在状態を再計算する。</summary>
    public void SetVisibility(ITreeNode node, bool isVisible) => _source?.SetVisibility(node, isVisible);

    /// <summary>選択中のノードを木全体(折りたたまれた枝を含む)から収集する。</summary>
    public IReadOnlyList<ITreeNode> GetSelectedNodes()
    {
        var result = new List<ITreeNode>();
        if (ItemsSource is not null)
        {
            Collect(ItemsSource);
        }

        return result;

        void Collect(IEnumerable<ITreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.IsSelected)
                {
                    result.Add(node);
                }

                Collect(node.Children);
            }
        }
    }

    public override void OnApplyTemplate()
    {
        if (_list is not null)
        {
            _list.SelectionChanged -= OnListSelectionChanged;
        }

        base.OnApplyTemplate();
        _list = GetTemplateChild(PartList) as ListBox;
        if (_list is not null)
        {
            _list.SelectionChanged += OnListSelectionChanged;
        }
    }

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SelectionChanged?.Invoke(this, e);

    private void OnItemButtonClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { Name: VisibilityTogglePartName } element
            && element.DataContext is FlatTreeItem item)
        {
            _source?.ToggleVisibility(item.Node);
            e.Handled = true;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled || _list?.SelectedItem is not FlatTreeItem item)
        {
            return;
        }

        // Left/Right は内部 ListBox の方向ナビゲーションに取られる前に
        // ツリー標準の展開/折りたたみ操作として処理する
        if (e.Key == Key.Right)
        {
            if (item.HasChildren && !item.Node.IsExpanded)
            {
                item.Node.IsExpanded = true;
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Left)
        {
            if (item.HasChildren && item.Node.IsExpanded)
            {
                item.Node.IsExpanded = false;
                e.Handled = true;
            }
            else if (item.Parent is not null && _source is not null && _list is not null)
            {
                var parentItem = _source.Items.FirstOrDefault(i => ReferenceEquals(i.Node, item.Parent));
                if (parentItem is not null)
                {
                    _list.UnselectAll();
                    _list.SelectedItem = parentItem;
                    FocusItem(parentItem);
                    e.Handled = true;
                }
            }
        }
    }

    protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseRightButtonDown(e);

        // 右クリックで未選択の行を単独選択してからコンテキストメニューを開く(CAE ツリーの定番挙動)
        var container = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (container is { IsSelected: false } && _list is not null)
        {
            _list.UnselectAll();
            container.IsSelected = true;
            container.Focus();
        }
    }

    private void FocusItem(FlatTreeItem item)
    {
        if (_list is null)
        {
            return;
        }

        _list.ScrollIntoView(item);
        _list.UpdateLayout();
        (_list.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem)?.Focus();
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null and not T)
        {
            current = current is Visual or Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return current as T;
    }
}
