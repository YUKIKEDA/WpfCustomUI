namespace WpfCustomUI.Viewport3D;

/// <summary>
/// プローブ 1 回分のヒット情報(spec 6.20.2)。
/// 値補間(重心座標)まではライブラリの責務、単位・書式などの物理的な意味づけは
/// アプリの責務(<see cref="WcuViewport.ProbeLabelFormatter"/> で差し替える)。
/// </summary>
/// <param name="Mesh">ヒットしたメッシュ。</param>
/// <param name="TriangleIndex">ヒットした三角形インデックス(TriangleIndices / 3 単位)。</param>
/// <param name="NodeIndex">ヒット点に最も寄与する節点(重心座標が最大の頂点)。注釈のアンカーになる。</param>
/// <param name="X">ヒット点のモデル座標 X(変形前の形状上の位置)。</param>
/// <param name="Y">ヒット点のモデル座標 Y。</param>
/// <param name="Z">ヒット点のモデル座標 Z。</param>
/// <param name="ScalarValue">ヒット点の補間スカラー値(重心座標補間)。ScalarValues が無いメッシュでは null。</param>
/// <param name="NodeScalarValue"><see cref="NodeIndex"/> 節点のスカラー値。ScalarValues が無いメッシュでは null。</param>
public sealed record ProbeResult(
    ViewportMesh Mesh,
    int TriangleIndex,
    int NodeIndex,
    double X,
    double Y,
    double Z,
    double? ScalarValue,
    double? NodeScalarValue);

/// <summary>
/// <see cref="WcuViewport.ProbePicked"/> のイベント引数。
/// <see cref="Handled"/> を true にすると、ライブラリの既定動作(注釈の自動追加)を抑止できる
/// (アプリが独自の注釈管理・表示を行う場合)。
/// </summary>
public sealed class ProbePickedEventArgs(ProbeResult result) : EventArgs
{
    /// <summary>ヒット情報。</summary>
    public ProbeResult Result { get; } = result;

    /// <summary>true にするとライブラリは注釈を自動追加しない。既定 false。</summary>
    public bool Handled { get; set; }
}
