namespace WpfCustomUI.Viewport3D;

/// <summary>カメラの投影方式(spec 6.16.2)。CAE は寸法確認に平行投影を多用する。</summary>
public enum ViewportProjection
{
    /// <summary>透視投影。</summary>
    Perspective,

    /// <summary>平行投影(正射影)。</summary>
    Orthographic,
}

/// <summary>ターンテーブル回転の上方向軸(spec 6.16.4)。CAE の慣例に合わせ既定は Z-up。</summary>
public enum ViewportUpAxis
{
    /// <summary>Z 軸が上(CAE / CAD の主流)。</summary>
    ZUp,

    /// <summary>Y 軸が上(DCC / ゲーム系)。</summary>
    YUp,
}

/// <summary>
/// 左ボタンによるピッキングの選択粒度(spec 6.17.2)。
/// 面=三角形 / 節点は描画レベルのインデックスで返され、FEM 実体への逆引きはアプリの責務。
/// </summary>
public enum ViewportPickMode
{
    /// <summary>ピッキング無効(左ボタンは何もしない)。</summary>
    None,

    /// <summary>パーツ(ViewportMesh)単位の選択。</summary>
    Part,

    /// <summary>面(三角形)単位の選択。</summary>
    Face,

    /// <summary>節点(頂点)単位の選択。</summary>
    Node,
}

/// <summary>標準視点(spec 6.17.5)。Z-up では Front = -Y 側から見る CAD 慣例に従う。</summary>
public enum ViewportStandardView
{
    Front,
    Back,
    Left,
    Right,
    Top,
    Bottom,
    Isometric,
}
