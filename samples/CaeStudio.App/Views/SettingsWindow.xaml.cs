using System.Windows;
using WpfCustomUI.Controls;

namespace CaeStudio.App.Views;

/// <summary>設定ダイアログ(spec 6.26.6)。</summary>
public partial class SettingsWindow : WcuDialogWindow
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
