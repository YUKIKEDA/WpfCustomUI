using System.ComponentModel;
using System.Windows.Media;

namespace WpfCustomUI.Controls;

/// <summary>
/// ModelTree が購読するノードの最小契約(spec 6.3)。
/// 既存の ViewModel 階層がある場合はこのインターフェースを実装して接続する。
/// ゼロから作る場合は通知実装済みの <see cref="TreeNode"/> 基底クラスを継承すればよい。
/// </summary>
public interface ITreeNode : INotifyPropertyChanged
{
    /// <summary>ツリーに表示する名前。</summary>
    string Name { get; }

    /// <summary>
    /// 子ノード。動的な増減を反映するには
    /// <see cref="System.Collections.Specialized.INotifyCollectionChanged"/> を実装した
    /// 同一インスタンスを返し続けること(ObservableCollection 推奨)。
    /// </summary>
    IEnumerable<ITreeNode> Children { get; }

    /// <summary>展開状態。変更通知により ModelTree の表示が追従する。</summary>
    bool IsExpanded { get; set; }

    /// <summary>
    /// 表示/非表示(目アイコン)の三状態。
    /// true=表示 / false=非表示 / null=子孫の状態が混在。
    /// 伝播計算は ModelTree(FlatTreeSource)側が行う。
    /// </summary>
    bool? IsVisible { get; set; }

    /// <summary>選択状態。ModelTree の複数選択と双方向同期する。</summary>
    bool IsSelected { get; set; }

    /// <summary>ノードアイコン(Geometry)。null なら非表示。</summary>
    Geometry? Icon { get; }
}
