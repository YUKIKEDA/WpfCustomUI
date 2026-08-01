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

    private static readonly string[] ControlDictionaries =
    [
        "Controls/Shared.xaml",
        "Controls/Button.xaml",
        "Controls/TextBox.xaml",
        "Controls/CheckBox.xaml",
        "Controls/ComboBox.xaml",
        "Controls/ListBox.xaml",
        "Controls/ListView.xaml",
        "Controls/TreeView.xaml",
        "Controls/Text.xaml",
        "Controls/ScrollBar.xaml",
        "Controls/TabControl.xaml",
        "Controls/Menu.xaml",
        "Controls/ToolTip.xaml",
        "Controls/Slider.xaml",
        "Controls/ProgressBar.xaml",
        "Controls/Expander.xaml",
        "Controls/GridSplitter.xaml",
        "Controls/GroupBox.xaml",
        "Controls/Separator.xaml",
        "Controls/ToolBar.xaml",
        "Controls/StatusBar.xaml",
        "Controls/DataGrid.xaml",
        // 暗黙 DataTemplate(エディタ選択)はアプリのリソースツリーから
        // 確実に見つかるよう、Generic.xaml とは別にここでもマージする
        "Controls/PropertyGrid.xaml",
    ];

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

        // コントロールスタイルはトークンを DynamicResource 参照するため、
        // テーマバリアントに依存せず同じ辞書を使う。
        foreach (var dictionary in ControlDictionaries)
        {
            MergedDictionaries.Add(Load(dictionary));
        }
    }

    private static ResourceDictionary Load(string fileName) =>
        new() { Source = new Uri(ThemesBaseUri + fileName, UriKind.Absolute) };
}
