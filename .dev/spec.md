# WpfCustomUI 仕様書

WPF 製デスクトップ CAE アプリケーション向け UI コンポーネントライブラリの設計仕様。
(2026-07-31 の設計インタビューで決定した内容のまとめ)

## 1. 概要

- **目的**: 汎用テーマ基盤 + CAE 特化コントロールの両方を提供するライブラリ(段階的に構築)
- **ターゲット**: .NET 10 (`net10.0-windows`) / WPF
- **要件の駆動源**: 具体的なドッグフーディング対象アプリはなし。「典型的な CAE アプリ像」(プリ/ポスト処理、ソルバー実行)を仮想顧客として進める

## 2. プロジェクト構成

| プロジェクト                 | 役割                                                                                                                              |
| ---------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| `WpfCustomUI.Controls`       | ライブラリ本体(旧 `WpfCustomControls` から改名する)                                                                               |
| `WpfCustomUI.Gallery`        | ギャラリーアプリ(旧 `WpfCustomUI.Example` から改名。左にコントロール一覧ナビ、右にデモページ。テーマ切替・トークン一覧表示も担う) |
| `WpfCustomUI.Controls.Tests` | xUnit テストプロジェクト(新規追加)                                                                                                |

- **配布形態**: 当面はプロジェクト参照で利用。ただし `csproj` にパッケージメタデータを整備し、いつでも NuGet 化できる構造にしておく
- **XAML 名前空間**: `XmlnsDefinition` 属性で全 CLR 名前空間を単一 URI(例: `https://schemas.wpfcustomui.dev/xaml`)に集約。利用側の xmlns 宣言は1行で済むようにする

## 3. 依存関係ポリシー

- ライブラリ本体は**ゼロ依存**(WPF 標準のみ)
- アイコンは Path データを自前リソース化して対応
- ドッキング UI など自作が非現実的な大物は、必要になった時点で**別アセンブリ**(例: `WpfCustomUI.Docking`)に分離して外部依存を隔離する
- ギャラリー・テストプロジェクトは外部パッケージ使用可

## 4. テーマ基盤

- **完全自作テーマ**。組み込み Fluent テーマ(`ThemeMode`)には依存しない
  - 理由: CAE は高密度 UI が基本で Fluent の大きな余白が合わない。内部実装への依存は .NET バージョンアップで壊れやすい
- **ダーク先行、切替可能な構造**:
  - テーマ辞書(色定義)とスタイル辞書を分離
  - ブラシ参照は **`DynamicResource` 規約**で統一(初日から徹底。後戻りが高くつくため)。寸法系トークンは切替不要なので `StaticResource` 参照でよい
  - 当面はダーク辞書のみ実装。ライト辞書は実需が出てから追加
- 標準コントロールのスタイルは全部を一括で作らず、必要なコントロールから順次自作

### 4.1 デザイントークン

- **2層構造(プリミティブ→セマンティック)**:
  - 第1層: パレット(`Wcu.Color.Gray.800` 等)
  - 第2層: 意味付きトークン(`Wcu.Brush.Surface.Primary` 等)がパレットを参照。テーマ切替はこの層の辞書差し替えで実現
  - コンポーネント別トークン(第3層)は原則作らない。状態別色が本当に必要な箇所だけ局所的に許容
- **命名規約**: ライブラリ接頭辞付きドット区切り文字列キー(例: `Wcu.Brush.Surface.Primary`、`Wcu.Spacing.S`)。衝突安全・grep 容易(MahApps と同じ流儀)
- **トークン化の範囲**: 色/ブラシ+寸法基本セット(`Wcu.Spacing.XS/S/M/L`、`Wcu.FontSize.S/M/L`、`Wcu.CornerRadius`、`Wcu.IconSize`、`Wcu.ControlHeight`)。全寸法の網羅はしない

### 4.2 ビジュアル方向性(ダークテーマ既定値)

- **基調色: ニュートラルグレー系(VS 風)**: 例 `#1E1E1E`(最背面)/`#252526`(パネル)/`#3F3F46`(境界)
  - 理由: ポスト処理でユーザーがコンター色から物理量を読むため、UI は無彩色で徹底し色被りを避ける
- **アクセント: 青 `#007ACC` 系**(エンジニアリングツールの定番。警告色との衝突なし)
- **密度基準値**:
  - フォント: `Segoe UI` 12px(日本語は `Yu Gothic UI` フォールバック)、`FontSize.S`=11 / `M`=12 / `L`=14
  - コントロール高さ: 24px(VS ツールバー相当)
  - Spacing: XS=2 / S=4 / M=8 / L=12(4px グリッド基調)
  - 角丸: 2px(ほぼスクエア)
  - 迷ったら Visual Studio 本体の密度を物差しにする

### 4.3 テーマの取り込み・切替 API

- **`WcuTheme`(ResourceDictionary 派生)を提供**: 利用側は `App.xaml` に `<ui:WcuTheme Theme="Dark"/>` の1行で全辞書(トークン+全スタイル)が正しい順序でマージされる。辞書の内部構成はライブラリの実装詳細として隠蔽
- **`ThemeManager` 静的 API**: 実行時テーマ切替(`SetTheme`)と、アクセントカラー変更(`SetAccent(Color)`。Hover/Pressed 等の派生色は HSL 明度調整で自動計算)を提供
- 内部ファイル構成: `Themes/Tokens.Dark.xaml`(プリミティブ+セマンティック)、`Themes/Controls/*.xaml`(コントロール別スタイル)

### 4.4 スタイル適用方式

- **キー付き定義+暗黙スタイルの2段構え**: キー付きスタイル(例: `Wcu.Button`)を定義し、暗黙スタイルは `BasedOn` でそれを参照
  - 新規アプリでは暗黙適用で全体統一、バリアント(例: `Wcu.Button.Accent`)や部分利用はキー指定

