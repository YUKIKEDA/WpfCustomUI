namespace WpfCustomUI.Viewport3D;

/// <summary>
/// ホバープリハイライトの CPU 側純粋関数(spec 6.24.3)。
/// シーン静止時にキャプチャした ID バッファ(1 ピクセルにつき uint ×2:
/// R=パーツ ID+1 / G=グローバル三角形インデックス)を参照する。
/// </summary>
internal static class ViewportHover
{
    /// <summary>1 ピクセルあたりの uint 数(R32G32_UInt)。</summary>
    public const int UintsPerPixel = 2;

    /// <summary>
    /// ID バッファから指定ピクセルのヒットを読む。範囲外・背景(パーツ ID 0)は null。
    /// 戻り値の MeshIndex はキャプチャ時の可視メッシュリスト内の位置。
    /// </summary>
    public static (int MeshIndex, int TriangleIndex)? ReadId(
        uint[] buffer, int width, int height, int x, int y)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            return null;
        }

        var offset = (y * width + x) * UintsPerPixel;
        if (offset + 1 >= buffer.Length)
        {
            return null;
        }

        var part = buffer[offset];
        if (part == 0)
        {
            return null;
        }

        return ((int)part - 1, (int)buffer[offset + 1]);
    }
}
