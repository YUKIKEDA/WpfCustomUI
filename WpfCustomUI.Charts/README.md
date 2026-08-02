# WpfCustomUI.Charts

[ScottPlot.WPF](https://scottplot.net/) をテーマに載せ、CAE 向け複合チャートを提供するアセンブリです。

## 役割

- ライブラリ テーマ（ブラシ／フォント密度）と ScottPlot の見た目の整合
- 履歴・FRF・ヒストグラムなど、サンプル／Gallery で使う複合チャート

仕様の位置づけは [`.dev/spec.md`](../.dev/spec.md)（Charts 関連フェーズ）を参照。

## 依存

| 種類 | 内容 |
| --- | --- |
| TFM | `net10.0-windows` |
| プロジェクト | `WpfCustomUI.Controls` |
| パッケージ | `ScottPlot.WPF` |

## 利用の入り方

1. [Controls](../WpfCustomUI.Controls/) の `WcuTheme` をアプリに入れる  
2. 本プロジェクトを参照し、`xmlns:ui="https://schemas.wpfcustomui.dev/xaml"` でチャート型を使う  
3. 見た目・操作の確認は Gallery の Charts ページ

## 主な型

Gallery / CaeStudio から参照されるチャート複合（履歴・パス・FRF・ヒストグラム等）。個別 API の一覧は Gallery デモを正とします。

## 関連

- [WpfCustomUI.Controls](../WpfCustomUI.Controls/)
- [WpfCustomUI.Gallery](../WpfCustomUI.Gallery/)
