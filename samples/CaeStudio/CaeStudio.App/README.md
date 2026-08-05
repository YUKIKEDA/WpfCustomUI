# CaeStudio.App

ライブラリのドッグフード用 **2D FEM サンプルシェル**（WPF アプリ）です。

## 役割

- リボン（モデル → 解析 → 結果 → 表示）+ AvalonDock シェル
- ローカル解析実行と、外部 HPC 模擬ジョブ（投入／モニタ／結果読込）
- Controls / Charts / Docking / Viewport3D の実戦投入

UI／ジョブ方針の詳細は [`.dev/spec.md`](../../.dev/spec.md) Phase 26–27。

## 依存

| 種類 | 内容 |
| --- | --- |
| TFM | `net10.0-windows` |
| 層 | Application, Infrastructure（→ Domain） |
| UI ライブラリ | Controls, Charts, Docking, Viewport3D |
| パッケージ | Hosting, R3 WPF 拡張, Behaviors など |

## 実行

```bash
dotnet run --project samples/CaeStudio/CaeStudio.App
```

UIA 回帰の入口: [`.dev/scripts/verify-caestudio.ps1`](../../.dev/scripts/verify-caestudio.ps1)

## 構成の目安

| パス | 内容 |
| --- | --- |
| `Views/` | `MainWindow` など |
| `ViewModels/` | `MainViewModel` ほか |
| `Services/` | ダイアログ等の View 寄りサービス |
| `Behaviors/` | ビューポート連携など |

層の責務分担は隣接 README（Domain / Application / Infrastructure）を参照。
