using CaeStudio.Domain.Models;

namespace CaeStudio.App.Services;

/// <summary>
/// VM から View(ダイアログ)を呼び出すための抽象。実装は View 層(DialogService)。
/// VM テストではフェイク実装を注入できる。
/// </summary>
public interface IDialogService
{
    /// <summary>新規解析ウィザードを表示する。キャンセル時は null。</summary>
    CaeProjectData? ShowNewProjectWizard();

    /// <summary>未保存の変更を破棄してよいか確認する。</summary>
    bool ConfirmDiscardChanges(string projectName);

    /// <summary>プロジェクトを開くダイアログ。キャンセル時は null。</summary>
    string? ShowOpenProjectDialog(string? initialDirectory);

    /// <summary>プロジェクトの保存先ダイアログ。キャンセル時は null。</summary>
    string? ShowSaveProjectDialog(string suggestedFileName, string? initialDirectory);

    /// <summary>設定ダイアログを表示する。OK で閉じたら true。</summary>
    bool ShowSettingsDialog();

    /// <summary>エラーメッセージを表示する。</summary>
    void ShowError(string message);
}
