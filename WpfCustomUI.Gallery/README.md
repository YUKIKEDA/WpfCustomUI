# WpfCustomUI.Gallery

ライブラリの **コントロール一覧デモ** です。テーマ切替・トークン確認・各機能のドッグフーディング入口。

## 役割

- 左ナビ + 右デモページ構成
- Controls / Charts / Docking / Viewport3D の実演
- スクショ・手動確認の基準アプリ

## 依存

| 種類 | 内容 |
| --- | --- |
| TFM | `net10.0-windows` |
| プロジェクト | Controls, Charts, Docking, Viewport3D |

## 実行

```bash
dotnet run --project WpfCustomUI.Gallery
```

ソリューションから:

```bash
dotnet run --project WpfCustomUI.Gallery/WpfCustomUI.Gallery.csproj
```

## 構成の目安

- `Pages/`: コントロール別デモ
- テーマは `WcuTheme` + Gallery 内の切替 UI

仕様・ページ追加方針は [`.dev/spec.md`](../.dev/spec.md) を参照。

## 関連

- [ルート README](../README.md)
- [`.dev/spec.md`](../.dev/spec.md)（Gallery は旧 `WpfCustomUI.Example` から改名）
