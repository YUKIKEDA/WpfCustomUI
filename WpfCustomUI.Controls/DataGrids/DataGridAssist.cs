using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace WpfCustomUI.Controls;

/// <summary>
/// DataGrid の列にテーマの表示/編集用スタイルを自動適用する添付ビヘイビア。
/// </summary>
/// <remarks>
/// DataGridTextColumn / DataGridComboBoxColumn は、WPF 組み込みの静的な
/// Default(Editing)ElementStyle を既定値として使うため、テーマの暗黙スタイルが
/// 生成エレメントに届かない。このビヘイビアは「既定スタイルのまま」の列だけを
/// テーマのスタイルに差し替える。アプリが明示指定した列には手を付けない。
/// </remarks>
public static class DataGridAssist
{
    public const string EditingTextBoxStyleKey = "Wcu.DataGrid.EditingTextBox";
    public const string ElementTextBlockStyleKey = "Wcu.DataGrid.ElementTextBlock";

    public static readonly DependencyProperty AutoApplyColumnStylesProperty =
        DependencyProperty.RegisterAttached(
            "AutoApplyColumnStyles",
            typeof(bool),
            typeof(DataGridAssist),
            new PropertyMetadata(false, OnAutoApplyColumnStylesChanged));

    public static bool GetAutoApplyColumnStyles(DataGrid grid)
        => (bool)grid.GetValue(AutoApplyColumnStylesProperty);

    public static void SetAutoApplyColumnStyles(DataGrid grid, bool value)
        => grid.SetValue(AutoApplyColumnStylesProperty, value);

    private static readonly DependencyProperty IsHookedProperty =
        DependencyProperty.RegisterAttached(
            "IsHooked", typeof(bool), typeof(DataGridAssist), new PropertyMetadata(false));

    private static Style? _fallbackEditingTextBoxStyle;
    private static Style? _fallbackElementTextBlockStyle;

    private static void OnAutoApplyColumnStylesChanged(
        DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid || !(bool)e.NewValue)
        {
            return;
        }

        if (!(bool)grid.GetValue(IsHookedProperty))
        {
            grid.SetValue(IsHookedProperty, true);
            grid.Columns.CollectionChanged += (_, args) =>
            {
                if (args.NewItems is null || !GetAutoApplyColumnStyles(grid))
                {
                    return;
                }

                foreach (DataGridColumn column in args.NewItems)
                {
                    ApplyColumnStyle(grid, column);
                }
            };

            grid.Initialized += (_, _) =>
            {
                if (GetAutoApplyColumnStyles(grid))
                {
                    ApplyAll(grid);
                }
            };
            grid.Loaded += (_, _) =>
            {
                if (GetAutoApplyColumnStyles(grid))
                {
                    ApplyAll(grid);
                }
            };
        }

        ApplyAll(grid);
    }

    private static void ApplyAll(DataGrid grid)
    {
        foreach (var column in grid.Columns)
        {
            ApplyColumnStyle(grid, column);
        }
    }

    private static void ApplyColumnStyle(DataGrid grid, DataGridColumn column)
    {
        switch (column)
        {
            case DataGridTextColumn text:
                if (ReferenceEquals(text.EditingElementStyle, DataGridTextColumn.DefaultEditingElementStyle))
                {
                    text.EditingElementStyle = FindEditingTextBoxStyle(grid);
                }

                if (ReferenceEquals(text.ElementStyle, DataGridTextColumn.DefaultElementStyle))
                {
                    text.ElementStyle = FindElementTextBlockStyle(grid);
                }
                break;

            case DataGridComboBoxColumn combo
                when ReferenceEquals(combo.EditingElementStyle, DataGridComboBoxColumn.DefaultEditingElementStyle):
                if (FindResource(grid, typeof(ComboBox)) is Style comboStyle)
                {
                    combo.EditingElementStyle = comboStyle;
                }
                break;
        }
    }

    private static Style FindEditingTextBoxStyle(DataGrid grid)
        => FindResource(grid, EditingTextBoxStyleKey) as Style
           ?? GetFallbackEditingTextBoxStyle();

    private static Style FindElementTextBlockStyle(DataGrid grid)
        => FindResource(grid, ElementTextBlockStyleKey) as Style
           ?? GetFallbackElementTextBlockStyle();

    private static object? FindResource(FrameworkElement element, object key)
    {
        if (Application.Current?.TryFindResource(key) is { } appResource)
        {
            return appResource;
        }

        return element.TryFindResource(key);
    }

    private static ControlTemplate CreateEmptyErrorTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(AdornedElementPlaceholder));
        return new ControlTemplate { VisualTree = factory };
    }

    private static Style GetFallbackEditingTextBoxStyle()
    {
        if (_fallbackEditingTextBoxStyle is not null)
        {
            return _fallbackEditingTextBoxStyle;
        }

        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter(Control.BackgroundProperty, System.Windows.Media.Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, System.Windows.Media.Brushes.Transparent));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 0, 5, 0)));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(Validation.ErrorTemplateProperty, CreateEmptyErrorTemplate()));

        var errorTrigger = new Trigger { Property = Validation.HasErrorProperty, Value = true };
        errorTrigger.Setters.Add(new Setter(
            Control.BorderBrushProperty,
            new DynamicResourceExtension("Wcu.Brush.Error")));
        errorTrigger.Setters.Add(new Setter(
            FrameworkElement.ToolTipProperty,
            new Binding("(Validation.Errors)[0].ErrorContent") { RelativeSource = RelativeSource.Self }));
        style.Triggers.Add(errorTrigger);

        style.Seal();
        _fallbackEditingTextBoxStyle = style;
        return style;
    }

    private static Style GetFallbackElementTextBlockStyle()
    {
        if (_fallbackElementTextBlockStyle is not null)
        {
            return _fallbackElementTextBlockStyle;
        }

        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(6, 0, 6, 0)));
        style.Setters.Add(new Setter(Validation.ErrorTemplateProperty, CreateEmptyErrorTemplate()));

        var errorTrigger = new Trigger { Property = Validation.HasErrorProperty, Value = true };
        errorTrigger.Setters.Add(new Setter(
            FrameworkElement.ToolTipProperty,
            new Binding("(Validation.Errors)[0].ErrorContent") { RelativeSource = RelativeSource.Self }));
        style.Triggers.Add(errorTrigger);

        style.Seal();
        _fallbackElementTextBlockStyle = style;
        return style;
    }
}
