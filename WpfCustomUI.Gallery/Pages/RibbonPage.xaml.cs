using System.Windows;
using System.Windows.Controls;

namespace WpfCustomUI.Gallery.Pages;

public partial class RibbonPage : UserControl
{
    private readonly List<string> _log = [];

    public RibbonPage()
    {
        InitializeComponent();
    }

    private void OnAction(object sender, RoutedEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag as string ?? "(不明)";
        _log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {tag}");
        if (_log.Count > 8)
        {
            _log.RemoveAt(_log.Count - 1);
        }

        ActionLog.Text = string.Join(Environment.NewLine, _log);
    }
}
