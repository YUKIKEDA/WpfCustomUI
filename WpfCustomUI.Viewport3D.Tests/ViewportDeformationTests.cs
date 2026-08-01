using WpfCustomUI.Viewport3D;

namespace WpfCustomUI.Viewport3D.Tests;

public class ViewportDeformationTests
{
    // ================= GetMaxDisplacementMagnitude =================

    [Fact]
    public void MaxDisplacement_Null_ReturnsZero() =>
        Assert.Equal(0.0, ViewportDeformation.GetMaxDisplacementMagnitude(null));

    [Fact]
    public void MaxDisplacement_Empty_ReturnsZero() =>
        Assert.Equal(0.0, ViewportDeformation.GetMaxDisplacementMagnitude([]));

    [Fact]
    public void MaxDisplacement_ReturnsLargestVectorLength()
    {
        // 節点0: (3,4,0) → 5、節点1: (0,0,2) → 2
        double[] displacements = [3.0, 4.0, 0.0, 0.0, 0.0, 2.0];
        Assert.Equal(5.0, ViewportDeformation.GetMaxDisplacementMagnitude(displacements), 12);
    }

    [Fact]
    public void MaxDisplacement_IgnoresNaNAndTrailingRemainder()
    {
        // NaN を含む節点は無視、末尾の余り(2 要素)も無視される
        double[] displacements = [double.NaN, 1.0, 0.0, 0.0, 1.0, 0.0, 99.0, 99.0];
        Assert.Equal(1.0, ViewportDeformation.GetMaxDisplacementMagnitude(displacements), 12);
    }

    // ================= ComputeSuggestedScale =================

    [Fact]
    public void SuggestedScale_TargetsFractionOfModelSize()
    {
        // 最大変位 0.5、モデル寸法 100、目標 5% → 100×0.05/0.5 = 10
        Assert.Equal(10.0, ViewportDeformation.ComputeSuggestedScale(0.5, 100.0), 12);
    }

    [Fact]
    public void SuggestedScale_CustomFraction()
    {
        Assert.Equal(20.0, ViewportDeformation.ComputeSuggestedScale(0.5, 100.0, 0.1), 12);
    }

    [Theory]
    [InlineData(0.0, 100.0)]  // 変位ゼロ
    [InlineData(1.0, 0.0)]    // 寸法ゼロ
    [InlineData(-1.0, 100.0)] // 不正値
    public void SuggestedScale_DegenerateInput_ReturnsIdentity(double maxDisplacement, double modelSize) =>
        Assert.Equal(1.0, ViewportDeformation.ComputeSuggestedScale(maxDisplacement, modelSize));

    // ================= GetAnimationFactor =================

    [Fact]
    public void AnimationFactor_SineWaveform()
    {
        const double period = 2.0;
        Assert.Equal(0.0, ViewportDeformation.GetAnimationFactor(0.0, period), 12);
        Assert.Equal(1.0, ViewportDeformation.GetAnimationFactor(period / 4.0, period), 12);
        Assert.Equal(0.0, ViewportDeformation.GetAnimationFactor(period / 2.0, period), 12);
        Assert.Equal(-1.0, ViewportDeformation.GetAnimationFactor(3.0 * period / 4.0, period), 12);
        Assert.Equal(0.0, ViewportDeformation.GetAnimationFactor(period, period), 12);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.PositiveInfinity)]
    public void AnimationFactor_InvalidPeriod_ReturnsOne(double period) =>
        Assert.Equal(1.0, ViewportDeformation.GetAnimationFactor(0.7, period));

    // ================= ToDisplacementArray =================

    [Fact]
    public void ToDisplacementArray_Null_ReturnsZeros()
    {
        var result = ViewportDeformation.ToDisplacementArray(null, 3);
        Assert.Equal(9, result.Length);
        Assert.All(result, v => Assert.Equal(0.0f, v));
    }

    [Fact]
    public void ToDisplacementArray_ConvertsValues()
    {
        var result = ViewportDeformation.ToDisplacementArray([1.0, 2.0, 3.0, 4.0, 5.0, 6.0], 2);
        Assert.Equal([1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f], result);
    }

    [Fact]
    public void ToDisplacementArray_ShortInput_PadsWithZeros()
    {
        // 長さ不正(3N 未満)の残りはゼロ(=変位なし)として扱う
        var result = ViewportDeformation.ToDisplacementArray([1.0, 2.0, 3.0], 2);
        Assert.Equal([1.0f, 2.0f, 3.0f, 0.0f, 0.0f, 0.0f], result);
    }

    [Fact]
    public void ToDisplacementArray_LongInput_Truncates()
    {
        var result = ViewportDeformation.ToDisplacementArray([1.0, 2.0, 3.0, 4.0, 5.0, 6.0], 1);
        Assert.Equal([1.0f, 2.0f, 3.0f], result);
    }
}
