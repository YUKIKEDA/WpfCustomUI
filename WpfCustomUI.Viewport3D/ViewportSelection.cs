namespace WpfCustomUI.Viewport3D;

/// <summary>
/// ビューポートの選択状態モデル(spec 6.17.3)。
/// <para>
/// パーツ / 面(三角形インデックス) / 節点(頂点インデックス)の選択集合を保持し、
/// 変更を <see cref="Changed"/> で通知する。<see cref="WcuViewport"/> はこのモデルを購読して
/// ハイライトを描画するため、アプリがプログラムから選択を操作(ModelTree 連動など)しても
/// 表示は自動で追従する。逆に FEM 実体への逆引き(三角形→要素等)はアプリの責務。
/// </para>
/// <para>
/// 連続した複数操作は <see cref="BeginUpdate"/> / <see cref="EndUpdate"/> で囲むと
/// 通知が 1 回にまとめられる(クリック置換選択の Clear+Add 等)。UI スレッド専用。
/// </para>
/// </summary>
public sealed class ViewportSelection
{
    private readonly HashSet<ViewportMesh> _parts = [];
    private readonly Dictionary<ViewportMesh, HashSet<int>> _faces = [];
    private readonly Dictionary<ViewportMesh, HashSet<int>> _nodes = [];

    private int _updateDepth;
    private bool _changedDuringUpdate;

    /// <summary>選択内容が変わったときに発火する。</summary>
    public event EventHandler? Changed;

    /// <summary>選択中のパーツ。</summary>
    public IReadOnlyCollection<ViewportMesh> SelectedParts => _parts;

    /// <summary>面選択を持つメッシュの一覧。</summary>
    public IReadOnlyCollection<ViewportMesh> MeshesWithFaceSelection => _faces.Keys;

    /// <summary>節点選択を持つメッシュの一覧。</summary>
    public IReadOnlyCollection<ViewportMesh> MeshesWithNodeSelection => _nodes.Keys;

    /// <summary>選択中パーツ数。</summary>
    public int PartCount => _parts.Count;

    /// <summary>選択中の面(三角形)総数。</summary>
    public int FaceCount => _faces.Values.Sum(s => s.Count);

    /// <summary>選択中の節点総数。</summary>
    public int NodeCount => _nodes.Values.Sum(s => s.Count);

    public bool IsEmpty => _parts.Count == 0 && _faces.Count == 0 && _nodes.Count == 0;

    /// <summary>指定メッシュの選択中三角形インデックス。</summary>
    public IReadOnlyCollection<int> GetSelectedFaces(ViewportMesh mesh) =>
        _faces.TryGetValue(mesh, out var set) ? set : [];

    /// <summary>指定メッシュの選択中節点インデックス。</summary>
    public IReadOnlyCollection<int> GetSelectedNodes(ViewportMesh mesh) =>
        _nodes.TryGetValue(mesh, out var set) ? set : [];

    public bool IsPartSelected(ViewportMesh mesh) => _parts.Contains(mesh);

    public bool IsFaceSelected(ViewportMesh mesh, int triangleIndex) =>
        _faces.TryGetValue(mesh, out var set) && set.Contains(triangleIndex);

    public bool IsNodeSelected(ViewportMesh mesh, int nodeIndex) =>
        _nodes.TryGetValue(mesh, out var set) && set.Contains(nodeIndex);

    // ================= 変更操作 =================

    /// <summary>通知の一時停止を開始する(入れ子可)。</summary>
    public void BeginUpdate() => _updateDepth++;

