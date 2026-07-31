namespace WpfCustomUI.Controls.Theming;

/// <summary>
/// テーマのバリアント。当面はダークのみ実装(spec 4)。
/// ライトテーマは Tokens.Light.xaml を追加し、この列挙型にメンバーを足すだけで対応できる。
/// </summary>
public enum WcuThemeVariant
{
    Dark,
}
