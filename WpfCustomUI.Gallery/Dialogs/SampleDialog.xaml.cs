using System.Windows;
using WpfCustomUI.Controls;

namespace WpfCustomUI.Gallery.Dialogs
{
    public partial class SampleDialog : WcuDialogWindow
    {
        public SampleDialog()
        {
            InitializeComponent();
        }

        public string MaterialName => NameBox.Text;

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
