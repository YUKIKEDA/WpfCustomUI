# WpfCustomUI.Controls

WPF 向け **テーマ基盤** と **CAE 向け UI コントロール** の本体アセンブリです。外部 NuGet 依存はありません。

## 役割

- セマンティック トークン（Dark / Light）と `WcuTheme` / `ThemeManager`
- 標準コントロールのスタイル、CAE 向け入力・表示コントロール
- `WcuIcon` / `WcuIcons`、ボタン 4 階層、`WcuRibbon` など

詳細なトークン規約・コントロール仕様は [`.dev/spec.md`](../.dev/spec.md) を参照。

## 依存

| 種類 | 内容 |
| --- | --- |
| TFM | `net10.0-windows` |
| パッケージ | なし（WPF のみ） |

## 利用の入り方

```bash
dotnet add reference path/to/WpfCustomUI.Controls/WpfCustomUI.Controls.csproj
```

`App.xaml`:

```xml
<Application xmlns:ui="https://schemas.wpfcustomui.dev/xaml"
             ...>
    <Application.Resources>
        <ui:WcuTheme Theme="Dark" />
    </Application.Resources>
</Application>
```

実行時切替の例: `ThemeManager.SetTheme(WcuThemeVariant.Light)`。

## 主な型・領域

| 領域 | 例 |
| --- | --- |
| テーマ | `WcuTheme`, `ThemeManager`, `WcuThemeVariant` |
| ウィンドウ | `WcuWindow`, `WcuDialogWindow`（`TitleBarContent` 可） |
| 入力 | `NumericBox`, `PathBox`, `SearchBox`, `SplitButton`, `DropDownButton` など |
| リボン | `WcuRibbon`, `WcuRibbonTab`, `WcuRibbonGroup` |
| アイコン | `WcuIcon`, `WcuIcons` |

デモは [WpfCustomUI.Gallery](../WpfCustomUI.Gallery/) を起動してください。

## テスト

[WpfCustomUI.Controls.Tests](../WpfCustomUI.Controls.Tests/)
