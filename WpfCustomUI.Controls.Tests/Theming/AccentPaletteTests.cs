using System.Windows.Media;
using WpfCustomUI.Controls.Theming;

namespace WpfCustomUI.Controls.Tests.Theming
{
    public class AccentPaletteTests
    {
        private static readonly Color BaseBlue = Color.FromRgb(0x00, 0x7A, 0xCC);

        [Fact]
        public void FromBase_DefaultIsBaseColor()
        {
            var palette = AccentPalette.FromBase(BaseBlue);

            Assert.Equal(BaseBlue, palette.Default);
        }

        [Fact]
        public void FromBase_LightnessOrder_IsHoverDefaultPressedMuted()
        {
            var palette = AccentPalette.FromBase(BaseBlue);

            var (_, _, hoverL) = ColorMath.ToHsl(palette.Hover);
            var (_, _, defaultL) = ColorMath.ToHsl(palette.Default);
            var (_, _, pressedL) = ColorMath.ToHsl(palette.Pressed);
            var (_, _, mutedL) = ColorMath.ToHsl(palette.Muted);

            Assert.True(hoverL > defaultL, "Hover は Default より明るい");
            Assert.True(pressedL < defaultL, "Pressed は Default より暗い");
            Assert.True(mutedL < pressedL, "Muted は Pressed よりさらに暗い");
        }

        [Fact]
        public void FromBase_AllDerivedColorsPreserveHue()
        {
            var palette = AccentPalette.FromBase(BaseBlue);
            var (baseH, _, _) = ColorMath.ToHsl(BaseBlue);

            foreach (var color in new[] { palette.Hover, palette.Pressed, palette.Muted })
            {
                var (h, _, _) = ColorMath.ToHsl(color);
                // 8bit RGB への量子化で色相はわずかにずれるため ±2 度を許容
                Assert.InRange(h, baseH - 2, baseH + 2);
            }
        }
    }
}
