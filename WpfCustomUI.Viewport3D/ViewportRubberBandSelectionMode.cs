namespace WpfCustomUI.Viewport3D;

/// <summary>矩形(ラバーバンド)選択の対象範囲(spec 6.24.4)。</summary>
public enum ViewportRubberBandSelectionMode
{
    /// <summary>見えている要素のみ(GPU ID 領域読み出し、既定)。</summary>
    VisibleOnly,

    /// <summary>
    /// 隠面も含む貫通選択。矩形のスクリーン射影判定を CPU 並列で行う
    /// (面=3 頂点全てが矩形内 / 節点=射影点が矩形内 / パーツ=1 三角形でもかかれば選択)。
    /// 変形表示中は変位適用後の座標で判定し、断面クリップされた要素は除外される。
    /// </summary>
    Through,
}
