namespace WpfCustomUI.Viewport3D;

/// <summary>
/// 軸平行境界ボックス(double 精度)。再センタリングと Fit 計算の基準(spec 6.16.3)。
/// </summary>
public readonly struct Bounds3D
{
    public static Bounds3D Empty { get; } = new(
        double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity,
        double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);

    public Bounds3D(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        MinX = minX;
        MinY = minY;
        MinZ = minZ;
        MaxX = maxX;
        MaxY = maxY;
        MaxZ = maxZ;
    }

    public double MinX { get; }
    public double MinY { get; }
    public double MinZ { get; }
    public double MaxX { get; }
    public double MaxY { get; }
    public double MaxZ { get; }

    public bool IsEmpty => MinX > MaxX || MinY > MaxY || MinZ > MaxZ;

    public double CenterX => (MinX + MaxX) / 2.0;
    public double CenterY => (MinY + MaxY) / 2.0;
    public double CenterZ => (MinZ + MaxZ) / 2.0;

    /// <summary>境界球の半径(ボックス対角線の半分)。</summary>
    public double Radius
    {
        get
        {
            if (IsEmpty)
            {
                return 0.0;
            }

            var dx = MaxX - MinX;
            var dy = MaxY - MinY;
            var dz = MaxZ - MinZ;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz) / 2.0;
        }
    }

    /// <summary>2 つの境界の合併。</summary>
    public Bounds3D Union(Bounds3D other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        return new Bounds3D(
            Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY), Math.Min(MinZ, other.MinZ),
            Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY), Math.Max(MaxZ, other.MaxZ));
    }
}
