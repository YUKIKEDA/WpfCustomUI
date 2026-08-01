namespace WpfCustomUI.Controls.Theming;

/// <summary>
/// テーマのバリアント(spec 4 / 6.15)。
/// バリアント名は Tokens.{variant}.xaml のファイル名と一致させること。
/// </summary>
public enum WcuThemeVariant
{
    /// <summary>ダークテーマ(既定。VS Dark 系)。</summary>
    Dark,

    /// <summary>ライトテーマ(VS 2022 Light 系)。</summary>
    Light,
}