### 4.5 アイコン

- **Fluent UI System Icons(MIT)から必要分だけ SVG→Path 変換して内蔵**: `Wcu.Icon.*` の Geometry リソースとして提供。帰属表示不要でゼロ依存と両立
- CAE 固有アイコン(境界条件、メッシュ等)は将来必要に応じて自作で追加

### 4.6 検証エラー表示規約

- **赤枠+ツールチップ方式**: エラー時に境界を `Wcu.Brush.Error` に変え、ホバー/フォーカスでメッセージをツールチップ表示。レイアウトを動かさない(高密度な表内でも行高が変わらない)
- この規約を `Validation.ErrorTemplate` としてテーマに含め、全コントロールで統一

## 5. コントロール実装規約

- **全てルックレス CustomControl 方式**: `Control` 派生 + `Themes/Generic.xaml` にデフォルトテンプレート。UserControl は使わない(利用側のテンプレート差し替え可能性を担保するため)
- **文字列**: コントロール内蔵文字列は極力持たない。表示文字列は依存関係プロパティで外から注入可能にし、デフォルトは英語。ライブラリに .resx は持たない(ローカライズはアプリの責務)

## 6. CAE 特化コントロール(全7種、第1弾スコープ)

### 6.1 単位付き数値入力(NumericBox)

- 単位表示、指数表記、min/max、増減ボタン
- **値の型は `double?`(nullable)**: null は「未入力/空欄」を表現(プロパティグリッドの「未設定」状態に必要)
- **確定タイミング**: Enter / フォーカス喪失で確定、Esc で編集前の値に復元。打鍵ごとの即時反映はしない
- **入力表記**: 通常表記+指数表記(`1e-3`、`2.5E+6`)。数式評価は初期スコープ外(後からパース層の拡張で追加可能)
- **単位換算はインターフェース委譲方式**: ライブラリは `IUnitProvider` 相当の差し込み口を定義し、「表示単位と内部値の変換」をアプリに委譲。ライブラリ自体は単位系を知らない
  - 想定ユースケース: 内部は SI、表示は mm/MPa。ユーザーが "500 mm" と入力すると内部値 0.5 m になる等
  - 単位系・換算テーブルの内蔵はしない(網羅・保守が本業を圧迫するため)

### 6.2 プロパティグリッド

- **明示的アイテムモデル方式をコア**とする: アプリが `PropertyItem` のコレクションを組み立てて渡す
  - 理由: CAE では「境界条件タイプ選択でパラメータ群が変わる」等の動的構成が頻出で、リフレクション方式は相性が悪い
- **型別派生クラス+暗黙 DataTemplate 方式**: `PropertyItem` 基底クラス+組み込み派生(`NumericPropertyItem`、`TextPropertyItem`、`BoolPropertyItem`、`ChoicePropertyItem` 等)。各派生は型付き `Value` プロパティを持ち、エディタは派生型に対応する DataTemplate で自動選択
  - アプリ独自エディタは「派生クラス+DataTemplate を1つ書く」だけで追加可能
  - `NumericPropertyItem` は `IUnitProvider` と min/max を持ち、エディタに NumericBox を使用
- リフレクションから自動構築するヘルパー層(`SelectedObject` 流)は後付け可能な設計にする(実装は需要が出てから)
- **v1 付帯機能**: カテゴリ折りたたみ+項目名フィルタボックスを含める。項目の説明は下部ペインではなく行ホバーのツールチップで表示(高密度 UI と整合)

### 6.3 モデルツリー

- **ListBox ベースのフラット化ツリー**として完全自作: 階層をフラットなリストに射影し、仮想化・複数選択・Shift 範囲選択・キーボード操作を ListBox の標準機能で実現
  - 理由: 数千〜数万ノード規模での性能と、複数選択(標準 TreeView が非対応)の両立
- **ノード契約はインターフェース+基底クラスの両方を提供**:
  - `ITreeNode`(Children / IsExpanded / IsVisible / IsSelected / Icon と変更通知)がコントロールの購読する最小契約
  - 通知実装済みの `TreeNode` 基底クラスも提供(アプリは基底継承で即使え、既存 VM 階層がある場合はインターフェース実装で接続)
- **表示/非表示トグル(目アイコン)は三状態の自動伝播を内蔵**: 親クリックで子孫全てに反映、子の一部のみ非表示なら親は「混在」表示
- アイコン、コンテキストメニュー対応
- ドラッグ&ドロップによる並べ替え/付け替えは v1 スコープ外

### 6.4 カラーマップ凡例

- **カラーマップモデルを内蔵**: Jet / Viridis / Coolwarm 等の定番スケール定義と「値→色」変換 API(連続/離散レベル分割、対数スケール、範囲外色)をライブラリに持ち、凡例コントロールはそのモデルを描画する
- コンター描画ユーティリティ(値配列→色配列の一括変換等)は範囲外(3D 側実装に依存するため)

### 6.5 ログコンソール

- **仮想化リスト + リングバッファ + バッチ反映を内蔵**:
  - 1行=1アイテムの仮想化リスト
  - 最大保持行数(リングバッファ)をライブラリが管理
  - UI スレッドへのバッチ反映(例: 100ms ごとにまとめて追加)を内蔵(秒間数千行のソルバー出力に耐える)
- **データ供給はドキュメントモデル分離方式**: スレッドセーフな `LogBuffer` モデルクラス(`Append(LogEntry)` を任意スレッドから呼べる。リングバッファ・バッチ通知を内包)を提供し、コントロールは `Source` プロパティでそれを表示するだけ。VM 単体テストも容易
- レベル別色分け、選択・コピー対応
- 自動スクロールは「最下部にいる時だけ追従、上にスクロールしたら停止」の定番挙動を内蔵

