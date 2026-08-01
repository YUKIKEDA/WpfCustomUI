using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace WpfCustomUI.Viewport3D;

/// <summary>
/// <see cref="WcuViewport"/> に表示する三角形メッシュ1パーツ分のモデル(spec 6.16.3)。
/// <para>
/// 描画レベルの表現(三角形+節点スカラー)であり、FEM 要素からの表面抽出・三角形分割は
/// アプリの責務。座標は double で受け、ライブラリ内部でモデル中心へ再センタリングしてから
/// float 化して GPU に送る(大座標値での精度ジッタ対策)。
/// </para>
/// <para>
/// 更新は Charts の ChartSeries と同じ流儀で「配列インスタンスの差し替え」。
/// 配列の中身を書き換えても検知されない(描画中のデータ競合を避けるための仕様)。
/// </para>
/// </summary>
public sealed class ViewportMesh : INotifyPropertyChanged
{
    private string? _name;
    private double[] _positions = [];
    private int[] _triangleIndices = [];
    private double[]? _scalarValues;
    private double[]? _displacements;
    private Color _color = Color.FromRgb(0xB0, 0xB4, 0xBC);
    private bool _isVisible = true;
    private bool _showEdges = true;
    private double _opacity = 1.0;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>パーツ名(凡例・デバッグ用)。</summary>
    public string? Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    /// <summary>節点座標。x0,y0,z0, x1,y1,z1, ... の平坦な配列(長さは 3 の倍数)。</summary>
    public double[] Positions
    {
        get => _positions;
        set => SetField(ref _positions, value ?? []);
    }

    /// <summary>三角形の節点インデックス。3 個で 1 三角形。</summary>
    public int[] TriangleIndices
    {
        get => _triangleIndices;
        set => SetField(ref _triangleIndices, value ?? []);
    }

    /// <summary>
    /// 節点スカラー値(コンター表示用)。節点数と同じ長さ。
    /// null ならこのパーツは単色(<see cref="Color"/>)で描画される。
    /// </summary>
    public double[]? ScalarValues
    {
        get => _scalarValues;
        set => SetField(ref _scalarValues, value);
    }

    /// <summary>
    /// 節点変位ベクトル(変形表示用、spec 6.18)。ux0,uy0,uz0, ux1,... の平坦な配列(長さ 3×節点数)。
    /// null または長さ不正なら変位ゼロとして扱う。表示上の変形量は
    /// <see cref="WcuViewport.DeformationScale"/> との積で決まる(GPU 側で適用)。
    /// 差し替えはジオメトリ再構築でなく変位バッファの部分更新で処理されるため、
    /// フレーム再生(過渡応答)で毎フレーム差し替えても効率的に動く。
    /// </summary>
    public double[]? Displacements
    {
        get => _displacements;
        set => SetField(ref _displacements, value);
    }

    /// <summary>単色表示時のパーツ色(コンター無効時・スカラーなし時に使用)。</summary>
    public Color Color
    {
        get => _color;
        set => SetField(ref _color, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value);
    }

    /// <summary>メッシュエッジ(ワイヤフレーム)の重畳表示。</summary>
    public bool ShowEdges
    {
        get => _showEdges;
        set => SetField(ref _showEdges, value);
    }

    /// <summary>不透明度(0〜1)。1 未満で半透明描画(簡易・ソートなし)。</summary>
    public double Opacity
    {
        get => _opacity;
        set => SetField(ref _opacity, Math.Clamp(value, 0.0, 1.0));
    }

    /// <summary>節点数。</summary>
    public int VertexCount => _positions.Length / 3;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
