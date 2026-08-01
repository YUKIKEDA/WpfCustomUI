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