### 6.6 進捗表示

- **単一ジョブ表示の複合コントロール**: 進捗バー+ステータステキスト+経過時間+キャンセルボタン(`ICommand`)。不確定モード(IsIndeterminate)対応
- ジョブキュー UI(複数ジョブ管理)はアプリの領分としてスコープ外。複数表示が必要なら本コントロールを `ItemsControl` で並べる

### 6.7 グループパネル / Expander

- 設定項目の折りたたみセクション。他コントロールの部品にもなるため早期に実装

## 6.8 第2弾スコープ — アプリシェル要素

(2026-08-01 の設計インタビューで決定)

- **ゴール**: 「このライブラリだけで CAE アプリの画面一式(メニュー+ツールバー+ペイン分割+ステータスバー+ダイアログ)を組める」状態にする
- ドッキング UI は既定方針どおり対象外(必要になった時点で別アセンブリ)

### 6.8.1 レイアウト・シェル小物

- **GridSplitter / GroupBox / Separator**: スタイル自作(論点なし)
- **ToolBar / StatusBar**: WPF 標準 ToolBar / ToolBarTray のスタイル自作(オーバーフローメニュー対応を維持)。アイコンボタン用のキー付きスタイル `Wcu.Button.Icon` を追加
- **SearchBox**: 検索アイコン+プレースホルダー+クリアボタン付き入力欄の CustomControl。`SearchDelay`(既定 200ms)でデバウンス確定する `SearchText` を提供。**PropertyGrid のフィルタ欄も SearchBox に置き換えて重複実装を解消する**

### 6.8.2 DataGrid

- **WPF 標準 DataGrid のスタイル自作**方式(自作グリッドは工数が非現実的。ソート/編集/仮想化/列操作は実装済み機能を活用)
- **フル対応**: 列ヘッダー(ソート矢印含む)/行・セルの選択・ホバー/編集セル/検証エラー(赤枠+ツールチップ規約)に加え、行ヘッダー・行詳細(RowDetails)・グループ化ヘッダーまでスタイル化
- **列スタイルの自動適用**: `DataGridTextColumn` 等は WPF 組み込みの静的既定スタイルを使うためテーマの暗黙スタイルが届かない。`DataGridAssist.AutoApplyColumnStyles`(テーマで既定 ON)が既定スタイルのままの列だけをテーマスタイルに差し替える
- **検証エラー表示**: エラーは生成されたセル Content に付くため、`DataGridCell` が `Content.(Validation.HasError)` を監視して赤枠+ツールチップを表示。検証エラーが未確定の間、標準 DataGrid はソートを保留する(仕様)
- **RowDetails**: 選択追従(VisibleWhenSelected)はレイアウトが跳ねるため非推奨。行先頭の ▶ トグル(`Wcu.DataGrid.DetailsToggle` + `Wcu.DataGrid.VisibilityToBool`)で明示的に開閉するパターンを推奨(複数行同時展開可)

### 6.8.3 ウィンドウクローム・ダイアログ

- **WcuWindow(Window 派生)**: `WindowChrome` ベースでタイトルバーまでダークな自作クローム。アイコン/タイトル/システムボタンに加え、`TitleBarContent` プロパティでメニューや検索欄をタイトルバーに埋め込める(VS 風高密度レイアウト)。Win11 スナップレイアウト対応は将来課題
- **ダイアログ基盤は2層構成**:
  - `WcuDialogWindow`(WcuWindow 派生。ボタンバー規約内蔵で任意ダイアログの土台)
  - `WcuMessageBox.Show(...)` 静的 API(確認/警告/エラーの定型ダイアログ)
  - MVVM 用の `IDialogService` 抽象化はアプリの責務としてライブラリには含めない(ローカライズ方針と同じ分担)

### 6.8.4 オーバーレイ系

- **ToastHost(通知トースト)**: 明示的ホスト方式。アプリがレイアウトに ToastHost を 1 つ置き、`host.Show(メッセージ, レベル, 表示時間)` で表示。ウィンドウ自動検出の静的 API は採らない(仕組みが透明で MVVM と相性が良い)
- **BusyOverlay**: ContentControl ラッパー方式。任意の領域を `<ui:BusyOverlay IsBusy="...">` で囲むと、半透明ベール+スピナー+メッセージで内側への入力を遮断。部分適用(ビューポートだけブロック等)が可能

## 6.9 第3弾スコープ — 入力・ツールバー小物

(2026-08-01 の設計インタビューで決定)

- **狙い**: CAE で使用頻度が高いのに WPF 標準に存在しない入力・ツールバー小物を揃える。シェル拡張(ドキュメントタブ/Wizard)・軽量チャート・テーマ網羅(PasswordBox 等)は今回見送り(チャートは工数大のため着手時に別アセンブリ化を含めて再議論)

### 6.9.1 DropDownButton / SplitButton

- **ContextMenu 流用方式**: `DropDownMenu` プロパティに ContextMenu を指定してボタン押下で開く。スタイル・キーボード操作・UIA が既存の Menu 資産でそのまま効く(CAE ツールバーの用途はほぼメニュー項目の列挙)
- **SplitButton**: 左半分が `Command` 実行、右の ▼ が同じメニューを開く姉妹コントロール

### 6.9.2 PathBox

- 参照ボタン(...)付きのファイル/フォルダパス入力欄
- **ダイアログ内蔵方式**: `Microsoft.Win32` の OpenFileDialog / SaveFileDialog / OpenFolderDialog を直接呼ぶ(WPF 標準の一部なのでゼロ依存を維持。.NET 10 ターゲットのため OpenFolderDialog も使用可)
- `Mode`(OpenFile / SaveFile / Folder)と `Filter` を DP で公開。`BrowseRequested` イベントを Handled 可能にして、アプリがダイアログ呼び出しを差し替えられる口を残す

