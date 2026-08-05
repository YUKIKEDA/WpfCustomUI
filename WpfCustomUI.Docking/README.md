# WpfCustomUI.Docking

[AvalonDock](https://github.com/Dirkster99/AvalonDock) をテーマに載せ、レイアウト保存／復元ヘルパーを提供するアセンブリです。

## 役割

- `WcuDockTheme`: トークン連動のドック見た目
- `DockLayout`: XML 文字列／ファイルへのレイアウト永続化（ContentId ベースの再結線）

Controls 本体はゼロ依存方針のため、外部ドックはここに隔離しています。

## 依存

| 種類 | 内容 |
| --- | --- |
| TFM | `net10.0-windows` |
| プロジェクト | `WpfCustomUI.Controls` |
| パッケージ | `Dirkster.AvalonDock`, `Dirkster.AvalonDock.Themes.VS2013` |

## 利用の入り方

1. Controls の `WcuTheme` をアプリに入れる  
2. 本プロジェクトを参照  
3. `DockingManager` + `WcuDockTheme`、必要なら `DockLayout.SaveToString` / `LoadFromString`  
4. Gallery の Docking デモ、または CaeStudio のシェルを参照

**注意**: 保存 XML に新しい `ContentId` が無いと、そのパネルは復元時に消えます。必須 ID の検証はアプリ側の責務です（CaeStudio は起動時に欠落レイアウトを破棄）。

## 主な型

| 型 | 説明 |
| --- | --- |
| `WcuDockTheme` | テーマ連動 DictionaryTheme |
| `DockLayout` | レイアウト XML の保存・復元 |

## 関連

- [WpfCustomUI.Controls](../WpfCustomUI.Controls/)
- [samples/CaeStudio/CaeStudio.App](../samples/CaeStudio/CaeStudio.App/)
