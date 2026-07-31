using System.Windows;
using System.Windows.Markup;

[assembly: ThemeInfo(
    // テーマ別辞書は使わない(テーマは WcuTheme + Tokens 辞書で管理する)
    ResourceDictionaryLocation.None,
    // CustomControl の既定スタイルは Themes/Generic.xaml から解決する
    ResourceDictionaryLocation.SourceAssembly
)]

// 利用側は xmlns:ui="https://schemas.wpfcustomui.dev/xaml" の1行で全名前空間を参照できる
[assembly: XmlnsPrefix("https://schemas.wpfcustomui.dev/xaml", "ui")]
[assembly: XmlnsDefinition("https://schemas.wpfcustomui.dev/xaml", "WpfCustomUI.Controls")]
[assembly: XmlnsDefinition("https://schemas.wpfcustomui.dev/xaml", "WpfCustomUI.Controls.Theming")]