### 6.9.3 RangeSlider

- `Minimum` / `Maximum` / `LowerValue` / `UpperValue`、縦横対応
- **中央の選択範囲バー自体のドラッグに対応**: 範囲幅を保ったまま両端を同時に移動できる(コンター範囲の「幅そのままスライド」)
- ColorMapLegend と連携するデモをギャラリーに用意する

### 6.9.4 ColorPicker

- **2部品構成**: 選択 UI 本体の `ColorEditor`(HSV 面+色相スライダー+Hex/RGB 入力+パレット)と、それをポップアップで開くスウォッチボタン型 `ColorPicker`。ダイアログ埋め込みとツールバー利用の両方に対応
- **アルファ対応**: A スライダー+8桁 Hex(パーツの半透明表示が定番のため)。`IsAlphaEnabled` で非表示化可能

### 6.9.5 Vector3Box

- XYZ まとめ入力(NumericBox×3 の複合)
- **X / Y / Z を個別の double? DP で公開**: NumericBox と同じ nullable 規約で「未入力」を表現し、VM プロパティにそのままバインド可能。単位(`IUnitProvider`)・min/max は3軸共通で一括指定

### 6.9.6 PropertyGrid 連携

- 新規入力系に対応する派生を同フェーズで追加: `PathPropertyItem` / `ColorPropertyItem` / `Vector3PropertyItem`(派生クラス+DataTemplate 各1つの薄いラッパー)

## 6.10 第4弾スコープ — テーマ網羅+小物

(2026-08-01 の設計インタビューで決定)

- **狙い**: 「どの標準コントロールを置いてもダークテーマが崩れない」保証を実用範囲で完成させ、実需の高い小物3種を追加する
- **チャートは自作しない(方針決定)**: 収束モニタ等のチャート系は、実績ある外部ライブラリ(ScottPlot / OxyPlot 等を着手時に評価・選定)をベースに、テーマ適合アダプタ+CAE 向け複合コントロールを被せる構成とし、**別アセンブリ `WpfCustomUI.Charts`** として将来フェーズで実施(本体のゼロ依存ポリシーを維持)
- ドキュメントタブ / Wizard は引き続き見送り(ドッキング UI 検討と同時に設計する方が手戻りがない)

### 6.10.1 テーマ網羅(未スタイル標準コントロールの穴埋め)

- **TreeView / TreeViewItem**: 展開シェブロン+選択/ホバー。ModelTree とビジュアルを揃える(インデントガイド線等の拡張はなし。大規模・複数選択ツリーは引き続き ModelTree の領分)
- **ListView + GridView**: 列ヘッダーのホバー/押下スタイル。ソート矢印等の基盤は持たない(ソート付き表は DataGrid を使う、とギャラリーで案内)
  - 実装メモ: アプリレベルの暗黙 `ListViewItem` スタイルはテーマレベルの `GridViewItemContainerStyleKey` より優先されるため、暗黙スタイル自身が `View` の有無でテンプレート(GridViewRowPresenter ⇔ ContentPresenter)を切り替える。ヘッダー行の固定・横スクロール同期は `GridViewScrollViewerStyleKey` で提供
- **PasswordBox**: TextBox と同一の見た目
- **RichTextBox**: TextBox スタイルを TextBoxBase 共通に再編して適用
- **Hyperlink**: アクセント色、ホバーで下線+Hover 色
- **Label**: 既定 Padding 5 → 0 の軽量スタイル(高密度基準に整合)
- **未対応リスト(実需が出てから)**: DatePicker / Calendar(Calendar テンプレートが最大級の工数で、CAE での遭遇率と釣り合わないため)

### 6.10.2 InfoBar

- パネル上部等に置く**常駐型のインライン警告バナー**(「メッシュが古い」「単位系が未設定」等)。一過性の ToastHost と役割を分担
- **WinUI InfoBar 準拠の縮小版 API**: `Severity`(Info / Success / Warning / Error)、`Title`、`Message`、`IsOpen`(two-way 既定)、`IsClosable`、`ActionContent`(アクションボタン等の差し込みスロット)。閉じるボタンで `IsOpen=false` になり `Closed` イベントが発火
- **ビジュアル**: 背景は `Surface.Elevated` のまま、左端の色帯+アイコンで重要度を示す(無彩色 UI の方針を維持し、色はアクセントに限定)
- 凝ったレイアウトが必要な場合は `Message` を空にして `ActionContent` に任意 UI を入れる

### 6.10.3 ToggleSwitch

- ToggleButton 派生。トラック約 32×16 で `Wcu.ControlHeight`(24px)内に収まる高密度サイズ
- ラベルは右側 `Content`。On/Off 文字列は内蔵しない(文字列方針と整合)。三状態は非対応

### 6.10.4 ProgressRing

- BusyOverlay 内蔵スピナーの切り出しリファクタ。`IsActive` のみ公開、サイズは Width / Height(Viewbox スケール)、色は `Foreground` で指定
- BusyOverlay は本コントロールを内部利用する形に変更

## 6.11 第5弾スコープ — ポスト処理系 CAE 小物 第2弾

(2026-08-01 の設計インタビューで決定)

- **狙い**: ポスト処理ワークフローで既存部品(ColorMapLegend / RangeSlider)と対になる操作 UI を追加し、CAE 特化領域を完成に近づける
- **見送り**: 断面(カットプレーン)定義 UI・プローブ表示は 3D ビューポート実装と密結合のため対象外(コンター描画ユーティリティを外した判断と同型)。応力成分セレクタ・視点プリセット類は既存の ComboBox / DropDownButton の組み合わせで足りるため専用化しない。Wizard / ドキュメントタブは引き続き実需待ち

### 6.11.1 PlaybackBar(結果アニメーション再生バー)

