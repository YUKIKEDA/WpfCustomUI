using System.Windows.Media;
using WpfCustomUI.Controls;
using Xunit;

namespace WpfCustomUI.Controls.Tests.ColorMaps;

public class ColorMapTests
{
    [Fact]
    public void GetColor_AtEndpoints_ReturnsStopColors()
    {
        Assert.Equal(Color.FromRgb(0x00, 0x00, 0x7F), ColorMap.Jet.GetColor(0.0));
        Assert.Equal(Color.FromRgb(0x7F, 0x00, 0x00), ColorMap.Jet.GetColor(1.0));
    }

    [Fact]
    public void GetColor_AtMidpoint_InterpolatesLinearly()
    {
        var mid = ColorMap.Grayscale.GetColor(0.5);
        Assert.InRange(mid.R, 127, 128);
        Assert.Equal(mid.R, mid.G);
        Assert.Equal(mid.R, mid.B);
    }

    [Fact]
    public void GetColor_OutOfRange_Clamps()
    {
        Assert.Equal(ColorMap.Viridis.GetColor(0.0), ColorMap.Viridis.GetColor(-1.0));
        Assert.Equal(ColorMap.Viridis.GetColor(1.0), ColorMap.Viridis.GetColor(2.0));
    }

    [Fact]
    public void Constructor_LessThanTwoStops_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ColorMap("Bad", (0.0, Colors.Red)));
    }
}

public class ColorScaleTests
{
    [Fact]
    public void Normalize_Linear_MapsRangeToUnitInterval()
    {
        var scale = new ColorScale { Minimum = 100, Maximum = 200 };

        Assert.Equal(0.0, scale.Normalize(100));
        Assert.Equal(0.5, scale.Normalize(150));
        Assert.Equal(1.0, scale.Normalize(200));
    }

    [Fact]
    public void Normalize_Logarithmic_UsesDecades()
    {
        var scale = new ColorScale { Minimum = 1, Maximum = 1000, IsLogarithmic = true };

        Assert.Equal(0.0, scale.Normalize(1), 10);
        Assert.Equal(1.0 / 3.0, scale.Normalize(10), 10);
        Assert.Equal(2.0 / 3.0, scale.Normalize(100), 10);
        Assert.Equal(1.0, scale.Normalize(1000), 10);
    }

    [Fact]
    public void Normalize_LogWithNonPositiveRange_FallsBackToLinear()
    {
        var scale = new ColorScale { Minimum = -10, Maximum = 10, IsLogarithmic = true };

        Assert.Equal(0.5, scale.Normalize(0));
    }

    [Fact]
    public void Denormalize_RoundtripsNormalize()
    {
        var linear = new ColorScale { Minimum = -50, Maximum = 50 };
        Assert.Equal(25.0, linear.Denormalize(linear.Normalize(25.0)), 10);

        var log = new ColorScale { Minimum = 0.001, Maximum = 1000, IsLogarithmic = true };
        Assert.Equal(10.0, log.Denormalize(log.Normalize(10.0)), 8);
    }

    [Fact]
    public void GetColor_AtRangeEnds_MatchesMapEndpoints()
    {
        var scale = new ColorScale { ColorMap = ColorMap.Jet, Minimum = 0, Maximum = 100 };

        Assert.Equal(ColorMap.Jet.GetColor(0.0), scale.GetColor(0));
        Assert.Equal(ColorMap.Jet.GetColor(1.0), scale.GetColor(100));
    }

    [Fact]
    public void GetColor_OutOfRange_DefaultClampsToEndColors()
    {
        var scale = new ColorScale { ColorMap = ColorMap.Jet, Minimum = 0, Maximum = 100 };

        Assert.Equal(scale.GetColor(0), scale.GetColor(-10));
        Assert.Equal(scale.GetColor(100), scale.GetColor(110));
    }

    [Fact]
    public void GetColor_OutOfRange_UsesConfiguredColors()
    {
        var scale = new ColorScale
        {
            Minimum = 0,
            Maximum = 100,
            BelowRangeColor = Colors.Magenta,
            AboveRangeColor = Colors.Lime,
        };

        Assert.Equal(Colors.Magenta, scale.GetColor(-1));
        Assert.Equal(Colors.Lime, scale.GetColor(101));
    }

    [Fact]
    public void GetColor_NaN_ReturnsNaNColor()
    {
        var scale = new ColorScale { NaNColor = Colors.Magenta };

        Assert.Equal(Colors.Magenta, scale.GetColor(double.NaN));
    }

    [Fact]
    public void GetColor_DiscreteLevels_QuantizesWithinBand()
    {
        var scale = new ColorScale
        {
            ColorMap = ColorMap.Jet,
            Minimum = 0,
            Maximum = 100,
            LevelCount = 4,
        };

        // 同じ帯(0〜25)内は同色、帯の中央の色が使われる
        Assert.Equal(scale.GetColor(5), scale.GetColor(20));
        Assert.Equal(ColorMap.Jet.GetColor(0.125), scale.GetColor(10));

        // 帯をまたぐと色が変わる
        Assert.NotEqual(scale.GetColor(20), scale.GetColor(30));

        // 上端値は最後の帯に入る
        Assert.Equal(ColorMap.Jet.GetColor(0.875), scale.GetColor(100));
    }

    [Fact]
    public void GetColor_ZeroRange_DoesNotThrow()
    {
        var scale = new ColorScale { Minimum = 5, Maximum = 5 };

        Assert.Equal(scale.ColorMap.GetColor(0.0), scale.GetColor(5));
    }

    [Fact]
    public void PropertyChanged_IsRaisedForModifications()
    {
        var scale = new ColorScale();
        var raised = new List<string?>();
        scale.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        scale.Maximum = 250;
        scale.LevelCount = 10;
        scale.Maximum = 250; // 同値なら発火しない

        Assert.Equal([nameof(ColorScale.Maximum), nameof(ColorScale.LevelCount)], raised);
    }
}
