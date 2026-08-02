# CaeStudio.Infrastructure

CaeStudio の **インフラ層**（Application ポートの実装）です。

## 役割

- `JsonProjectRepository`: `.wcuproj`（System.Text.Json）
- `JsonSettingsService`: ユーザ設定（テーマ・最近使ったファイル・ドック XML など）
- `SimulatedHpcClient`: 外部 HPC ジョブの模擬（キュー／進捗／失敗／結果）。計算本体は Domain ソルバ

差し替え口（SSH/REST 等）を示すための参照実装です。

## 依存

| 種類 | 内容 |
| --- | --- |
| TFM | `net10.0` |
| プロジェクト | `CaeStudio.Application`（→ Domain） |
| パッケージ | R3 |

## 主な型

| 型 | 実装するポート |
| --- | --- |
| `JsonProjectRepository` | `IProjectRepository` |
| `JsonSettingsService` | `ISettingsService` |
| `SimulatedHpcClient` | `IJobClient` |

DI 登録と UI 配線は [CaeStudio.App](../CaeStudio.App/)。単体テストは [CaeStudio.Tests](../CaeStudio.Tests/)。