- モード形状・時刻歴結果などのアニメーション操作 UI。3D 描画はアプリの領分で、本コントロールは「現在フレームと再生状態を公開する操作 UI」に徹する(LogConsole と同じ分担)
- **インデックスベースのフレームモデル**: `FrameCount`(総フレーム数)+ `CurrentFrame`(現在位置、TwoWay)が契約。時刻ベース・コレクションベースは採らない(CAE の再生対象は解析ステップ/モード番号/増分など離散列で、実時間と一致しないため)
- **再生タイマー内蔵**: `DispatcherTimer` により `IsPlaying` / `FramesPerSecond` / `IsLooping` で自走(置くだけで動く)。外部駆動したいアプリは `IsPlaying` を使わず `CurrentFrame` を直接動かせば共存できる
- **ループは単純リピートのみ**(`IsLooping`、既定 ON)。ピンポン再生はフレーム列に逆順を連結すればアプリ側で実現可能。実需が出たら `LoopMode` 追加で拡張(非破壊)
- **UI 構成**: ステップ戻し / 再生・一時停止 / ステップ送り / ループトグル + フレームスライダー + `現在/総数` 表示 + `FrameLabel`(string DP。「Step 12/50, t=0.24s」等の整形はアプリの責務)
  - 停止ボタンは持たない(一時停止+スライダーで代替)。速度選択 UI も持たない(`FramesPerSecond` DP のみ。表記の文化はアプリに依存するため)
  - 表示カスタマイズフラグは設けず、テンプレート差し替えに委ねる
- **キーボード**: Space(再生/停止)、← →(ステップ)、Home / End(先頭/末尾)

### 6.11.2 ColorScaleEditor(カラーマップ設定エディタ)

- Phase 5 の `ColorScale` / `ColorMap` モデルの編集 UI。ColorMapLegend が「表示」、本コントロールが「設定」で対になる
- **直接編集(ライブ反映)方式**: `Scale` DP に受け取った `ColorScale` インスタンスのプロパティを直接書き換える。同じインスタンスを見ている凡例・コンター表示は即座に追従する
  - 編集イベントは NumericBox の確定規約(Enter/フォーカス喪失)により十分離散的。重い再計算はアプリが `PropertyChanged` をスロットリング
  - 適用/キャンセルが必要な場面は「コピーを渡して OK 時に書き戻す」`WcuDialogWindow` パターンで実現。支援のため **`ColorScale.Clone()` を追加**する
- **主要+詳細の2段構成**:
  - 常時表示: ①カラーマップ選択(グラデーションプレビュー付き ComboBox。既定は組み込みプリセット一覧、`ColorMaps` DP で差し替え可)②Min/Max(NumericBox。`IUnitProvider` / `Format` 透過、Min ≥ Max は検証エラー規約で弾く)③レベル数(「離散レベル」ToggleSwitch + NumericBox 2〜256 既定 10。`LevelCount=null` ⇔ OFF)④対数(ToggleSwitch。Min/Max が非正のときは無効化+理由ツールチップ)
  - 詳細(Expander 収納): ⑤⑥範囲外色(「クランプ(既定)」CheckBox + 解除時 ColorPicker。null ⇔ クランプ)⑦NaN 色(ColorPicker)

### 6.11.3 実装メモ(Phase 11 完了時)

- `PlaybackBar`(`Playback/PlaybackBar.cs`): `CurrentFrame` は 0〜FrameCount-1 に Coerce。`MaxFrameIndex` / `PositionText`(「13 / 50」、1 始まり)は読み取り専用 DP でテンプレートに公開。`CurrentFrameChanged`(RoutedPropertyChangedEvent)をコードビハインド連携用に追加。Unloaded でタイマー停止・再表示で再開(リーク防止)。`FrameCount=0` はスタイルトリガーで無効表示
- `ColorScaleEditor`(`ColorMaps/ColorScaleEditor.cs`): ColorMap / IsLogarithmic / NaNColor はテンプレートから `Scale.*` へ直接 TwoWay バインド(INPC 追従)。null 許容の変換が絡む Min/Max・LevelCount・範囲外色は中間 DP(`MinimumValue` / `IsDiscrete` / `ClampBelow` 等)+ `_updating` ガードで同期。Min/Max の逆転は相互に `NumericBox.Minimum/Maximum` をバインドして既存の検証エラー規約で弾く。Min/Max の null 確定はモデル値へ復元。ラベル文字列は全て DP(既定英語、spec 5)
- `ColorMapToBrushConverter`: ColorMap → 水平グラデーションブラシ(ComboBox のプレビューに使用。公開 API)
- `ColorScale.Clone()` / `CopyFrom()` を追加(ダイアログの適用/キャンセルパターン支援)
- UIA 検証: `.dev/scripts/verify-postprocessing.ps1`(再生進行・一時停止・ステップ・ループ OFF 末尾自動停止・カラーマップ切替・離散 OFF・Max 編集の凡例追従・詳細 Expander のクランプ解除)

## 6.12 第6弾スコープ — 小物の最終弾

(2026-08-01 の設計インタビューで決定)

- **方針**: 小物として実需のあるものを出し切る最終弾。**ドッキングシステムと Charts は据え置き**——どちらも工数が大きく、要件(フローティング要否・レイアウト保存要否/描画性能・軸種別)は実アプリでの採用後に初めて確定するため、先行実装は作りすぎ・作り足りずの両リスクを負う

### 6.12.1 CheckComboBox(複数選択コンボ)

