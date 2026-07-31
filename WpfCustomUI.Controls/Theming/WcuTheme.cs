using System.Windows;

namespace WpfCustomUI.Controls.Theming;

/// <summary>
/// ライブラリの全リソース(デザイントークン+コントロールスタイル)を
/// 正しい順序でマージする ResourceDictionary。
/// 利用側は App.xaml に <c>&lt;ui:WcuTheme Theme="Dark"/&gt;</c> と書くだけでよい。
/// 辞書の内部構成(ファイル分割・マージ順)はこのクラスの実装詳細とする(spec 4.3)。
/// </summary>
public class WcuTheme : ResourceDictionary
{
    private const string ThemesBaseUri = "pack://application:,,,/WpfCustomUI.Controls;component/Themes/";

    private WcuThemeVariant _theme;
    private bool _applied;

    public WcuTheme()
    {
        ThemeManager.Register(this);
        Apply(WcuThemeVariant.Dark);
    }

    /// <summary>現在のテーマバリアント。設定するとトークン辞書が差し替わる。</summary>
    public WcuThemeVariant Theme
    {
        get => _theme;
        set => Apply(value);
    }

    internal void Apply(WcuThemeVariant variant)
    {
        if (_applied && variant == _theme)
        {
            return;
        }

        _theme = variant;
        _applied = true;

        // アクセント上書き(ThemeManager.SetAccent)はこの辞書自身のエントリとして
        // 保持されるため、マージ辞書を差し替えても維持される。
        MergedDictionaries.Clear();
        MergedDictionaries.Add(Load("Tokens.Core.xaml"));
        MergedDictionaries.Add(Load($"Tokens.{variant}.xaml"));
    }

    private static ResourceDictionary Load(string fileName) =>
        new() { Source = new Uri(ThemesBaseUri + fileName, UriKind.Absolute) };
}
