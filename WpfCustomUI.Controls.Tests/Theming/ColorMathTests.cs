using System.Windows.Media;
using WpfCustomUI.Controls.Theming;

namespace WpfCustomUI.Controls.Tests.Theming
{
    public class ColorMathTests
    {
        [Theory]
        [InlineData(0x00, 0x7A, 0xCC)] // アクセント青
        [InlineData(0x1E, 0x1E, 0x1E)] // ダーク背景
        [InlineData(0xFF, 0xFF, 0xFF)]
        [InlineData(0x00, 0x00, 0x00)]
        [InlineData(0xF1, 0x4C, 0x4C)] // エラー赤
        public void ToHsl_FromHsl_Roundtrip(byte r, byte g, byte b)
        {
            var original = Color.FromRgb(r, g, b);
            var (h, s, l) = ColorMath.ToHsl(original);
            var roundTripped = ColorMath.FromHsl(h, s, l);

            // 丸め誤差として各チャンネル ±1 を許容
            Assert.InRange(roundTripped.R, Math.Max(0, r - 1), Math.Min(255, r + 1));
            Assert.InRange(roundTripped.G, Math.Max(0, g - 1), Math.Min(255, g + 1));
            Assert.InRange(roundTripped.B, Math.Max(0, b - 1), Math.Min(255, b + 1));
        }

        [Fact]
        public void ToHsl_PureRed_ReturnsExpectedValues()
        {
            var (h, s, l) = ColorMath.ToHsl(Color.FromRgb(0xFF, 0x00, 0x00));

            Assert.Equal(0, h, 3);
            Assert.Equal(1, s, 3);
            Assert.Equal(0.5, l, 3);
        }

        [Fact]
        public void ToHsl_Gray_HasZeroSaturation()
        {
            var (_, s, _) = ColorMath.ToHsl(Color.FromRgb(0x80, 0x80, 0x80));

            Assert.Equal(0, s, 3);
        }

        [Fact]
        public void Lighten_IncreasesLightness()
        {
            var baseColor = Color.FromRgb(0x00, 0x7A, 0xCC);
            var lightened = ColorMath.Lighten(baseColor, 0.10);

            var (_, _, baseL) = ColorMath.ToHsl(baseColor);
            var (_, _, lightenedL) = ColorMath.ToHsl(lightened);

            Assert.True(lightenedL > baseL);
        }

        [Fact]
        public void Darken_DecreasesLightness()
        {
            var baseColor = Color.FromRgb(0x00, 0x7A, 0xCC);
            var darkened = ColorMath.Darken(baseColor, 0.10);

            var (_, _, baseL) = ColorMath.ToHsl(baseColor);
            var (_, _, darkenedL) = ColorMath.ToHsl(darkened);

            Assert.True(darkenedL < baseL);
        }

        [Fact]
        public void Lighten_ClampsAtWhite()
        {
            var result = ColorMath.Lighten(Color.FromRgb(0xF0, 0xF0, 0xF0), 1.0);

            Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0xFF), result);
        }

        [Fact]
        public void Darken_ClampsAtBlack()
        {
            var result = ColorMath.Darken(Color.FromRgb(0x10, 0x10, 0x10), 1.0);

            Assert.Equal(Color.FromRgb(0x00, 0x00, 0x00), result);
        }

        [Fact]
        public void Lighten_PreservesHue()
        {
            var baseColor = Color.FromRgb(0x00, 0x7A, 0xCC);
            var (baseH, _, _) = ColorMath.ToHsl(baseColor);
            var (lightenedH, _, _) = ColorMath.ToHsl(ColorMath.Lighten(baseColor, 0.10));

            Assert.Equal(baseH, lightenedH, 0);
        }

        // ================= HSV(ColorEditor 用。spec 6.9.4) =================

        [Theory]
        [InlineData(0x00, 0x7A, 0xCC)]
        [InlineData(0xFF, 0xFF, 0xFF)]
        [InlineData(0x00, 0x00, 0x00)]
        [InlineData(0xE8, 0x11, 0x23)]
        [InlineData(0x16, 0xC6, 0x0C)]
        [InlineData(0x88, 0x6C, 0xE4)]
        public void ToHsv_FromHsv_Roundtrip(byte r, byte g, byte b)
        {
            var original = Color.FromRgb(r, g, b);
            var (h, s, v) = ColorMath.ToHsv(original);
            var roundTripped = ColorMath.FromHsv(h, s, v);

            // 丸め誤差として各チャンネル ±1 を許容
            Assert.InRange(roundTripped.R, Math.Max(0, r - 1), Math.Min(255, r + 1));
            Assert.InRange(roundTripped.G, Math.Max(0, g - 1), Math.Min(255, g + 1));
            Assert.InRange(roundTripped.B, Math.Max(0, b - 1), Math.Min(255, b + 1));
        }

        [Fact]
        public void ToHsv_PureRed_ReturnsExpectedValues()
        {
            var (h, s, v) = ColorMath.ToHsv(Color.FromRgb(0xFF, 0x00, 0x00));

            Assert.Equal(0, h, 3);
            Assert.Equal(1, s, 3);
            Assert.Equal(1, v, 3);
        }

        [Fact]
        public void ToHsv_Black_HasZeroValue()
        {
            var (_, s, v) = ColorMath.ToHsv(Color.FromRgb(0x00, 0x00, 0x00));

            Assert.Equal(0, s, 3);
            Assert.Equal(0, v, 3);
        }

        [Fact]
        public void ToHsv_Gray_HasZeroSaturation()
        {
            var (_, s, v) = ColorMath.ToHsv(Color.FromRgb(0x80, 0x80, 0x80));

            Assert.Equal(0, s, 3);
            Assert.Equal(0x80 / 255.0, v, 3);
        }

        [Fact]
        public void FromHsv_HueWrapsAround()
        {
            // 360° と 0° は同じ色(赤)
            Assert.Equal(ColorMath.FromHsv(0, 1, 1), ColorMath.FromHsv(360, 1, 1));
        }
    }
}