- 結果成分・荷重ケース・レイヤ表示などのフィルタ用。WPF 標準に存在しない定番の穴
- **選択モデル**: `ItemsSource` + `SelectedItems`(IList。アプリが ObservableCollection を渡す)。ListBox の「SelectedItems がバインド不可」問題をライブラリ側で解消する
- **閉時表示**: 既定は表示名の連結(内蔵文字列なし)。`SummaryFormat` DP(例 `"{0} selected"`)を設定したら件数表示に切り替え
- **すべて選択**: トライステートのチェックボックス行を内蔵し、`SelectAllContent` DP(既定 null=非表示)で有効化。ラベルはアプリが与える(内蔵文字列なし)

### 6.12.2 MatrixBox(行列入力)

- 異方性材料・座標変換・慣性テンソル入力用。Vector3Box の行列版
- **データモデル**: `double[,] Values` DP。セル確定のたびに新しい配列インスタンスを作って DP にセット(TwoWay で VM に届く。PointCollection 等と同じ WPF 常套手段)。6×6 程度のコピーコストは無視できる
- `Rows` / `Columns` DP(既定 3×3)。`Values` の次元と不一致なら表示側(Rows/Columns)を優先
- `IsSymmetric` DP: (i,j) 編集で (j,i) に自動ミラー(剛性・コンプライアンス行列用)
- `RowHeaders` / `ColumnHeaders`(文字列リスト、null なら非表示)
- `UnitProvider` / `Format` は全セルの NumericBox へ透過

### 6.12.3 ModelTree インライン名前変更(既存強化)

- **契約**: `IRenamableNode : ITreeNode`(setter 付き `Name`)でオプトイン。実装した型だけ編集可能。`ITreeNode` は無変更(非破壊)。付属の `TreeNode` 基底クラスには実装を追加
- **トリガー**: F2 + 公開メソッド `BeginRename(node)`(アプリがコンテキストメニューから呼ぶ)。ダブルクリックはアプリの領分(ズーム/フィット等)として空けておく
- **編集規約**: Enter 確定 / Esc 取消 / 空白のみの名前は拒否してキャンセル扱い

### 6.12.4 KeyGestureBox(ショートカット入力欄)

- 設定ダイアログ用。フォーカス中のキー押下をキャプチャして表示
- **値**: `KeyGesture?` 型の `Gesture` DP 1本(null=未割り当て)。永続化は WPF 標準の `KeyGestureConverter` で文字列往復可能
- **規約**: 文字・数字キーは修飾キー必須(WPF の KeyGesture 仕様に準拠)、ファンクションキー・Delete 等は単独可。右端×ボタンでクリア(SearchBox パターン)、Esc で編集前の値に復元

### 6.12.5 Wizard / StepIndicator

- **2部品構成**: `StepIndicator`(ステップ進捗の表示専用。ソルバー進行表示等に単体転用可)+ `Wizard`(ページホスト+戻る/次へ/完了/キャンセル+StepIndicator 内蔵)。ウィンドウはアプリが用意(WcuDialogWindow の分担どおり)
- `Wizard` は `WizardStep`(Header + Content)を並べる ItemsControl 系。`CurrentIndex` は TwoWay
- **バリデーション**: `CanGoNext` / `CanFinish` DP(宣言的無効化)+ キャンセル可能な `Navigating` イベント(押下時検証)の両方を提供(Window.Closing と同じ常套パターン)
- 完了/キャンセルは `Finished` / `Cancelled` ルーティングイベント。ダイアログを閉じるのはアプリ
- ボタン文字列は既定英語+DP 差し替え(spec 5)

### 6.12.6 実装メモ(Phase 12 完了時)

- `CheckComboBox`(`Input/CheckComboBox.cs`): ItemsControl 派生。項目コンテナは行全体クリック可能な `CheckComboBoxItem : CheckBox`。選択同期は Checked/Unchecked のバブリングを一括処理(**Click は使わない**——UIA の Toggle は Click を発生させないため)。`SummaryText` / `SelectAllState` は読み取り専用 DP。「すべて選択」は操作前の `SelectAllState` で全選択/全解除を判定(三状態のトグル遷移に依存しない)。`SelectAllState` 更新はバインド経由で Checked/Unchecked を再発火させるため `_updating` ガード必須。Popup は StaysOpen=False + 「閉じた直後 200ms のトグルクリック無視」で再オープンのちらつきを防止
- `MatrixBox`(`Input/MatrixBox.cs`): テンプレートは `PART_Grid` のみで、セル(NumericBox)とヘッダーはコードで構築。セルは内部 VM(`MatrixCell` INPC)に TwoWay バインドし、確定のたびに新しい `double[,]` を作って `Values` を差し替え。null 確定(空欄)はモデル値へ復元。`IsSymmetric` のミラーは配列生成時に (j,i) へ書き込み+ミラー先セル表示を silent 更新
- ModelTree 名前変更: `FlatTreeItem.IsRenaming`(internal set)を行テンプレートの DataTrigger が参照して TextBox 表示に切替。確定/取消は ModelTree が KeyDown / LostKeyboardFocus のバブリングで一括処理(仮想化行のため個別購読しない)。フォーカスがコントロール外へ移った場合は奪い返さない。名前変更中は ←→ をキャレット移動として素通し
- `KeyGestureBox`(`Input/KeyGestureBox.cs`): PreviewKeyDown でキャプチャ(Alt 系は `e.SystemKey`)。修飾キー単独は無視、Tab は素通し。妥当性は `new KeyGesture()` の `NotSupportedException` に委譲(WPF 仕様と完全一致)。表示は `GetDisplayStringForCulture`
- `Wizard` / `StepIndicator`(`Wizard/`): ステップ切替は全 `WizardStep` を Grid 重ねで生存させ Visibility 制御(入力状態保持)。ボタンはテンプレート内 `PART_*` を ClickEvent クラスハンドラで処理。`StepHeaders`(読み取り専用 DP)経由でテンプレート内 StepIndicator にヘッダーを供給。`GoNext()` / `GoBack()` は公開(Navigating 検証込み)
- **重要な学び**: ItemsControl 派生のカスタムコントロールは WPF 既定の自動化ピアが「アイテムのみ」を UIA に公開し、テンプレート内のボタン等が見えなくなる。`OnCreateAutomationPeer` で `FrameworkElementAutomationPeer` を返して解決(CheckComboBox / Wizard / StepIndicator に適用)
- UIA 検証: `.dev/scripts/verify-miscinputs.ps1`(チェック連動・すべて選択・対称ミラー・ジェスチャキャプチャ/クリア・CanGoNext 無効化・Finished 発火・F2 名前変更/Esc 取消)

