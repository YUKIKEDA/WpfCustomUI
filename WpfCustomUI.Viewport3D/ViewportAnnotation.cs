using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WpfCustomUI.Viewport3D;

/// <summary>
/// ビューポート上の注釈 1 件(spec 6.20.3)。<see cref="WcuViewport.Annotations"/> に追加すると
/// チップ(ラベル)+リーダーラインのオーバーレイとして描画される。
/// <para>
/// アンカーは 2 種類:
/// - **節点バインド**(<see cref="Mesh"/> と <see cref="NodeIndex"/> を設定): 変形表示・
///   フレーム再生に毎フレーム追従する。プローブの既定はこちら。メッシュが非表示のとき、
///   断面カットで節点がクリップされたときは自動で隠れる。
/// - **自由 3D 点**(<see cref="Mesh"/> = null、<see cref="X"/>/<see cref="Y"/>/<see cref="Z"/> を設定):
///   モデル座標に固定。変形追従・自動非表示はしない(寸法線的な用途)。
/// </para>
/// </summary>
public sealed class ViewportAnnotation : INotifyPropertyChanged
{
    private ViewportMesh? _mesh;
    private int _nodeIndex = -1;
    private double _x;
    private double _y;
    private double _z;
    private string _text = "";
    private object? _tag;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>節点バインドの対象メッシュ。null なら自由 3D 点アンカー。</summary>
    public ViewportMesh? Mesh
    {
        get => _mesh;
        set => SetField(ref _mesh, value);
    }

    /// <summary>節点バインドの節点インデックス(<see cref="Mesh"/> 設定時に有効)。</summary>
    public int NodeIndex
    {
        get => _nodeIndex;
        set => SetField(ref _nodeIndex, value);
    }

    /// <summary>自由 3D 点アンカーのモデル座標 X(<see cref="Mesh"/> = null のとき使用)。</summary>
    public double X
    {
        get => _x;
        set => SetField(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => SetField(ref _y, value);
    }

    public double Z
    {
        get => _z;
        set => SetField(ref _z, value);
    }

    /// <summary>ラベルに表示する文字列(複数行可)。</summary>
    public string Text
    {
        get => _text;
        set => SetField(ref _text, value ?? "");
    }

    /// <summary>アプリ任意の関連データ(プローブ由来なら <see cref="ProbeResult"/> が入る)。</summary>
    public object? Tag
    {
        get => _tag;
        set => SetField(ref _tag, value);
    }

    /// <summary>
    /// ラベル文字列を返す。ListBox 等にそのままバインドしたときの表示・UI Automation の
    /// Name(テスト自動化)・デバッガ表示が注釈内容になる。
    /// </summary>
    public override string ToString() => _text;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
