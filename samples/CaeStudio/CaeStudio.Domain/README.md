# CaeStudio.Domain

CaeStudio の **ドメイン層**（モデル・メッシュ・ソルバ）です。UI／永続化に依存しません。

## 役割

- プロジェクト入力モデル（形状・材料・BC・解析タイプ）
- 2D メッシュ生成
- 静解析／固有値解析（疎行列・CG など）と厳密解ユーティリティ

計算の正しさの説明・検証はテストと [`.dev/spec.md`](../../.dev/spec.md) の解析関連節を参照。

## 依存

| 種類 | 内容 |
| --- | --- |
| TFM | `net10.0` |
| パッケージ | なし |

## 主な領域

| 名前空間 | 内容 |
| --- | --- |
| `CaeStudio.Domain.Models` | `CaeProjectData`, 材料, 形状, BC, テンプレート |
| `CaeStudio.Domain.Meshing` | `Mesh2D`, `MeshGenerator` |
| `CaeStudio.Domain.Solving` | `StaticAnalysis`, `ModalAnalysis`, CSR, CG など |

上位層: [CaeStudio.Application](../CaeStudio.Application/)