## 6.13 ドッキングシステム(Phase 13)

(2026-08-01 の設計インタビューで決定)

### 6.13.1 スコープと方式

- **機能スコープは VS 完全型(L)**: フローティングウィンドウ必須、マルチモニタ、ドッキングガイド、タブ化、自動隠し(ピン留め)、レイアウト保存/復元
- **Dirkster.AvalonDock v5 を採用**(スクラッチしない)。判断根拠:
  - フルセットのスクラッチは月単位の工数で、WPF の OSS で完遂例が AvalonDock 以外に事実上ないことが難度の証明
  - v5 は .NET 10 対応済みで保守が活発。MS-PL + Apache 2.0 デュアルライセンスで商用利用も無償
  - 採用実績: Stride(ゲームエンジン)、Macad3D(OSS 3D CAD)、Microsoft Profile Explorer 等
- **依存ゼロ方針の初の例外**として、新プロジェクト `WpfCustomUI.Docking` に依存を隔離する(`WpfCustomUI.Controls` コアは依存ゼロを維持。Charts で予定していた「外部依存は別アセンブリ」パターンの適用)

### 6.13.2 提供物(ラップは薄く)

- **完全ファサードは作らない**(ドッキングの抽象化は典型的な「漏れる抽象化」であり、WPF に代替ライブラリがない以上、差し替え保険の価値も低い)。AvalonDock の型(DockingManager / LayoutAnchorable 等)はアプリへ素通しする
- **① WcuDockTheme(`Theme` 派生)**: AvalonDock 公式のテーマ拡張点(`DockingManager.Theme`)に乗る。MS-PL の VS2013 Dark テーマ XAML をフォークし、色を `Wcu.Brush.*` トークンへの `DynamicResource` 参照に置換。フローティングウィンドウにも正しく伝播し、将来のライトテーマにもトークン差し替えで追従
  - 暗黙スタイル上書き方式は不採用(テーマ機構の迂回はフローティングウィンドウにリソースが届かない問題を踏む)
  - 置換は「目に付く要素(タブ・タイトルバー・自動隠しサイドバー・ドッキングガイド)から順」に段階的に進める。初回は細部が VS2013 Dark のままでも成立
- **② DockLayout(静的永続化ヘルパー)**: `Save(manager, path)` / `SaveToString(manager)` / `Load(manager, path, Func<string, object?> resolveContent)`。復元時の再結合は ContentId リゾルバに委譲
  - **食い違い規約**: リゾルバが null を返した項目は破棄(廃止ツールウィンドウ対策)。レイアウト XML に存在しない新規ツールウィンドウはアプリ既定位置のまま(XML が知らないものには触らない)
  - 保存先・保存タイミングはアプリの領分(自動保存サービスは作らない。必要なら A の上に約10行で書ける)
- ViewModel 基底は `Dirkster.AvalonDock.Mvvm` で足りるかを実装時に評価し、足りるなら再エクスポートせず素通しで使う

### 6.13.3 デモと検証

- **フルシェルデモ(本命)**: ギャラリーの「CAE シェルを開く」ボタンで WcuWindow + DockingManager 全面の別ウィンドウを起動。メニューバー・ステータスバー・中央ドキュメント(疑似ビューポート)+ ModelTree / PropertyGrid / LogConsole / ColorMapLegend のツールウィンドウ構成で、ライブラリ全部入りの実演を兼ねる
- **ギャラリーページ**: フルシェル起動ボタン+機能説明+レイアウト保存/復元の操作ボタン置き場(ページへの DockingManager 埋め込みはしない)
- **検証**: 自動隠し・フローティング・タブ化は `LayoutAnchorable.Float()` / `ToggleAutoHide()` 等をスクリプトからプログラム的に駆動してスクリーンショット確認。レイアウト保存/復元は Save → 配置変更 → Load のラウンドトリップ一致で検証。ドラッグのドッキングガイド操作のみ目視

### 6.13.4 実装メモ(Phase 13 完了時)

- **バージョン**: v5 系はまだ preview のみのため安定版 **4.74.1** を採用(`Dirkster.AvalonDock` + `Dirkster.AvalonDock.Themes.VS2013`)。API は同一系統なので v5 安定化後はパッケージ更新のみの見込み
- **テーマは XAML フォーク不要だった**: 現行の VS2013 テーマは「`ResourceKeys`(ComponentResourceKey 約80個)にブラシを与えると、制御テンプレート側が DynamicResource で拾う」再配色設計(`DictionaryTheme` + パレット辞書を実行時に組み立てる方式)。そこで `WcuDockTheme : DictionaryTheme` とし、
  - パッケージの `Generic.xaml` / `OverlayButtons.xaml`(制御テンプレート)はそのままマージ
  - `Themes/WcuDockResources.xaml` で全 ResourceKeys に `<SolidColorBrush Color="{DynamicResource Wcu.Color.*}"/>` を供給
  - テーマ辞書はフローティングウィンドウにも AvalonDock が自動でマージするため、フローティングも含めて実行時にテーマ・アクセント変更へ追従する
