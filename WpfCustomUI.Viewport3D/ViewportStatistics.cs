namespace WpfCustomUI.Viewport3D;

/// <summary>
/// ビューポートの統計スナップショット(spec 6.22.5)。<see cref="WcuViewport.GetStatistics"/> が返す。
/// フレーム時間の絶対値は環境(GPU / WARP)依存のため、性能検証では相対比較に使うこと。
/// </summary>
/// <param name="TriangleCount">全パーツの表面三角形数の合計。</param>
/// <param name="VertexCount">全パーツの節点数の合計(チャンク境界の重複は含まない)。</param>
/// <param name="MeshCount">GPU リソースを構築済みのパーツ数。</param>
/// <param name="ChunkCount">頂点/インデックスバッファのチャンク総数(spec 6.22.2)。</param>
/// <param name="EdgeSkippedMeshCount"><see cref="WcuViewport.EdgeExtractionLimit"/> 超過でエッジ抽出をスキップしたパーツ数(spec 6.22.4)。</param>
/// <param name="LastGeometryBuildTime">直近のジオメトリ構築(float 化・法線・チャンク分割・GPU 転送)に掛かった時間。</param>
/// <param name="LastRenderTime">直近の 1 フレーム描画に掛かった時間(CPU 側計測、コマンド発行+Flush を含む)。</param>
public readonly record struct ViewportStatistics(
    long TriangleCount,
    long VertexCount,
    int MeshCount,
    int ChunkCount,
    int EdgeSkippedMeshCount,
    TimeSpan LastGeometryBuildTime,
    TimeSpan LastRenderTime);