    /// <summary>通知の一時停止を解除し、停止中に変更があれば 1 回だけ通知する。</summary>
    public void EndUpdate()
    {
        if (_updateDepth == 0)
        {
            return;
        }

        if (--_updateDepth == 0 && _changedDuringUpdate)
        {
            _changedDuringUpdate = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Clear()
    {
        if (IsEmpty)
        {
            return;
        }

        _parts.Clear();
        _faces.Clear();
        _nodes.Clear();
        RaiseChanged();
    }

    public void AddPart(ViewportMesh mesh)
    {
        if (_parts.Add(mesh))
        {
            RaiseChanged();
        }
    }

    public void RemovePart(ViewportMesh mesh)
    {
        if (_parts.Remove(mesh))
        {
            RaiseChanged();
        }
    }

    public void TogglePart(ViewportMesh mesh)
    {
        if (!_parts.Remove(mesh))
        {
            _parts.Add(mesh);
        }

        RaiseChanged();
    }

    public void AddFace(ViewportMesh mesh, int triangleIndex) => AddFaces(mesh, [triangleIndex]);

    public void AddFaces(ViewportMesh mesh, IEnumerable<int> triangleIndices)
    {
        if (!_faces.TryGetValue(mesh, out var set))
        {
            set = [];
            _faces[mesh] = set;
        }

        var changed = false;
        foreach (var index in triangleIndices)
        {
            changed |= set.Add(index);
        }

        if (set.Count == 0)
        {
            _faces.Remove(mesh);
        }

        if (changed)
        {
            RaiseChanged();
        }
    }

    public void RemoveFace(ViewportMesh mesh, int triangleIndex)
    {
        if (_faces.TryGetValue(mesh, out var set) && set.Remove(triangleIndex))
        {
            if (set.Count == 0)
            {
                _faces.Remove(mesh);
            }

            RaiseChanged();
        }
    }

    public void ToggleFace(ViewportMesh mesh, int triangleIndex)
    {
        if (IsFaceSelected(mesh, triangleIndex))
        {
            RemoveFace(mesh, triangleIndex);
        }
        else
        {
            AddFace(mesh, triangleIndex);
        }
    }

    public void AddNode(ViewportMesh mesh, int nodeIndex) => AddNodes(mesh, [nodeIndex]);

    public void AddNodes(ViewportMesh mesh, IEnumerable<int> nodeIndices)
    {
        if (!_nodes.TryGetValue(mesh, out var set))
        {
            set = [];
            _nodes[mesh] = set;
        }

        var changed = false;
        foreach (var index in nodeIndices)
        {
            changed |= set.Add(index);
        }

        if (set.Count == 0)
        {
            _nodes.Remove(mesh);
        }

        if (changed)
        {
            RaiseChanged();
        }
    }

    public void RemoveNode(ViewportMesh mesh, int nodeIndex)
    {
        if (_nodes.TryGetValue(mesh, out var set) && set.Remove(nodeIndex))
        {
            if (set.Count == 0)
            {
                _nodes.Remove(mesh);
            }

            RaiseChanged();
        }
    }

    public void ToggleNode(ViewportMesh mesh, int nodeIndex)
    {
        if (IsNodeSelected(mesh, nodeIndex))
        {
            RemoveNode(mesh, nodeIndex);
        }
        else
        {
            AddNode(mesh, nodeIndex);
        }
    }

    /// <summary>
    /// 指定メッシュ集合に含まれないメッシュの選択エントリを取り除く
    /// (MeshSource からのパーツ削除に選択状態を追従させる)。
    /// </summary>
    internal void PruneTo(IReadOnlyCollection<ViewportMesh> existingMeshes)
    {
        var changed = _parts.RemoveWhere(m => !existingMeshes.Contains(m)) > 0;

        foreach (var stale in _faces.Keys.Where(m => !existingMeshes.Contains(m)).ToList())
        {
            _faces.Remove(stale);
            changed = true;
        }

        foreach (var stale in _nodes.Keys.Where(m => !existingMeshes.Contains(m)).ToList())
        {
            _nodes.Remove(stale);
            changed = true;
        }

        if (changed)
        {
            RaiseChanged();
        }
    }

    private void RaiseChanged()
    {
        if (_updateDepth > 0)
        {
            _changedDuringUpdate = true;
        }
        else
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
