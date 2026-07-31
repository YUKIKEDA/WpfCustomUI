using System.Windows.Input;

namespace WpfCustomUI.Gallery
{
    /// <summary>ギャラリーデモ用の最小 ICommand 実装。</summary>
    internal sealed class RelayCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute();
    }
}
