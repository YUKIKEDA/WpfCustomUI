using WpfCustomUI.Controls;
using Xunit;

namespace WpfCustomUI.Controls.Tests.Tree;

public class FlatTreeSourceTests
{
    private static TreeNode N(string name, params TreeNode[] children)
    {
        var node = new TreeNode { Name = name };
        foreach (var child in children)
        {
            node.Children.Add(child);
        }

        return node;
    }

    private static string[] Names(FlatTreeSource source) =>
        source.Items.Select(i => i.Node.Name).ToArray();

    // ---------------- フラット化 ----------------

    [Fact]
    public void InitialFlatten_CollapsedRoots_ShowsRootsOnly()
    {
        var roots = new[] { N("A", N("A1"), N("A2")), N("B") };
        using var source = new FlatTreeSource(roots);

        Assert.Equal(["A", "B"], Names(source));
    }

    [Fact]
    public void InitialFlatten_RespectsIsExpanded()
    {
        var a = N("A", N("A1", N("A1a")), N("A2"));
        a.IsExpanded = true;
        using var source = new FlatTreeSource([a, N("B")]);

        Assert.Equal(["A", "A1", "A2", "B"], Names(source));
    }

    [Fact]
    public void Levels_AreComputedFromDepth()
    {
        var a = N("A", N("A1", N("A1a")));
        a.IsExpanded = true;
        ((TreeNode)a.Children[0]).IsExpanded = true;
        using var source = new FlatTreeSource([a]);

        Assert.Equal([0, 1, 2], source.Items.Select(i => i.Level).ToArray());
        Assert.Null(source.Items[0].Parent);
        Assert.Same(a, source.Items[1].Parent);
    }

    [Fact]
    public void HasChildren_ReflectsChildPresence()
    {
        var a = N("A", N("A1"));
        a.IsExpanded = true;
        using var source = new FlatTreeSource([a]);

        Assert.True(source.Items[0].HasChildren);
        Assert.False(source.Items[1].HasChildren);
    }

    // ---------------- 展開/折りたたみ ----------------

    [Fact]
    public void Expand_InsertsChildrenAfterParent()
    {
        var a = N("A", N("A1"), N("A2"));
        using var source = new FlatTreeSource([a, N("B")]);

        a.IsExpanded = true;

        Assert.Equal(["A", "A1", "A2", "B"], Names(source));
    }

    [Fact]
    public void Collapse_RemovesWholeSubtree_IncludingExpandedDescendants()
    {
        var a1 = N("A1", N("A1a"), N("A1b"));
        a1.IsExpanded = true;
        var a = N("A", a1, N("A2"));
        a.IsExpanded = true;
        using var source = new FlatTreeSource([a, N("B")]);

        Assert.Equal(["A", "A1", "A1a", "A1b", "A2", "B"], Names(source));

        a.IsExpanded = false;

        Assert.Equal(["A", "B"], Names(source));
    }

    [Fact]
    public void Expand_AfterCollapse_RestoresDescendantExpansionState()
    {
        var a1 = N("A1", N("A1a"));
        a1.IsExpanded = true;
        var a = N("A", a1);
        a.IsExpanded = true;
        using var source = new FlatTreeSource([a]);

        a.IsExpanded = false;
        a.IsExpanded = true;

        // A1 の展開状態は保持されているので A1a も再表示される
        Assert.Equal(["A", "A1", "A1a"], Names(source));
    }

    // ---------------- 子コレクションの動的変更 ----------------

    [Fact]
    public void ChildAdded_WhileExpanded_AppearsInList()
    {
        var a = N("A", N("A1"));
        a.IsExpanded = true;
        using var source = new FlatTreeSource([a, N("B")]);

        a.Children.Add(N("A2"));

        Assert.Equal(["A", "A1", "A2", "B"], Names(source));
    }

    [Fact]
    public void ChildRemoved_WhileExpanded_DisappearsFromList()
    {
        var a1 = N("A1");
        var a = N("A", a1, N("A2"));
        a.IsExpanded = true;
        using var source = new FlatTreeSource([a]);

        a.Children.Remove(a1);

        Assert.Equal(["A", "A2"], Names(source));
    }

