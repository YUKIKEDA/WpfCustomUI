# WpfCustomUI.Controls.Tests

`WpfCustomUI.Controls` の **xUnit** テストです。

## 対象

- テーマ／トークン、コントロールロジック、アイコン辞書、リボン構造など（UI 自動化ではない単体・統合寄りのテスト）

## 依存

| 種類 | 内容 |
| --- | --- |
| TFM | `net10.0-windows` |
| プロジェクト | `WpfCustomUI.Controls` |
| パッケージ | xunit, Microsoft.NET.Test.Sdk など |

## 実行

```bash
dotnet test WpfCustomUI.Controls.Tests/WpfCustomUI.Controls.Tests.csproj -c Debug
```

またはソリューション全体:

```bash
dotnet test WpfCustomUI.slnx -c Debug
```
