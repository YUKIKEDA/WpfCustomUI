# CaeStudio.Tests

CaeStudio 各層の **xUnit** テストです。

## 対象

- Domain のメッシュ／解析
- Application の `AnalysisRunner` など
- Infrastructure の `SimulatedHpcClient` 状態遷移など

アプリ UI の UIA 回帰は別途 [`.dev/scripts/verify-caestudio.ps1`](../../.dev/scripts/verify-caestudio.ps1)。

## 依存

| 種類 | 内容 |
| --- | --- |
| TFM | `net10.0-windows` |
| プロジェクト | `CaeStudio.App`（経由で全層） |
| パッケージ | xunit, Microsoft.NET.Test.Sdk など |

## 実行

```bash
dotnet test samples/CaeStudio/CaeStudio.Tests/CaeStudio.Tests.csproj -c Debug
```