- **コアへの追随変更**: アクセントの「色プリミティブ」`Wcu.Color.Accent.Default/Hover/Pressed/Muted` を Tokens.Dark に追加し、`ThemeManager.SetAccent` がブラシと合わせて色キーも上書きするよう拡張(ブラシキーを参照できない外部テーマ再配色機構への供給路)
- `DockLayout`(`WpfCustomUI.Docking/DockLayout.cs`): `XmlLayoutSerializer` の薄いラッパー。`LayoutSerializationCallback` で ContentId → リゾルバ委譲、null は `e.Cancel = true` で破棄。`LoadFromString` / `SaveToString` も提供(アプリ設定埋め込み・既定レイアウトのリセット用)
- デモ: `DockingShellWindow`(WcuWindow + メニュー/ステータスバー + DockingManager 全面。モデル/プロパティ/凡例/ログ + ドキュメント2枚)。「表示」メニューは現在レイアウトから ContentId で LayoutAnchorable を検索して `Show()`(`Layout.Hidden` も検索対象)。「レイアウト」メニューで保存/復元/既定に戻す(既定 = Loaded 時に `SaveToString` で控えた XML)。ギャラリーには起動ボタン+解説の `DockingPage`、`--dockshell` 引数で起動直後にシェルを開く
- UIA 検証: `.dev/scripts/verify-docking.ps1`(初期表示 → レイアウト保存 → キャプションのマウスドラッグでフローティング化(ガイド表示のスクリーンショット込み)→ レイアウト復元でドックに戻ることを確認)。**注意**: UIA のデスクトップ直下列挙は owned window を取りこぼすことがあるため、シェルウィンドウは Win32 `EnumWindows` で hwnd を得て `AutomationElement.FromHandle` で掴む。ドラッグはタブではなくキャプションから行うと確実
- スモークテスト(`--smoke`)は全ページに加えて `DockingShellWindow` の生成も検証する

## 7. テスト方針

- **UI に依存しないロジックのみ** xUnit でテストする:
  - ツリーのフラット化・選択範囲計算
  - 値→色変換(カラーマップ)
  - 数値+単位のパース
  - ログのリングバッファ
- UI 自動テストは当面やらない(費用対効果が低い)。UI はギャラリーで目視検証

## 8. 実装フェーズ

| フェーズ                                         | 内容                                                                                                                                                 | 状態                 |
| ------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------- |
| **Phase 0 — 基盤**                               | `WpfCustomUI.Controls` へ改名、`XmlnsDefinition`、デザイントークン定義、ダークテーマ辞書、ギャラリー骨格、テストプロジェクト追加                     | ✅ 完了 (2026-07-31) |
| **Phase 1 — 標準コントロールスタイル(コア集合)** | Button / TextBox / ComboBox / CheckBox / ScrollBar / TabControl / Menu / ToolTip / Slider / ProgressBar など、CAE コントロールの部品になるものを優先 | ✅ 完了 (2026-07-31) |
| **Phase 2 — 部品系 CAE コントロール**            | 単位付き数値入力、グループパネル/Expander(他の部品になるため先行)                                                                                    | ✅ 完了 (2026-07-31) |
| **Phase 3 — プロパティグリッド**                 | Phase 2 のエディタを利用                                                                                                                             | ✅ 完了 (2026-08-01) |
| **Phase 4 — モデルツリー**                       | フラット化ツリー。ロジックのテスト重点                                                                                                               | ✅ 完了 (2026-08-01) |
| **Phase 5 — 独立系**                             | ログコンソール / 進捗表示 / カラーマップ凡例(相互独立なので順不同)                                                                                   | ✅ 完了 (2026-08-01) |
| **Phase 6 — シェル軽量群**                       | GridSplitter / GroupBox / Separator / ToolBar / StatusBar / SearchBox(+PropertyGrid フィルタ改修)                                                    | ✅ 完了 (2026-08-01) |
| **Phase 7 — ウィンドウ系**                       | WcuWindow(クローム) / WcuDialogWindow / WcuMessageBox / ToastHost / BusyOverlay                                                                      | ✅ 完了 (2026-08-01) |
| **Phase 8 — DataGrid**                           | 標準 DataGrid のフルスタイル化(最大工数のため独立フェーズ)                                                                                           | ✅ 完了 (2026-08-01) |
| **Phase 9 — 入力・ツールバー小物**               | DropDownButton / SplitButton / PathBox / RangeSlider / ColorPicker(ColorEditor) / Vector3Box + PropertyGrid 連携(Path/Color/Vector3 PropertyItem)    | ✅ 完了 (2026-08-01) |
| **Phase 10 — テーマ網羅+小物**                   | TreeView / ListView(GridView) / PasswordBox / RichTextBox / Hyperlink / Label のスタイル穴埋め + InfoBar / ToggleSwitch / ProgressRing               | ✅ 完了 (2026-08-01) |
| **Phase 11 — ポスト処理系小物 第2弾**            | PlaybackBar(結果アニメーション再生バー) / ColorScaleEditor(カラーマップ設定エディタ、`ColorScale.Clone()` 追加)                                      | ✅ 完了 (2026-08-01) |
| **Phase 12 — 小物の最終弾**                      | CheckComboBox / MatrixBox / ModelTree インライン名前変更 / KeyGestureBox / Wizard(StepIndicator)                                                      | ✅ 完了 (2026-08-01) |
| **Phase 13 — ドッキング**                        | `WpfCustomUI.Docking` 新設(Dirkster.AvalonDock 4.74.1)。WcuDockTheme(ResourceKeys 再配色+Wcu トークン)/ DockLayout 永続化ヘルパー / フルシェルデモ  | ✅ 完了 (2026-08-01) |
| **(将来)Charts**                                 | 外部ライブラリ(ScottPlot / OxyPlot 等を選定)ベースの別アセンブリ `WpfCustomUI.Charts`。収束モニタ等の CAE 向け複合コントロールを提供                 | 構想                 |
