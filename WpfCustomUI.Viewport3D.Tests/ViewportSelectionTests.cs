using WpfCustomUI.Viewport3D;

namespace WpfCustomUI.Viewport3D.Tests;

public class ViewportSelectionTests
{
    private static ViewportMesh CreateMesh() => new() { Name = "test" };

    [Fact]
    public void NewSelection_IsEmpty()
    {
        var selection = new ViewportSelection();

        Assert.True(selection.IsEmpty);
        Assert.Equal(0, selection.PartCount);
        Assert.Equal(0, selection.FaceCount);
        Assert.Equal(0, selection.NodeCount);
    }

    [Fact]
    public void AddFace_UpdatesCountAndContains()
    {
        var selection = new ViewportSelection();
        var mesh = CreateMesh();

        selection.AddFace(mesh, 5);
        selection.AddFace(mesh, 7);
        selection.AddFace(mesh, 5); // 重複は無視

        Assert.Equal(2, selection.FaceCount);
        Assert.True(selection.IsFaceSelected(mesh, 5));
        Assert.True(selection.IsFaceSelected(mesh, 7));
        Assert.False(selection.IsFaceSelected(mesh, 6));
    }

    [Fact]
    public void ToggleFace_AddsThenRemoves()
    {
        var selection = new ViewportSelection();
        var mesh = CreateMesh();

        selection.ToggleFace(mesh, 3);
        Assert.True(selection.IsFaceSelected(mesh, 3));

        selection.ToggleFace(mesh, 3);
        Assert.False(selection.IsFaceSelected(mesh, 3));
        Assert.True(selection.IsEmpty);
    }

    [Fact]
    public void TogglePart_AddsThenRemoves()
    {
        var selection = new ViewportSelection();
        var mesh = CreateMesh();

        selection.TogglePart(mesh);
        Assert.True(selection.IsPartSelected(mesh));

        selection.TogglePart(mesh);
        Assert.False(selection.IsPartSelected(mesh));
    }

    [Fact]
    public void AddNodes_AggregatesAcrossMeshes()
    {
        var selection = new ViewportSelection();
        var meshA = CreateMesh();
        var meshB = CreateMesh();

        selection.AddNodes(meshA, [1, 2, 3]);
        selection.AddNodes(meshB, [4]);

        Assert.Equal(4, selection.NodeCount);
        Assert.Equal(3, selection.GetSelectedNodes(meshA).Count);
        Assert.Single(selection.GetSelectedNodes(meshB));
    }

    [Fact]
    public void Clear_RemovesEverything()
    {
        var selection = new ViewportSelection();
        var mesh = CreateMesh();
        selection.AddPart(mesh);
        selection.AddFace(mesh, 0);
        selection.AddNode(mesh, 0);

        selection.Clear();

        Assert.True(selection.IsEmpty);
    }

    [Fact]
    public void Changed_FiresOncePerMutation()
    {
        var selection = new ViewportSelection();
        var mesh = CreateMesh();
        var count = 0;
        selection.Changed += (_, _) => count++;

        selection.AddFaces(mesh, [1, 2, 3]); // 一括追加は 1 回
        selection.AddFace(mesh, 1);          // 変化なし → 通知なし
        selection.Clear();                   // 1 回

        Assert.Equal(2, count);
    }

    [Fact]
    public void BeginEndUpdate_BatchesNotifications()
    {
        var selection = new ViewportSelection();
        var mesh = CreateMesh();
        var count = 0;
        selection.Changed += (_, _) => count++;

        selection.BeginUpdate();
        selection.AddFace(mesh, 1);
        selection.AddFace(mesh, 2);
        selection.Clear();
        selection.AddNode(mesh, 5);
        selection.EndUpdate();

        Assert.Equal(1, count);
        Assert.Equal(1, selection.NodeCount);
        Assert.Equal(0, selection.FaceCount);
    }

    [Fact]
    public void BeginEndUpdate_NoChange_DoesNotNotify()
    {
        var selection = new ViewportSelection();
        var count = 0;
        selection.Changed += (_, _) => count++;

        selection.BeginUpdate();
        selection.EndUpdate();

        Assert.Equal(0, count);
    }

    [Fact]
    public void PruneTo_RemovesStaleMeshEntries()
    {
        var selection = new ViewportSelection();
        var kept = CreateMesh();
        var removed = CreateMesh();
        selection.AddPart(removed);
        selection.AddFace(removed, 1);
        selection.AddNode(kept, 2);

        selection.PruneTo([kept]);

        Assert.Equal(0, selection.PartCount);
        Assert.Equal(0, selection.FaceCount);
        Assert.Equal(1, selection.NodeCount);
        Assert.True(selection.IsNodeSelected(kept, 2));
    }
}
