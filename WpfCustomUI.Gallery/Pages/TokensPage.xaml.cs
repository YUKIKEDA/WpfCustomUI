using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfCustomUI.Controls.Theming;

namespace WpfCustomUI.Gallery.Pages
{
    public partial class TokensPage : UserControl
    {
        private static readonly string[] SemanticBrushKeys =
        [
            "Wcu.Brush.Surface.Window",
            "Wcu.Brush.Surface.Panel",
            "Wcu.Brush.Surface.Elevated",
            "Wcu.Brush.Surface.Input",
            "Wcu.Brush.Text.Primary",
            "Wcu.Brush.Text.Secondary",
            "Wcu.Brush.Text.Disabled",
            "Wcu.Brush.Text.OnAccent",
            "Wcu.Brush.Border.Default",
            "Wcu.Brush.Border.Strong",
            "Wcu.Brush.Border.Focus",
            "Wcu.Brush.Accent.Default",
            "Wcu.Brush.Accent.Hover",
            "Wcu.Brush.Accent.Pressed",
            "Wcu.Brush.Accent.Muted",
            "Wcu.Brush.State.Hover",
            "Wcu.Brush.State.Pressed",
            "Wcu.Brush.State.Selected",
            "Wcu.Brush.State.SelectedInactive",
            "Wcu.Brush.Error",
            "Wcu.Brush.Warning",
            "Wcu.Brush.Success",
            "Wcu.Brush.Info",
        ];

        private static readonly (string Label, Color? Accent)[] AccentPresets =
        [
            ("Blue (default)", null),
            ("Orange", Color.FromRgb(0xCA, 0x51, 0x00)),
            ("Green", Color.FromRgb(0x38, 0x8A, 0x34)),
            ("Purple", Color.FromRgb(0x8E, 0x47, 0xCF)),
        ];

        public TokensPage()
        {
            InitializeComponent();
            PopulateBrushSwatches();
            PopulateAccentButtons();
            PopulateSpacingBars();
        }

        private void PopulateBrushSwatches()
        {
            foreach (var key in SemanticBrushKeys)
            {
                var swatch = new Border
                {
                    Width = 48,
                    Height = 32,
                    CornerRadius = (CornerRadius)FindResource("Wcu.CornerRadius"),
                    BorderThickness = new Thickness(1),
                };
                // DynamicResource 相当の参照にすることで、アクセント切替が即座に反映される
                swatch.SetResourceReference(Border.BackgroundProperty, key);
                swatch.SetResourceReference(Border.BorderBrushProperty, "Wcu.Brush.Border.Default");

                var label = new TextBlock
                {
                    Text = key["Wcu.Brush.".Length..],
                    FontSize = (double)FindResource("Wcu.FontSize.S"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0),
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, "Wcu.Brush.Text.Secondary");

                var item = new StackPanel
                {
                    Width = 110,
                    Margin = new Thickness(0, 0, 8, 8),
                    ToolTip = key,
                };
                item.Children.Add(swatch);
                item.Children.Add(label);
                BrushPanel.Children.Add(item);
            }
        }

        private void PopulateAccentButtons()
        {
            foreach (var (label, accent) in AccentPresets)
            {
                var button = new Button
                {
                    Content = label,
                    Padding = new Thickness(12, 4, 12, 4),
                    Margin = new Thickness(0, 0, 8, 0),
                };
                button.Click += (_, _) =>
                {
                    if (accent is { } color)
                    {
                        ThemeManager.SetAccent(color);
                    }
                    else
                    {
                        ThemeManager.ResetAccent();
                    }
                };
                AccentPanel.Children.Add(button);
            }
        }

        private void PopulateSpacingBars()
        {
            foreach (var name in new[] { "XS", "S", "M", "L" })
            {
                var key = $"Wcu.Spacing.{name}";
                var size = (double)FindResource(key);

                var bar = new Border { Width = size * 10, Height = 12 };
                bar.SetResourceReference(Border.BackgroundProperty, "Wcu.Brush.Accent.Muted");

                var label = new TextBlock
                {
                    Text = $"{key} = {size}px",
                    Width = 160,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 2, 0, 2),
                };
                row.Children.Add(label);
                row.Children.Add(bar);
                SpacingPanel.Children.Add(row);
            }
        }
    }
}
