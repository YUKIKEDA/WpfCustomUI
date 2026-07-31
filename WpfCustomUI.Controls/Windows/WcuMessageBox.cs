using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WpfCustomUI.Controls;

/// <summary>
/// テーマに従う MessageBox 代替(spec 6.8.5)。
/// <c>WcuMessageBox.Show(owner, "...", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question)</c>
/// のように標準 MessageBox とほぼ同じ形で使える。
/// ボタン文字列は既定で英語。アプリ側で <see cref="OkText"/> 等を差し替え可能(spec 方針: 文字列注入)。
/// </summary>
public static class WcuMessageBox
{
    public static string OkText { get; set; } = "OK";
    public static string CancelText { get; set; } = "Cancel";
    public static string YesText { get; set; } = "Yes";
    public static string NoText { get; set; } = "No";

    public static MessageBoxResult Show(
        string message,
        string caption = "",
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None)
        => Show(null, message, caption, buttons, image);

    public static MessageBoxResult Show(
        Window? owner,
        string message,
        string caption = "",
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None)
    {
        var dialog = new WcuDialogWindow
        {
            Title = caption,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 320,
            MaxWidth = 560,
        };

        owner ??= GetActiveWindow();
        if (owner is not null && !ReferenceEquals(owner, dialog))
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dialog.Content = BuildBody(message, image);

        var result = MessageBoxResult.None;
        dialog.Footer = BuildButtons(buttons, r =>
        {
            result = r;
            dialog.DialogResult = true;
        });

        dialog.ShowDialog();

        // × ボタン等で閉じられた場合のフォールバック
        if (result == MessageBoxResult.None)
        {
            result = buttons switch
            {
                MessageBoxButton.OK => MessageBoxResult.OK,
                MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
                MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
                // YesNo は Esc/× で閉じられない仕様が標準だが、
                // カスタムクロームでは閉じられるため No を返す
                MessageBoxButton.YesNo => MessageBoxResult.No,
                _ => MessageBoxResult.None,
            };
        }

        return result;
    }

    private static Window? GetActiveWindow()
    {
        var app = Application.Current;
        if (app is null)
        {
            return null;
        }

        foreach (Window window in app.Windows)
        {
            if (window.IsActive)
            {
                return window;
            }
        }

        return app.MainWindow is { IsLoaded: true } main ? main : null;
    }

    private static object BuildBody(string message, MessageBoxImage image)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(20, 20, 20, 16),
        };

        var icon = CreateIcon(image);
        if (icon is not null)
        {
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 2, 12, 0);
            panel.Children.Add(icon);
        }

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(text);
        return panel;
    }

    private static FrameworkElement? CreateIcon(MessageBoxImage image)
    {
        // MessageBoxImage は同値のエイリアスを持つ(Information=Asterisk 等)
        (string data, string brushKey)? spec = image switch
        {
            MessageBoxImage.Error =>
                ("M12,2 A10,10 0 1 1 12,22 A10,10 0 1 1 12,2 Z M8.1,6.7 L6.7,8.1 L10.6,12 L6.7,15.9 L8.1,17.3 L12,13.4 L15.9,17.3 L17.3,15.9 L13.4,12 L17.3,8.1 L15.9,6.7 L12,10.6 Z",
                 "Wcu.Brush.Error"),
            MessageBoxImage.Warning =>
                ("M12,2 L23,21 L1,21 Z M11,9 L11,15 L13,15 L13,9 Z M11,16.5 L11,18.5 L13,18.5 L13,16.5 Z",
                 "Wcu.Brush.Warning"),
            MessageBoxImage.Question =>
                ("M12,2 A10,10 0 1 1 12,22 A10,10 0 1 1 12,2 Z M12,5.5 C9.8,5.5 8.2,6.9 8,9 L10,9.3 C10.1,8.1 10.9,7.4 12,7.4 C13.1,7.4 13.9,8.1 13.9,9.1 C13.9,10 13.5,10.4 12.4,11.2 C11.3,12 11,12.6 11,14 L13,14 C13,13.1 13.2,12.8 14.2,12 C15.4,11.1 15.9,10.3 15.9,9 C15.9,7 14.2,5.5 12,5.5 Z M11,15.5 L11,17.5 L13,17.5 L13,15.5 Z",
                 "Wcu.Brush.Accent.Default"),
            MessageBoxImage.Information =>
                ("M12,2 A10,10 0 1 1 12,22 A10,10 0 1 1 12,2 Z M11,6.5 L11,8.5 L13,8.5 L13,6.5 Z M11,10 L11,17.5 L13,17.5 L13,10 Z",
                 "Wcu.Brush.Info"),
            _ => null,
        };

        if (spec is null)
        {
            return null;
        }

        var path = new Path
        {
            Data = Geometry.Parse(spec.Value.data),
            Width = 24,
            Height = 24,
            Stretch = Stretch.Uniform,
        };
        path.SetResourceReference(Shape.FillProperty, spec.Value.brushKey);
        return path;
    }

    private static object BuildButtons(MessageBoxButton buttons, Action<MessageBoxResult> onResult)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        void Add(string text, MessageBoxResult result, bool isDefault, bool isCancel)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 80,
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = isDefault,
                IsCancel = isCancel,
            };
            if (isDefault)
            {
                // WcuTheme 未使用時は解決されず既定スタイルのままになる(安全)
                button.SetResourceReference(FrameworkElement.StyleProperty, "Wcu.Button.Accent");
            }

            button.Click += (_, _) => onResult(result);
            panel.Children.Add(button);
        }

        switch (buttons)
        {
            case MessageBoxButton.OK:
                Add(OkText, MessageBoxResult.OK, isDefault: true, isCancel: true);
                break;
            case MessageBoxButton.OKCancel:
                Add(OkText, MessageBoxResult.OK, isDefault: true, isCancel: false);
                Add(CancelText, MessageBoxResult.Cancel, isDefault: false, isCancel: true);
                break;
            case MessageBoxButton.YesNo:
                Add(YesText, MessageBoxResult.Yes, isDefault: true, isCancel: false);
                Add(NoText, MessageBoxResult.No, isDefault: false, isCancel: false);
                break;
            case MessageBoxButton.YesNoCancel:
                Add(YesText, MessageBoxResult.Yes, isDefault: true, isCancel: false);
                Add(NoText, MessageBoxResult.No, isDefault: false, isCancel: false);
                Add(CancelText, MessageBoxResult.Cancel, isDefault: false, isCancel: true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(buttons));
        }

        return panel;
    }
}