    [Fact]
    public void ChildAdded_ToLeaf_UpdatesHasChildren()
    {
        var a = N("A");
        using var source = new FlatTreeSource([a]);
        Assert.False(source.Items[0].HasChildren);

        a.Children.Add(N("A1"));

        Assert.True(source.Items[0].HasChildren);
        // 折りたたまれたままなのでリストには現れない
        Assert.Equal(["A"], Names(source));
    }

    [Fact]
    public void RootsCollectionChanged_RebuildsList()
    {
        var roots = new System.Collections.ObjectModel.ObservableCollection<ITreeNode> { N("A") };
        using var source = new FlatTreeSource(roots);

        roots.Add(N("B"));

        Assert.Equal(["A", "B"], Names(source));
    }

    // ---------------- 表示/非表示の三状態伝播 ----------------

    [Fact]
    public void ToggleVisibility_OnParent_PropagatesToAllDescendants()
    {
        var a = N("A", N("A1", N("A1a")), N("A2"));
        a.IsExpanded = true;
        using var source = new FlatTreeSource([a]);

        source.ToggleVisibility(a);

        Assert.False(a.IsVisible);
        Assert.All(a.Children, c => Assert.False(c.IsVisible));
        Assert.False(a.Children[0].Children.Single().IsVisible);
    }

    [Fact]
    public void HideOneChild_ParentBecomesMixed()
    {
        var a1 = N("A1");
        var a = N("A", a1, N("A2"));
        a.IsExpanded = true;
        using var source = new FlatTreeSource([a]);

        source.ToggleVisibility(a1);

        Assert.False(a1.IsVisible);
        Assert.Null(a.IsVisible); // 混在
    }

    [Fact]
    public void HideAllChildren_ParentBecomesHidden()
    {
        var a1 = N("A1");
        var a2 = N("A2");
        var a = N("A", a1, a2);
        a.IsExpanded = true;
        using var source = new FlatTreeSource([a]);

        source.ToggleVisibility(a1);
        source.ToggleVisibility(a2);

        Assert.False(a.IsVisible);
    }

    [Fact]
    public void MixedState_PropagatesUpToGrandparent()
    {
        var leaf = N("A1a");
        var a1 = N("A1", leaf, N("A1b"));
        a1.IsExpanded = true;
        var a = N("A", a1);
        a.IsExpanded = true;
        using var source = new FlatTreeSource([a]);

        source.ToggleVisibility(leaf);

        Assert.Null(a1.IsVisible);
        Assert.Null(a.IsVisible);
    }

    [Fact]
    public void ToggleHiddenNode_ShowsSubtree_AndRestoresAncestors()
    {
        var a1 = N("A1", N("A1a"));
        a1.IsExpanded = true;
        var a = N("A", a1, N("A2"));
        a.IsExpanded = true;
        using var source = new FlatTreeSource([a]);

        source.ToggleVisibility(a1); // 非表示 → 親は混在
        Assert.Null(a.IsVisible);

        source.ToggleVisibility(a1); // 再表示

        Assert.True(a1.IsVisible);
        Assert.True(a1.Children.Single().IsVisible);
        Assert.True(a.IsVisible); // 全子が表示に戻ったので親も表示
    }

    [Fact]
    public void ToggleMixedParent_HidesEverything()
    {
        var a1 = N("A1");
        var a = N("A", a1, N("A2"));
        a.IsExpanded = true;
        using var source = new FlatTreeSource([a]);

        source.ToggleVisibility(a1); // 親は混在(null)になる
        source.ToggleVisibility(a);  // 混在の親をクリック → 全部非表示

        Assert.False(a.IsVisible);
        Assert.All(a.Children, c => Assert.False(c.IsVisible));
    }

    [Fact]
    public void SetVisibility_OnCollapsedSubtree_StillPropagates()
    {
        // 折りたたまれていて画面に出ていない子孫にも伝播する
        var a = N("A", N("A1", N("A1a")));
        using var source = new FlatTreeSource([a]); // A は折りたたみ状態

        source.SetVisibility(a, false);

        Assert.False(a.Children[0].IsVisible);
        Assert.False(a.Children[0].Children.Single().IsVisible);
    }
}
