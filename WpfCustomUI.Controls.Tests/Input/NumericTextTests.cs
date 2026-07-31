using System.Globalization;
using WpfCustomUI.Controls;

namespace WpfCustomUI.Controls.Tests.Input
{
    public class NumericTextTests
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        [Theory]
        [InlineData("3.14", 3.14)]
        [InlineData("-42", -42)]
        [InlineData("  2.5  ", 2.5)]
        [InlineData("1e-3", 0.001)]
        [InlineData("2.5E+6", 2500000)]
        [InlineData("-1.5e2", -150)]
        [InlineData("+0.5", 0.5)]
        public void TryParse_PlainNumber_ReturnsValueWithoutUnit(string text, double expected)
        {
            var ok = NumericText.TryParse(text, Invariant, out var number, out var unit);

            Assert.True(ok);
            Assert.Equal(expected, number, 10);
            Assert.Null(unit);
        }

        [Theory]
        [InlineData("500 mm", 500, "mm")]
        [InlineData("500mm", 500, "mm")]
        [InlineData("1e-3 m", 0.001, "m")]
        [InlineData("9.81 m/s²", 9.81, "m/s²")]
        [InlineData("200 GPa", 200, "GPa")]
        [InlineData("50 %", 50, "%")]
        [InlineData("30 °", 30, "°")]
        [InlineData("12.5µm", 12.5, "µm")]
        public void TryParse_NumberWithUnit_ReturnsValueAndUnit(string text, double expected, string expectedUnit)
        {
            var ok = NumericText.TryParse(text, Invariant, out var number, out var unit);

            Assert.True(ok);
            Assert.Equal(expected, number, 10);
            Assert.Equal(expectedUnit, unit);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("abc")]
        [InlineData("mm")]
        [InlineData("12.3.4")]
        [InlineData("1/3")]
        [InlineData("--5")]
        public void TryParse_InvalidInput_ReturnsFalse(string text)
        {
            var ok = NumericText.TryParse(text, Invariant, out _, out _);

            Assert.False(ok);
        }

        [Fact]
        public void TryParse_TrailingExponentChar_TreatedAsUnit()
        {
            // "12e" は数値 12 + 単位 "e" と解釈される(プロバイダーが解釈できなければエラーになる)
            var ok = NumericText.TryParse("12e", Invariant, out var number, out var unit);

            Assert.True(ok);
            Assert.Equal(12, number);
            Assert.Equal("e", unit);
        }

        [Fact]
        public void TryParse_GermanCulture_ParsesCommaDecimalSeparator()
        {
            var de = CultureInfo.GetCultureInfo("de-DE");

            var ok = NumericText.TryParse("1,5", de, out var number, out var unit);

            Assert.True(ok);
            Assert.Equal(1.5, number);
            Assert.Null(unit);
        }
    }
}
