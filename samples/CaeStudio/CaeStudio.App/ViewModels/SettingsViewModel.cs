using CaeStudio.Application;
using R3;
using WpfCustomUI.Controls.Theming;

namespace CaeStudio.App.ViewModels;

/// <summary>
/// 設定ダイアログの VM。編集はローカルに保持し、OK 確定時に
/// <see cref="ISettingsService"/> へ書き戻してテーマを即適用する。
/// </summary>
public sealed class SettingsViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly ISettingsService _settings;

    public SettingsViewModel(ISettingsService settings)
    {
        _settings = settings;

        var current = settings.Current;
        IsLightTheme = Register(new BindableReactiveProperty<bool>(current.Theme == "Light"));
        DefaultProjectDirectory = Register(new BindableReactiveProperty<string?>(current.DefaultProjectDirectory));
        RunGestureText = Register(new BindableReactiveProperty<string>(current.RunGesture));
    }

    /// <summary>テーマ(false = ダーク / true = ライト)。</summary>
    public BindableReactiveProperty<bool> IsLightTheme { get; }

    /// <summary>プロジェクトファイルの既定保存先(PathBox)。</summary>
    public BindableReactiveProperty<string?> DefaultProjectDirectory { get; }

    /// <summary>解析実行ショートカット(KeyGestureBox の文字列表現)。</summary>
    public BindableReactiveProperty<string> RunGestureText { get; }

    /// <summary>OK 確定: 設定を保存し、テーマを即適用する。</summary>
    public void Commit()
    {
        var theme = IsLightTheme.Value ? "Light" : "Dark";
        _settings.Update(s => s with
        {
            Theme = theme,
            DefaultProjectDirectory = string.IsNullOrWhiteSpace(DefaultProjectDirectory.Value)
                ? null : DefaultProjectDirectory.Value,
            RunGesture = string.IsNullOrWhiteSpace(RunGestureText.Value) ? "F5" : RunGestureText.Value,
        });

        ThemeManager.SetTheme(IsLightTheme.Value ? WcuThemeVariant.Light : WcuThemeVariant.Dark);
    }

    private T Register<T>(T disposable) where T : IDisposable
    {
        _disposables.Add(disposable);
        return disposable;
    }

    public void Dispose() => _disposables.Dispose();
}
