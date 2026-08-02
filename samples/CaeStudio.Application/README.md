# CaeStudio.Application

CaeStudio の **アプリケーション層**（ユースケース・ポート）です。WPF には依存しません。

## 役割

- `ProjectStore`: 現在プロジェクトの単一情報源
- `AnalysisRunner`: Domain ソルバをバックグラウンド実行し状態／残差を公開
- `IProjectRepository` / `ISettingsService` / `IJobClient`: 永続化・設定・外部ジョブのポート

UI は購読側で `ObserveOn` する前提です。

## 依存

| 種類 | 内容 |
| --- | --- |
| TFM | `net10.0` |
| プロジェクト | `CaeStudio.Domain` |
| パッケージ | R3, ObservableCollections |

## 主な型

| 型 | 説明 |
| --- | --- |
| `ProjectStore` | プロジェクト状態 |
| `AnalysisRunner` | 静／固有値解析の実行 |
| `IJobClient` | ジョブ投入・進捗・結果・キャンセル |
| `IProjectRepository` | `.wcuproj` 等の永続化口 |
| `ISettingsService` / `UserSettings` | ユーザ設定 |

実装は [CaeStudio.Infrastructure](../CaeStudio.Infrastructure/)、UI は [CaeStudio.App](../CaeStudio.App/)。
