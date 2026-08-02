# WpfCustomUI.Viewport3D.Tests

`WpfCustomUI.Viewport3D` の **xUnit** テストです。

## 対象

- ジオメトリ／チャンク／LOD／座標変換、構築コーディネータなど、ビューポート周辺のロジック
- GPU 実機描画の UIA ではなく、決定論的な単体寄りテストが中心

## 依存

| 種類 | 内容 |
| --- | --- |
| TFM | `net10.0-windows` |
| プロジェクト | `WpfCustomUI.Viewport3D` |
| パッケージ | xunit, Microsoft.NET.Test.Sdk など |

## 実行

```bash
dotnet test WpfCustomUI.Viewport3D.Tests/WpfCustomUI.Viewport3D.Tests.csproj -c Debug
```
