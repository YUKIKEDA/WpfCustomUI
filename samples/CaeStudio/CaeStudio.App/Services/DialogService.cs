using CaeStudio.App.ViewModels;
using CaeStudio.App.Views;
using CaeStudio.Application;
using CaeStudio.Domain.Models;
using Microsoft.Win32;
using System.Windows;
using WpfCustomUI.Controls;

namespace CaeStudio.App.Services;

/// <summary>ダイアログ表示の View 層実装。</summary>
public sealed class DialogService(ISettingsService settings) : IDialogService
{
    private const string ProjectFilter = "CaeStudio プロジェクト (*.wcuproj)|*.wcuproj|すべてのファイル (*.*)|*.*";

    public CaeProjectData? ShowNewProjectWizard()
    {
        using var viewModel = new NewProjectWizardViewModel();
        var window = new NewProjectWizardWindow
        {
            Owner = System.Windows.Application.Current.MainWindow,
            DataContext = viewModel,
        };

        return window.ShowDialog() == true ? viewModel.BuildProject() : null;
    }

    public bool ConfirmDiscardChanges(string projectName) =>
        WcuMessageBox.Show(
            System.Windows.Application.Current.MainWindow,
            $"「{projectName}」には未保存の変更があります。破棄して続行しますか？",
            "確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public string? ShowOpenProjectDialog(string? initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "プロジェクトを開く",
            Filter = ProjectFilter,
            InitialDirectory = initialDirectory ?? "",
        };
        return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true
            ? dialog.FileName : null;
    }

    public string? ShowSaveProjectDialog(string suggestedFileName, string? initialDirectory)
    {
        var dialog = new SaveFileDialog
        {
            Title = "プロジェクトを保存",
            Filter = ProjectFilter,
            FileName = suggestedFileName,
            DefaultExt = ".wcuproj",
            InitialDirectory = initialDirectory ?? "",
        };
        return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true
            ? dialog.FileName : null;
    }

    public bool ShowSettingsDialog()
    {
        using var viewModel = new SettingsViewModel(settings);
        var window = new SettingsWindow
        {
            Owner = System.Windows.Application.Current.MainWindow,
            DataContext = viewModel,
        };

        if (window.ShowDialog() == true)
        {
            viewModel.Commit();
            return true;
        }

        return false;
    }

    public void ShowError(string message) =>
        WcuMessageBox.Show(
            System.Windows.Application.Current.MainWindow,
            message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
}
