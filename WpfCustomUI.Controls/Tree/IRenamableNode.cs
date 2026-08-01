namespace WpfCustomUI.Controls;

/// <summary>
/// インライン名前変更(F2 / <see cref="ModelTree.BeginRename"/>)を受け付けるノードの契約(spec 6.12.3)。
/// <see cref="ITreeNode"/> は無変更のまま、実装した型だけがオプトインで編集可能になる。
/// 名前はモデルへ直接書き込まれ、INotifyPropertyChanged 経由で表示が追従する。
/// </summary>
public interface IRenamableNode : ITreeNode
{
    /// <summary>ツリーに表示する名前(書き込み可)。</summary>
    new string Name { get; set; }
}
