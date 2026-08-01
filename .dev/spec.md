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
  - Dark(既定)+ Light(Phase 15 で追加、VS 2022 Light 準拠)の2バリアント
- 標準コントロールのスタイルは全部を一括で作らず、必要なコントロールから順次自作

### 4.1 デザイントークン

- **2層構造(セマンティック Color →セマンティック Brush)**(Phase 15 で改訂):
  - 第1層: 役割ベースの色プリミティブ(`Wcu.Color.Surface.Window` / `Wcu.Color.Text.Primary` 等)。ブラシを使えない消費者(Docking のドックテーマ、Charts のラスタ描画)が DynamicResource で参照する
  - 第2層: 意味付きブラシ(`Wcu.Brush.Surface.Window` 等)が第1層を参照。コントロールスタイルはこちらを参照する。テーマ切替はトークン辞書の差し替えで実現
  - リテラル名のパレット(`Wcu.Color.Gray.800` 等)は廃止(「ライトで Gray.900=ほぼ白」という嘘の名前を避ける)。Color 実値は各テーマファイルに直接書く
  - 注意: コンパイル済み(遅延)辞書では `<StaticResource x:Key>` エイリアスエントリを他エントリから参照できない(実行時 XamlParseException)。エイリアスは使わないこと
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
- **`ThemeManager.GetSystemTheme()`**: Windows のアプリモード設定(レジストリ `AppsUseLightTheme`)を読むだけのヘルパー。起動時に使うか・実行中に追従するかはアプリの責務
- 内部ファイル構成: `Themes/Tokens.Dark.xaml` / `Themes/Tokens.Light.xaml`(セマンティック Color+Brush。両ファイルのキー構成は同一に保つ)、`Themes/Controls/*.xaml`(コントロール別スタイル、テーマ非依存)

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

## 6.14 チャート(Phase 14)

(2026-08-01 の設計インタビューで決定)

### 6.14.1 スコープと方式

- **Dirkster.AvalonDock で確立した「外部依存は別アセンブリ」パターンを適用**し、新プロジェクト `WpfCustomUI.Charts` に依存を隔離する(コアは依存ゼロを維持)
- **ScottPlot 5 を採用**(調査時点 5.1.58 / 2026-03、MIT、.NET 10 公式対応)。判断根拠:
  - CAE の用途(数十万〜数百万点の時刻歴、ストリーミング更新される収束モニタ、対数軸)に性能面で応えられるのは実質 ScottPlot のみ(Signal/SignalXY プロット)
  - OxyPlot は保守停滞が明確(最終リリース 2024、メンテナが時間確保困難と明言)。LiveCharts2 はダッシュボード向きで大規模データに不向き。スクラッチは月単位の工数
  - トレードオフ: ラスタ描画のためテーマ適用は XAML でなくコード(下記②で解決)。SkiaSharp のネイティブ依存が付く(別アセンブリ隔離でコアは無影響)

### 6.14.2 提供物(ハイブリッド: テーマ+複合コントロール少数+素通し)

- 完全ファサードは作らない(ドッキングと同じ判断)。自由プロットは ScottPlot の型を素通しで使う
- **① WcuPlot(`WpfPlot` 派生)+ WcuChartTheme**: ロード時に Wcu トークン配色(背景・軸・グリッド・凡例・シリーズパレット)を自動適用し、テーマ/アクセント変更に自動追従。静的 `WcuChartTheme.Apply(Plot)` も公開(素の WpfPlot・画像出力用 Plot への手動適用)
  - **コアへの追随変更**: `ThemeManager` に `ThemeChanged` イベントを追加(SetTheme / SetAccent / ResetAccent で発火)。ラスタ描画系の消費者が再配色・再描画するためのフック
- **② 複合コントロール4つ**(いずれも WcuPlot を内包):
  - **ConvergenceMonitor**: 収束モニタ。`ConvergenceSeries`(Name + スレッドセーフ `Append(double)` / `Clear()`)を複数受け、約 100ms のスロットリングでまとめて再描画(LogBuffer と同じ発想。ソルバーの毎反復 Append に耐える)。DP: `Threshold`(しきい値破線、null で非表示)/ `IsLogScale`(既定 true)。X は反復番号で自動スクロール
  - **HistoryChart**: 汎用折れ線。`ChartSeries` モデル(Name / X,Y 配列 / 任意の色・線種、INPC)+ `SeriesSource` バインド。データ更新は「新しい配列をセット」で通知(MatrixBox と同じ常套手段)。色未指定はトークン由来のシリーズパレットを自動割当。カーソル読取・軸ラベル付き
  - **FrequencyResponsePlot**: 周波数応答。振幅(上段)+位相(下段)の2段構成で X 軸(周波数・対数)を共有。`FrequencyResponseSeries`(Frequencies / Magnitudes / Phases は任意)。DP: `ShowPhase`(既定 true)/ `MagnitudeInDecibels`(既定 true)
  - **HistogramChart**: 結果量の分布表示。`Values` + `BinCount`(既定 20)。集計は ScottPlot の Histogram 機能に委譲。DP: `Normalize`(度数/確率密度、既定は度数)
- シリーズの MVVM 規約は既存の「明示的アイテムモデル」(PropertyItem / ITreeNode)の流儀に合わせる

### 6.14.3 デモと検証

- ギャラリーに「Charts」ページ: 4複合コントロールのデモ(収束モニタはソルバーシミュレーション連動)+ 素の WcuPlot 自由プロット例。ボード線図が HistoryChart の対数軸設定でも表現できることも例示
- 検証は既存流儀(--smoke へのページ組み込み + UIA スクリプト + スクリーンショット目視)

### 6.14.4 実装メモ(Phase 14 完了時)

- **バージョン**: ScottPlot.WPF **5.1.59** を採用。推移的依存の SkiaSharp.Views.WPF が net4x TFM のみのため NU1701 警告が出るが、ScottPlot 公式サポート構成のため `<NoWarn>NU1701</NoWarn>` で抑制
- **WcuChartTheme**(`WpfCustomUI.Charts/WcuChartTheme.cs`): 適用時点の `Wcu.Color.*` トークンを Application リソースから読み取り、`FigureBackground` / `DataBackground` / `Axes.Color` / `Grid.Major(Minor)LineColor` / `Legend.*` / `Add.Palette`(先頭=アクセント現在値の 8 色パレット)を設定。`plot.Font.Automatic()` で日本語ラベルにも対応
- **WcuPlot**: ctor で配色適用、Loaded/Unloaded で `ThemeManager.ThemeChanged` を購読/解除(静的イベントのリーク防止)。再適用後に `ThemeApplied` イベントを発火し、複合コントロールはこれを受けてシリーズ再構築(既存プロットの色は Apply では変わらないため)
- **ホイール競合対策(Ctrl+ホイール=ズーム)**: ScottPlot はホイールズーム後もイベントを Handled にしないため、外側に ScrollViewer があるとページスクロールとズームが同時に起きる。WcuPlot は既定で **Ctrl+ホイールのみズーム**とし(`WheelZoomRequiresCtrl`、スクロールしない全面配置なら false で素のホイールズームに戻せる)、素のホイールは `OnPreviewMouseWheel` で内部 SKElement に届く前に止めて WcuPlot 起点のバブリングイベントとして再発行→外側のページスクロールに素通しする。ズーム時(Ctrl あり)は `OnMouseWheel` で Handled にしてページスクロールを止める
  - **注意**: ScottPlot 既定の `MouseWheelZoom` は Ctrl を「縦軸ロック」キーに使っておりゲートと競合するため、ctor でロックキーを Shift(横軸)/ Alt(縦軸)に付け替えている
  - 検証: `.dev/scripts/verify-charts-wheel.ps1`(素のホイール=スクロールのみ・ズームなし / Ctrl+ホイール=ズームのみ・スクロールなし)
- **対数軸は log10 変換方式**(ScottPlot 5 の公式流儀): データを log10 変換して描画し、`NumericAutomatic + LogMinorTickGenerator + IntegerTicksOnly + LabelFormatter` で目盛りを 10^n 表示(`ChartHelpers.CreateLogTickGenerator`)。ConvergenceMonitor の縦軸・FrequencyResponsePlot の横軸で使用
- **ConvergenceMonitor**: `ConvergenceSeries.Changed` はワーカースレッドから飛ぶため、ハンドラは volatile な dirty フラグを立てるだけにし、UI 側 `DispatcherTimer`(100ms、Loaded/Unloaded で開始/停止)が dirty のときだけ再構築する
- **FrequencyResponsePlot**: テンプレートは WcuPlot 2 枚(`PART_MagnitudePlot` / `PART_PhasePlot`)。X 軸同期は各 Plot の `RenderManager.AxisLimitsChanged` で相手に `SetLimitsX` をコピー(再入ガード付き)。`ShowPhase=False` は Trigger で行 Height=0 + Collapsed
- **HistogramChart**: ビン集計は自前実装(min/max 等幅、max ちょうどは最終ビン、全値同一でも幅を確保)。ScottPlot の `Statistics.Histogram` は API 変動があるため使わない。`Normalize` は総数×ビン幅で除して確率密度化
- **HistoryChart のカーソル読取**: `Crosshair` プロットタブル + `VerticalLine.Text` / `HorizontalLine.Text` に座標を表示(MouseMove で `GetCoordinates(pixel × DisplayScale)`)。MouseLeave で非表示
- デモ(`ChartsPage`): 解析開始ボタンで `Task.Run` の疑似ソルバー(Thread.Sleep(25) × 最大400反復、途中で荷重ステップ切替の残差ジャンプ)がワーカースレッドから直接 `Append`。荷重-変位曲線 / 2自由度 FRF / von Mises 分布(Box-Muller)/ 素の WcuPlot(移動平均)の例
- UIA 検証: `.dev/scripts/verify-charts.ps1`(ストリーミング進行と収束検出 → 十字カーソル → 位相パネル切替 → ビン数変更・密度正規化 → 自由プロット)+ `verify-charts-accent.ps1`(アクセント変更後のチャート再配色)

## 6.15 ライトテーマ(Phase 15)

(2026-08-01 の設計インタビューで決定)

### 6.15.1 スコープと配色

- **Tokens.Light.xaml を追加**し、`WcuThemeVariant.Light` を有効化する(切替機構・`ThemeChanged`・`Wcu.Color` プリミティブ供給路は Phase 13/14 で整備済み。設計どおり「トークンファイル追加+enum メンバー追加」で乗る)
- **配色は VS 2022 Light 準拠**: ダークが VS Dark 系なので同じデザイン言語の対とし、「どのグレー段階をどの面に使うか」の対応関係を写す
- **アクセントは現行 #007ACC を維持**し、白背景でコントラスト不足が出た箇所のみ VS Light の #005FB8 寄りに調整
- **既定テーマは Dark のまま**(ダークファースト方針維持)。ライトはオプトイン

### 6.15.2 トークン構造(セマンティック Color キーの正式化)

- **問題**: Docking のテーマ辞書(86箇所)と Charts は `Wcu.Color.Gray.850` / `Wcu.Color.White` などリテラル名のプリミティブを直接参照している。ライトで値だけ反転させると「Gray.900=ほぼ白」という嘘の名前になり、リテラルのままだと外部アセンブリだけ暗いまま残る
- **決定**: セマンティックな Color キー(`Wcu.Color.Surface.Window` / `Text.Primary` / `Border.Default` など、既存 Brush 層と同じ役割名)を各テーマに追加し、**Docking / Charts はセマンティック Color キー参照へ移行**する。`Wcu.Color.Accent.*`(Phase 13)で作った前例の一般化
  - 当初案の「StaticResource エイリアス(セマンティック名 → リテラルプリミティブ)」は**実装不可と判明**: コンパイル済み(遅延)辞書ではエイリアスエントリを他エントリ(ブラシの Color 等)から解決できず、実行時 XamlParseException になる
  - 最終形: リテラル層(Gray.* 等)を**廃止**し、セマンティック Color キーに実値を直接定義。Brush 層はセマンティック Color を StaticResource 参照(単段参照は問題ない)。二重管理も嘘の名前も発生しない

### 6.15.3 ギャラリーと API

- **ナビゲーション(左サイドバー)下部に常設の Dark/Light トグル**(ToggleSwitch)。全ページを見ながらその場で切替できるようにし、目視検証と UIA スクリプトの両方で使う
- **`ThemeManager.GetSystemTheme()`** を追加(レジストリ `AppsUseLightTheme` の読取のみ)。起動時に使うか・実行中に追従するかはアプリの責務(SystemEvents 監視の自動追従は提供しない)

### 6.15.4 検証

- **自動スクリーンショット横断**: UIA スクリプトで全 17 ページ×両テーマを撮影して目視レビュー(ドッキングシェル・ダイアログ・Toast 含む)
- **静的検査**(rg のパターン検索で機械的に洗い出し):
  - コントロールテンプレート内の #RRGGBB ハードコード色(トークン外の色を潰す)
  - セマンティックブラシの `StaticResource` 参照(実行時切替で更新されないバグの源。トークン→トークンのエイリアスは除く)

### 6.15.5 実装メモ(2026-08-01 完了)

- **トークン**: `Tokens.Light.xaml` 新設(VS 2022 Light 準拠: Window=#EEEEF2 / Panel=#F5F5F7 / Input=#FFFFFF / Border=#CCCEDB、ステータス色は白背景で判読できる濃色 #C42B1C 等)。`Tokens.Dark.xaml` は 6.15.2 の最終形へ再構成。ホバー等のオーバーレイはダーク=半透明白、ライト=半透明黒
- **Surface.Alternate ブラシを追加**: DataGrid の交互行背景が `#08FFFFFF` ハードコードでライトで不可視だったのをトークン化(静的検査で検出)
- **アクセント派生のバリアント対応**: `AccentPalette.FromBase(color, variant)` を追加。Muted はダーク=暗色化(-0.35)、ライト=淡色化(+0.45)。`SetTheme` はアクセント上書き中なら新バリアントで再計算する(`ThemeManager` が基準色を保持)
- **Charts のライト対応**: `WcuChartTheme` はセマンティック Color キー参照へ移行し、シリーズパレットは背景輝度で ダーク用(明色系)/ライト用(濃色系)を自動選択。`HistoryChart` 十字カーソルの文字色も `Text.OnAccent` トークン化
- **Docking**: `WcuDockResources.xaml` の全 86 ブラシをセマンティック Color キー参照へ移行(Gray.850→Surface.Panel、Gray.700→Border.Default/Control.Hover 等、役割で振り分け)
- **ギャラリー**: ナビ下部に「ライトテーマ」ToggleSwitch 常設。`--light` 起動引数を追加(スクリーンショット・スモーク用)
- **検証**: `--smoke` 両テーマ通過、xUnit 94 件通過。`.dev/scripts/capture-themes.ps1`(同一プロセスで Dark 全17ページ → 実行時トグル切替 → Light 全17ページを撮影 = 実行時切替検証を兼ねる)+ `capture-dockshell-light.ps1`(ドッキングシェル)で全数目視レビュー済み。ハードコード色の静的検査では ColorPicker のチェッカー柄/色相グラデーション・WcuWindow 閉じるボタン赤(#E81123)・ドッキングガイド半透明色など「テーマ非依存で意図的」なもののみ残置

## 6.16 3D ビューポート (Phase 16)

(2026-08-01 の設計インタビューで決定)

### 6.16.1 方向性とレンダリング基盤

- **ハイブリッド方針を 3D にも適用**: 別アセンブリ `WpfCustomUI.Viewport3D` で実レンダリング付きビューポートを提供する。従来の「3D 描画はアプリの領分」を転換し、見送ってきた周辺部品(断面 UI・プローブ・コンター描画等)が将来ここに合流できる土台を作る。コアの依存ゼロは維持
- **基盤は Silk.NET の Direct3D11 + DXGI バインディング + D3DImage で自作エンジン**:
  - Helix Toolkit(v3 は現役)は検討の末不採用。理由: 内部依存 SharpDX が上流開発終了で長期基盤として負債、毎フレーム頂点更新(変形アニメ)がマネージドジオメトリ経由で大規模メッシュに不向き、アイソバンド/断面キャップ/GPU ピッキング等は結局カスタムシェーダ地獄になり「制御できない土台の上でエンジンを書く」ことになる
  - OpenGL ではなく D3D11 を選ぶ決定的理由: **D3DImage(D3D11 共有テクスチャ→D3D9Ex)は WPF 合成にネイティブ統合され、エアスペース問題が起きない**。AvalonDock のフローティング/タブや BusyOverlay を 3D ビューに重ねられることが本ライブラリでは必須要件。OpenGL は HwndHost 経由になり品質が落ちる
  - 自作のため Phase 16 は「最小核」、以降のフェーズで積み増す長期戦と割り切る(1フェーズ完結の ScottPlot 型ではない)

### 6.16.2 Phase 16 のスコープ(最小核)

- **`WcuViewport` コントロール**: D3DImage ホスト、デバイス/共有テクスチャ管理、リサイズ・デバイスロスト処理、MSAA、WARP フォールバック(GPU なし・RDP 対応)、背景色は Wcu トークン連動(`ThemeChanged` 追従)
- **描画はオンデマンド**(変更・操作時のみ再描画。連続レンダーループなし)
- **カメラ**: オービット回転 / パン / ズーム / Fit、透視投影⇔平行投影(CAE は平行投影多用のため初回から両対応)
- **メッシュ表示**: 三角形メッシュ+ライティング(単色)、節点スカラー+`ColorScale` の 1D テクスチャによるコンター表示、ワイヤフレーム/エッジ重畳
- **軸トライアッド**(隅の XYZ 表示)
- **見送り(Phase 17 以降)**: ピッキング/選択(選択モデル設計は別議論)、ViewCube、断面カット、変形アニメーション、グリフ、注釈、ジェスチャのカスタマイズ機構

### 6.16.3 データ API

- **`ViewportMesh` モデル**(ChartSeries と同じ流儀): 節点座標+三角形インデックス+任意の節点スカラー配列。配列差し替えで更新通知。パーツ単位に `Name` / `Color` / `IsVisible` / `ShowEdges`
- `WcuViewport.MeshSource` に `ObservableCollection<ViewportMesh>` をバインド。コンター用 `ColorScale` はビューポートレベルで1つ(`ColorMapLegend` と共有可能)
- **FEM 要素→三角形化(表面抽出・高次要素分割)はアプリ責務**。要素タイプ網羅はソルバー依存のドメインロジックであり、ライブラリの境界外(コンター描画ユーティリティを外した判断と同型)
- 座標は **double で受け、内部でモデル中心へ再センタリングしてから float 化**して GPU へ(大座標値での精度ジッタ対策)

### 6.16.4 マウス操作(既定)

- **中ボタンドラッグ=回転 / Shift+中ボタン=パン / ホイール=カーソル位置へズーム / 中ボタンダブルクリック=Fit**(NX/SolidWorks/Ansys 系の主流)
- **左ボタンは将来のピッキング/選択用に予約**(Charts の操作コンフリクトの教訓)
- ホイールは Charts と違い素のホイールでズーム(3D ビューポートの共通期待。主用途はドッキングのドキュメント領域でスクロール競合なし。ギャラリーのデモページ側でのみ高さ固定で対処)
- **回転はターンテーブル、既定 Z-up**(`UpAxis` で Y-up 切替可)。トラックボールは不採用

### 6.16.5 検証

- **数学層を xUnit で厚くテスト**(自作エンジンの利点): カメラ行列(ビュー/透視/平行)、Fit 境界計算、オービット回転、再センタリング、法線計算、スカラー→テクスチャ座標変換
- **描画はギャラリー+UIA スクリーンショット横断**(既存の型): サンプルメッシュ+スカラー場で回転・ズーム・投影切替・コンター切替・両テーマを撮影して目視。`ColorMapLegend` との `ColorScale` 共有デモを含める
- WARP 前提の決定的ピクセル比較テストは作り込まない(GPU/ドライバ差で壊れやすく費用対効果が低い)

### 6.16.6 実装メモ(2026-08-01 完了)

- **依存**: Silk.NET 2.23.0(Direct3D11 / Direct3D9 / DXGI / Direct3D.Compilers)。シェーダは HLSL ソース文字列を d3dcompiler_47(Windows 標準搭載)で実行時コンパイル(vs_4_0 / ps_4_0、FL10 GPU でも動作)
- **表示経路は 2 系統**(`ViewportRenderer` が自動選択):
  - **ハードウェア**: D3D11 で MSAA 4x 描画 → 非 MSAA 共有テクスチャへ Resolve → D3D9Ex が共有ハンドルを開き、そのサーフェスを `D3DImage.SetBackBuffer`(enableSoftwareFallback=true)。描画は `D3DImage.Lock` 中に行い `AddDirtyRect` で提示
  - **ソフトウェア(WARP / D3D9 不可)**: WARP アダプタの共有テクスチャはハードウェア D3D9 から開けないため、ステージングテクスチャへ CPU 読み戻しして WriteableBitmap に転送(オンデマンド描画なので実用上十分)
- **行列規約**: System.Numerics(行優先・行ベクトル)に合わせ、HLSL 側は `row_major` + `mul(v, M)` で転置アップロード不要
- **シェーディング**: 両面ヘッドライト(`abs(dot(n, eyeDir))`)。エッジ重畳は同一頂点バッファを LineList で再利用し、ラインシェーダで NDC 深度を微小手前シフト(`z -= 0.0005 * w`)して Z ファイティング回避
- **コンター**: `ColorScale.Sample` を 256 texel サンプリングした 1D テクスチャ(ポイントサンプリング=離散レベルの境界が鈍らない)。範囲外は Below/AboveRangeColor、NaN は NaNColor(透明なら discard)、対数スケールはシェーダ内 log10。`ColorScale.PropertyChanged` でテクスチャ再構築(凡例と同一インスタンス共有が成立)
- **カメラ**(`ViewportCamera`): Target+Yaw/Pitch/Distance のターンテーブル(Pitch ±89.5° クランプ)。平行投影の高さは `2·d·tan(fov/2)` で透視と見かけサイズ一致(投影切替でモデルサイズが変わらない)。near/far は距離とシーン半径から自動。カーソル位置ズームは「注視点深度の直下点を固定する Target 平行移動」で実装し、両投影で不変性を単体テスト済み
- **軸トライアッドは WPF オーバーレイ**(Canvas に Line+TextBlock、カメラ基底ベクトルへの射影で更新)。GPU パス追加より単純で、テーマ・DPI にも自然に追従する。D3DImage が WPF 合成にネイティブ統合されるからこそ成立する構え
- **`WcuViewport`**: lookless Control(PART_Image / PART_TriadCanvas)。描画は Dispatcher でコアレスされるオンデマンド(`InvalidateViewport`)。ジオメトリ再構築(GPU バッファ)は Positions/TriangleIndices/ScalarValues の差し替え時のみ、色・表示切替は再描画のみ。デバイスロスト時はレンダラーを破棄して 1 回だけ再作成を試み、連続失敗で停止。UIA から領域特定できるよう AutomationPeer(ClassName=WcuViewport)を実装
- **注意(ギャラリーで踏んだ罠)**: `IsChecked="True"` の ToggleSwitch は XAML パース中(コンストラクタのフィールド初期化前)に Checked を発火するため、イベントハンドラには null ガードが必要
- **デモ**(`Viewport3DPage`): 円孔付き平板の一軸引張(Kirsch 厳密解の von Mises、孔縁で応力集中 3 倍)+円筒ボスの 2 パーツ。構造格子(孔側を二乗分布で細分)から直接三角形生成。投影/コンター/エッジ/離散 10 分割/パーツ表示のトグル+Fit ボタン+`ColorMapLegend` 共有
- **検証**: xUnit 29 件(カメラ 14+ジオメトリ 11+境界 4)。`.dev/scripts/verify-viewport.ps1`(UIA+実マウスイベント: 中ボタン回転→ホイールズーム→ダブルクリック Fit→平行投影→コンター/エッジ切替→離散分割→ライトテーマ切替を撮影)+ `capture-viewport-light.ps1`(ライト起動)。ハードウェア D3D11+D3DImage 経路で全項目目視確認済み

## 6.17 ビューポート第2弾 — ピッキング/選択+視点操作系(Phase 17)

(2026-08-01 の設計インタビューで決定)

### 6.17.1 ピッキング方式

- **GPU ID ピッキング**を採用: パーツ ID+三角形 ID をオフスクリーンターゲットに描画し、ピクセル読み出しで特定する
  - 見た目と完全一致(描画したものを拾うので隠面処理がタダ、ピクセル精度)
  - コストがメッシュ規模でなく解像度依存のため、BVH なしで百万三角形級にスケール(CPU レイは BVH 構築・更新が別プロジェクト級)
  - 矩形選択が「領域読み出しで出現 ID を列挙」で自然に乗る
  - 3D ヒット点が必要な場面は深度バッファ読み出し+逆射影で得る(将来のプローブ UI の土台)
- ID パスはピック要求時のみ描画(オンデマンド方針と整合)

### 6.17.2 選択の粒度(ピックモード)

- **パーツ / 面(三角形) / 節点 の3モード**(`PickMode` DP: None / Part / Face / Node)
- ライブラリは描画レベル(三角形インデックス / 節点インデックス)で返し、**三角形→FEM 要素の逆引きはアプリ責務**(三角形化がアプリ責務なので対応表もアプリが持つ。6.16.3 と同型の境界線)
- 節点ピックは「クリック位置の三角形を ID で拾い、その3頂点のうちスクリーン距離最小の節点を返す」方式(頂点用の別 ID パス不要)
- エッジ選択は見送り(線の ID パス+許容距離判定が別途必要で実需も薄い)

### 6.17.3 選択モデルとハイライト

- **`ViewportSelection` モデルをライブラリが管理し、ハイライト描画を内蔵**(`WcuViewport.Selection` に公開)
  - パーツ/三角形/節点の選択集合(メッシュ別)+変更通知。LogBuffer / ConvergenceSeries と同じ「モデルをライブラリが定義、アプリは見る/操作する」流儀
  - 選択面の強調色オーバーレイ・選択節点のポイント描画はエンジン内部(シェーダ/バッファ)でしか実現できないため内蔵が必然
  - アプリは `SelectionChanged` で FEM 実体に逆引き。プログラムからの選択(ModelTree 連動の逆方向)も同じモデル操作で可能

### 6.17.4 マウス操作

- **左クリック=置換選択 / Ctrl+左クリック=トグル / 左ドラッグ=矩形(ラバーバンド)選択(可視のみ)**
  - 矩形選択は GPU 領域読み出しで実装。「見えているものだけ」が Ansys Mechanical 等の既定と同じ。貫通選択(裏側も含む)は将来 DP 追加で非破壊拡張
  - Shift+クリック追加は Ctrl トグルと実質重複+Shift はパン修飾キーとして使用済みのため入れない
- ホバー時のプリハイライトは見送り(マウス移動ごとの ID パス+読み出しはパイプラインストールの体感悪化リスク。将来 `IsHoverHighlightEnabled` DP で非破壊追加)。ピックモード中はカーソル形状で操作中を示す

### 6.17.5 視点操作系

- **`SetStandardView(Front/Back/Left/Right/Top/Bottom/Isometric)` API** を追加(カメラ Yaw/Pitch 設定。アプリのツールバー/ショートカットから利用可)
- **クリック式 ViewCube**: 軸トライアッドと同じ WPF オーバーレイ方式(テーマ・DPI に自動適合)。面/角のクリックで視点ジャンプ、カメラ連動の回転表示まで。キューブ自体のドラッグ回転は中ボタンオービットと完全重複のため不採用
- 視点切替は瞬間ジャンプでなく短い補間アニメーション(150ms 程度)で空間把握を保つ

### 6.17.6 検証

- **xUnit**: ID エンコード/デコード、矩形領域の ID 列挙ロジック、標準視点のカメラ角度、節点最近傍判定などの数学/変換層
- **UIA**: ギャラリーのデモページに選択サマリ文字列(例「Faces: 12, Nodes: 0」)を表示し、既知座標のクリック/矩形ドラッグ→サマリ文字列をアサート。ViewCube クリック→視点変化・ハイライト表示を両テーマでスクリーンショット
- ヘッドレス WARP でレンダラを直接叩くピック統合テストは見送り(UIA 経由で同じ経路を検証できるため)

### 6.17.7 実装メモ(2026-08-01 完了)

- **ID パス**(`ViewportRenderer.RenderIdPass`): R32G32_UInt ターゲットに R=パーツID+1(0=背景)/ G=`SV_PrimitiveID`(三角形インデックス)を書き込む専用シェーダ(SM4.0 で可)。パーツ ID は共有 cbuffer の `ObjectColor.x` で渡す。ピック用ターゲット+専用深度は初回ピック時に遅延作成し、リサイズで破棄。読み出しは `CopySubresourceRegion` で領域だけを都度作成のステージングへコピーして Map(ピックはユーザー操作ペースなのでコスト無視可)。単点ピックは 1×1 領域の特殊形
- **節点ピック/矩形節点列挙**は CPU 側の純関数(`ViewportPicking`): ヒット三角形の 3 頂点(矩形はヒット三角形群のユニーク頂点)をスクリーン射影して距離/内包判定。描画と同じ viewProj を使うため画面と完全一致
- **ハイライト描画**: 選択面=選択三角形だけの索引バッファ(頂点バッファは GpuMesh を再利用)を線シェーダ(深度手前シフト+フラット色)で半透明描画(深度テストあり・書き込みなし)。パーツ選択はメッシュ全体の索引バッファをそのまま流用(追加バッファ不要)。選択節点=位置+コーナー([-0.5,0.5]²)の 6 頂点クワッドをクリップ空間でピクセル拡張し円形 discard(`ViewportInfo` cbuffer 追加: ビューポートサイズ+ポイント径)。色はアクセントトークン(面 α0.55 / 節点 α1.0)
- **`ViewportSelection`**: メッシュ別 HashSet(パーツ/面/節点)+ `Changed` 1 回/操作。`BeginUpdate/EndUpdate` で置換選択(Clear+Add)を 1 通知に集約。`PruneTo` で MeshSource から消えたパーツの選択を自動掃除(ジオメトリ再構築時に呼ばれ、範囲外インデックスはバッファ構築時に黙って捨てる)
- **カメラ**: `SetOrientation(yaw, pitch)` は真上/真下 ±90° まで許可(プロパティ/Orbit は従来どおり ±89.5° クランプ)し、ビュー行列側で up が視線と平行になる特異点を水平軸フォールバックで処理(Z-up 真上=+Y が画面上、CAD 慣例)。`GetStandardViewAngles(view, upAxis)` は静的で単体テスト可能。Z-up: Front=-Y / Right=+X、Y-up: Front=+Z(DCC 慣例)
- **視点アニメーション**: `WcuViewport.SetStandardView` が CompositionTarget.Rendering で 150ms の ease-out cubic 補間(Yaw は [-π,π] 正規化で最短経路)。Unloaded で確実に解除
- **ViewCube**(`ViewCubeOverlay`): 6 面 Polygon+ラベル+8 角 Ellipse をカメラ基底ベクトルへの正射影で毎フレーム更新(表向き面のみ表示、facing で不透明度)。ブラシは `SetResourceReference` でトークン追従、ホバーはアクセントに差し替え。面クリック=標準視点 / 角クリック=八分円方向。子要素が e.Handled=true にするためピック操作と干渉しない
- **左ボタン操作**: down で位置記録+キャプチャ → 4dip 超の移動でラバーバンド(トライアッドと同じ Canvas 上の Rectangle、アクセント破線)→ up で単点 or 領域ピック。Ctrl=トグル(矩形は追加)。空クリック(背景)は置換選択の Clear として機能。トライアッドの Canvas 表示切替は要素単位に変更(ラバーバンドと共存のため)
- **検証結果**: xUnit 74 件(既存 29+選択モデル 10+ピッキング数学 8+標準視点/ViewCube 対応 27)。`.dev/scripts/verify-viewport-picking.ps1`(UIA+実マウス: 面クリック=1→Ctrl 追加=2→Ctrl 再クリック=1→矩形=865→節点クリック/矩形=427→パーツ=1→空クリック解除→解除ボタン→正面/上/等角ボタン→ViewCube TOP クリック→ライトテーマ、サマリ文字列を全アサート+スクショ 8 枚)。ハードウェア D3D11 経路で全項目確認済み

## 6.18 ビューポート第3弾 — 変形表示+アニメーション(Phase 18)

(2026-08-01 の設計インタビューで決定)

### 6.18.1 テーマ選定

- **変形表示+アニメーション**を採用。モード形状・過渡応答の確認は CAE ポスト処理の最頻出機能で、コンター(Phase 16)・選択(Phase 17)に続く自然な柱
- Helix を捨てて自作エンジンにした決定的動機のひとつ「毎フレーム頂点更新を大規模メッシュで効率よく」の価値を初めて回収するフェーズ
- 断面カット/プローブ/注釈は変形と独立に後から積めるため次フェーズ以降へ。小粒改善(ホバープリハイライト/貫通選択/エッジ選択)も引き続きバックログ

### 6.18.2 変形の適用方式 — GPU 頂点シェーダ

- **変位を頂点属性に追加**(position + normal + scalar + displacement = 40B ストライド)し、シェーダ内で `pos + disp × scale` を計算(scale は cbuffer)
  - 変形スケール変更・モード振動アニメ(scale を sin で振る)は**頂点更新ゼロ** — 百万節点でも描画するだけ
  - フレーム切替(過渡応答)は**変位属性だけを Dynamic バッファ部分更新**(バッファ再作成不要)
  - ID ピッキング/ハイライト/エッジの各シェーダにも同じ変位を適用し、変形状態のままのピックが画面と正確に一致
- **法線は非変形のまま**(変形後の法線再計算は GPU では困難。実用スケールの変形表示では一般的な簡略化。フレーム差替時の CPU 再計算オプションは将来拡張)

### 6.18.3 データ API と再生の責務分担

- 変位は **`ViewportMesh.Displacements`**(長さ 3N の double 配列、x0,y0,z0,...)。ScalarValues と同じ「配列インスタンス差し替えで更新検知」の流儀
- ビューポートに **`DeformationScale` DP**
- **モード振動アニメはライブラリ内蔵**(`IsDeformationAnimated` DP + `DeformationAnimationPeriod` DP。scale を ±1 の正弦で振る)。描画レートと密結合で、GPU 方式なら頂点更新ゼロなので内蔵が必然
- **フレーム列(過渡応答)の再生はアプリ責務**: PlaybackBar(Phase 11)のイベントで Displacements/ScalarValues を差し替えるだけ。データの時間割当・ロードはソルバ固有なのでライブラリが抱えない(LogBuffer/ConvergenceSeries と同じ境界線)。フレーム切替が部分更新で効率的に動くことはライブラリ側が保証
- フレーム列モデルの内蔵は不採用(全フレームをメモリに抱える前提になり大規模過渡解析で破綻)

### 6.18.4 付随機能

- **非変形形状の重畳**(`ShowUndeformedWireframe` DP): 変形前形状をワイヤフレームで薄く重ねる。GPU 方式なら「同じエッジパスを scale=0 でもう一度描く」だけで追加バッファ不要
- **自動スケール推奨値**(`GetSuggestedDeformationScale()`): 「最大変位がモデル代表寸法の約 5% になるスケール」を返す計算ヘルパ(Ansys の Auto Scale 相当)。適用はアプリの判断(DP には自動適用しない)。純粋関数で単体テスト容易
- 変位大きさのコンター自動生成は入れない(スカラーは ScalarValues にアプリが計算して渡せばよく、物理量の意味づけに踏み込まない境界線を維持)

### 6.18.5 デモ

- **片持ち平板の曲げモード形状**(Euler-Bernoulli の解析解。Kirsch 解と同じ「厳密解でデモデータを作る」流儀)
  - 1〜3 次モード切替(ComboBox → Displacements 差し替え=部分更新経路のデモ)
  - 変形スケール Slider+自動スケール適用ボタン / モード振動トグル / 非変形ワイヤフレームトグル
  - **PlaybackBar 連携**: 減衰自由振動(モード重ね合わせ×減衰)のフレーム列を事前生成し、フレーム変更で Displacements/ScalarValues を差し替える「アプリ責務の過渡再生」の参照実装
- 既存の円孔平板デモは残す(同一ページ内セクション追加か別ページ化はレイアウトを見て実装時に判断)

### 6.18.6 検証

- **xUnit**: `GetSuggestedDeformationScale` の計算(最大変位・代表寸法・ゼロ変位の境界)、モード振動の時間→スケール係数(純粋関数化して波形を検証)、Displacements 配列の検証(長さ不正の扱い)などの数学/変換層
- **UIA**: 文字列アサートができないため**ビューポート領域のピクセル差分を数値アサート**: スケール 0→適用で差分あり / 振動アニメ中の 2 時点で差分あり / 停止中は差分なし。加えてモード切替・非変形重畳・PlaybackBar 再生・ライトテーマのスクリーンショット目視(従来と同じ流儀)

### 6.18.7 実装メモ(2026-08-01 完了)

- **変位バッファの持ち方は設計から微修正**: 40B インターリーブではなく、既存の 28B 頂点バッファ(slot 0)+**独立した Dynamic 変位バッファ(slot 1、12B/頂点、`TEXCOORD1`)**の 2 スロット構成にした。フレーム切替時に `Map/WriteDiscard` で変位だけを差し替えられ、Immutable のジオメトリ本体に一切触れない(部分更新の意図は設計どおり、むしろ徹底)
- cbuffer に `DeformParams`(x=実効スケール)を追加(192B)。Mesh / Line / Pick / Point の全シェーダが `pos + disp × DeformParams.x` を適用
- `WcuViewport.OnMeshPropertyChanged` で `Displacements` 差し替えを軽量経路に分岐(`_displacementDirtyMeshes` → `EnsureDisplacements()` が変位バッファのみ更新)。`Positions` / `ScalarValues` / `TriangleIndices` は従来どおり全再構築
- 振動アニメは `CompositionTarget.Rendering` 購読で毎フレーム `InvalidateViewport`(オンデマンド描画の明示的例外)。実効スケール = `DeformationScale × sin(2πt/T)` は `ViewportDeformation.GetAnimationFactor`(純粋関数)で計算
- ピック(GPU ID パス+CPU 節点数学)は**直前に描画した実効スケール**(`_lastEffectiveDeformationScale`)を使い、画面とヒット判定が常に一致
- 選択節点ポイントのクワッド頂点(32B)にも変位属性を埋め込み、変形表示中も選択ハイライトが追従。Displacements 差し替え時は選択バッファも再構築
- 非変形ワイヤフレームは全可視メッシュのエッジバッファを `DeformParams.x=0`+エッジ色 α0.35 の半透明・深度書き込みなしで重畳描画(追加バッファなし)
- 長さ不正の Displacements はゼロ埋め扱い(`ViewportDeformation.ToDisplacementArray`)。例外は投げない
- デモは別ページ **「3D Deformation」**(`ViewportDeformationPage`)として追加(既存の円孔平板ページはそのまま)。βL = 1.8751/4.6941/7.8548 の 1〜3 次曲げ+減衰自由振動 90 フレーム
- 検証: xUnit 92 件(+14)全パス / `verify-viewport-deformation.ps1` のピクセル差分アサート 7 件全 PASS(自動スケール ×5.4 = 0.05×√(100²+40²)/1 と一致)/ 既存 `verify-viewport.ps1`・`verify-viewport-picking.ps1` 回帰なし / 両テーマ目視 OK

## 6.19 ビューポート第4弾 — 断面カット(Phase 19)

(2026-08-01 の設計インタビューで決定)

### 6.19.1 テーマ選定

- **断面カット(クリッピング平面)**を採用。コンター(16)→選択(17)→変形(18)に続く CAE ポスト処理の主要機能の最後の柱で、ソリッド内部の視認手段が現状ゼロという実用上最大の穴を埋める
- Post-processing フェーズで「断面定義 UI はビューポートと密結合のため見送り」とした宿題の回収。周辺部品が `WpfCustomUI.Viewport3D` に合流するハイブリッド方針の設計意図どおり
- プローブ/注釈は UI 寄りの設計論点(値補間・ラベル管理・リーダーライン)が多く一段重いため第5弾以降へ。グリフ・小粒改善(ホバープリハイライト/貫通選択/エッジ選択/変形後法線)も引き続きバックログ

### 6.19.2 クリップの描画方式 — GPU クリップのみ(切り口は開放)

- 頂点シェーダで平面の符号付き距離を計算し **`SV_ClipDistance`** に流す。頂点更新ゼロで、変形(DeformParams)適用後の位置に対してクリップする
- **全パス(メッシュ/エッジ/ピック/ハイライト/ポイント)に同じクリップを適用**: クリップされた部分はピックにも掛からず、画面とヒット判定の一致を維持(Phase 17/18 と同じ原則)
- **キャップ(切り口の塗りつぶし)は作らない**: ViewportMesh は表面三角形メッシュであり、切り口の中身(断面上のコンター値)は体積データがないと原理的に計算できない。ステンシルキャップ(閉多様体前提)も CPU 交差計算も工数対効果が悪い
- **断面上の値表示はアプリ責務**: アプリが断面スライスを計算して `ViewportMesh` として追加する(「FEM→三角形化はアプリ責務」と同型の境界線)。共存のため `IsClippable` フラグを用意(6.19.3)

### 6.19.3 API — 単一 SectionPlane DP + IsClippable

- **`WcuViewport.SectionPlane` DP**(null で無効=既定)。`SectionPlane` は通過点+法線(`OriginX/Y/Z`, `NormalX/Y/Z`、double)の変更通知付きモデル。座標は再センタリング前のモデル座標で受け、内部でシーン原点補正してシェーダ係数化(大座標対策の流儀を踏襲)
- シェーダは cbuffer に平面係数 float4 を 1 本追加。法線側(正の半空間)を残す。法線反転=どちら側を残すかの切替
- **`ViewportMesh.IsClippable`**(既定 true): アプリが追加する断面スライスや治具パーツをクリップ対象から除外する
- 複数平面(`SectionPlanes` コレクション、最大 4 枚=SV_ClipDistance の 1 レジスタ)やスラブ/ボックスは実需が出たら非破壊追加。実務の 9 割強は 1 枚で足りる

### 6.19.4 断面定義 UI — 可視化のみ内蔵、操作はアプリ側

- **`WcuViewport.ShowSectionPlaneIndicator` DP**(既定 true): シーン境界サイズの半透明クワッド+輪郭線を GPU で描画(クリップ対象外)。平面がどこで切っているかの視認はライブラリの責務
- 平面の操作(法線プリセット X/Y/Z、オフセット Slider、反転)は既存コントロールの組み合わせで足りるため専用化しない(「応力成分セレクタ等は既存コントロールで足りる」と同じ境界線)。ギャラリーに参照実装を置く
- ビューポート内ドラッグギズモは見送り(拘束付き 3D ドラッグの設計一式が必要で工数大。将来足しても非破壊)

### 6.19.5 デモ

- **新ページ「3D Section」**: 厚肉円筒(外面・内面・両端面キャップの閉じた表面メッシュ)+内圧の Lamé 厳密解コンター。肉厚内部に応力勾配がある(内面側ほど高応力)ため断面で切る意味が視覚的に明確。「厳密解でデモデータを作る」流儀を踏襲
- 操作参照実装: 法線プリセットボタン(X/Y/Z)+オフセット Slider+法線反転トグル+インジケータ表示トグル
- **アプリ責務の断面コンターの参照実装**: 断面位置の半リング帯スライスメッシュを計算し `IsClippable = false` で追加、オフセット Slider に追従して再計算(6.19.2 の境界線を動くコードで示す)

### 6.19.6 検証

- **xUnit**: 平面の符号付き距離、モデル座標→再センタリング後のシェーダ係数変換(大座標でも破綻しないこと)、法線の正規化・ゼロ法線の扱い、`SectionPlane` 変更通知
- **UIA ピクセル差分**(Phase 18 の PixelDiff 基盤を再利用): クリップ ON/OFF / オフセット移動 / 法線反転 / インジケータ ON/OFF でそれぞれ差分をアサート
- **ピック整合の数値アサート**: 同じ矩形範囲の面選択をクリップ前後で実行し、選択サマリの Faces 数が減ること(=クリップされた面はピック不可)を検証。断面×選択の統合点で最も壊れやすい箇所
- 両テーマのスクリーンショット目視(従来どおり)

### 6.19.7 実装メモ(2026-08-01 完了)

- cbuffer に `ClipPlane`(xyz=正規化法線・w=定数項、208B に拡張)を追加。Mesh / Line / Pick / Point の全シェーダが変形適用後の位置で符号付き距離を計算し `SV_ClipDistance0` へ出力。無効時は (0,0,0,1)(距離が常に +1)で分岐レス
- 係数変換は **`ViewportSection.ComputeClipCoefficients`**(純粋関数): 法線正規化+通過点のシーン原点補正を double で行ってから float 化(大座標対策の流儀)。ゼロ法線は null=クリップ無効
- `IsClippable` は `GpuMesh` に毎フレーム同期(Color/ShowEdges と同じ視覚オプション経路、ジオメトリ再構築なし)。パスごと・メッシュごとに cbuffer の ClipPlane を切り替える
- **ピック(GPU ID パス)にも同じ平面を適用**し、クリップで隠れた面は選択不可(検証: 同一矩形選択で Faces 836 → 2580)。CPU 節点数学は「可視三角形の節点」ベースなので追加のクリップ判定は不要
- **平面インジケータ**は `ViewportSection.BuildIndicatorVertices`(純粋関数、14 頂点=クワッド 6+輪郭ライン 8)を毎フレーム生成し、専用 Dynamic バッファへ Map/WriteDiscard。ライン系シェーダの 2 スロットレイアウトを「同一バッファを slot 0(offset 0)/slot 1(offset 12)に 24B ストライドで二重バインド」して満たし、専用シェーダなしで済ませた。アクセント色(塗り α0.10 / 輪郭 α0.70)、深度テストあり・書き込みなし
- `SectionPlane` は INPC モデルで、`WcuViewport` が PropertyChanged を購読(ColorScale と同じ流儀)。オフセット Slider のドラッグに自動追従
- デモ「3D Section」: 厚肉円筒(a=30/b=50/L=120、内圧 100 MPa)の Lamé 閉端解 von Mises コンター。断面スライスは平面と円筒の交差領域をアプリ側で厳密に計算(軸直交=アニュラス、軸平行=肉厚を貫く帯 1〜2 本)し `IsClippable=false` で追加する参照実装。**既定の法線は −軸方向**(既定の等角視点=+X+Y 象限から切り口が見えるように)
- 設計からの微修正: スライス形状は「半リング帯」でなく平面との交差領域そのもの(軸直交カットはアニュラス、軸平行カットは帯)。断面上のコンターとして正確で、参照実装としても分かりやすい
- 検証: xUnit 103 件(+11)全パス / `verify-viewport-section.ps1` のピクセル差分 6 件+ピック整合 1 件全 PASS(クリップ再有効化の決定性 diff=0 含む)/ 既存 `verify-viewport-picking.ps1`・`verify-viewport-deformation.ps1` 回帰なし / スモークテスト成功 / 両テーマ目視 OK

## 6.20 ビューポート第5弾 — プローブ+注釈(Phase 20)

(2026-08-01 の設計インタビューで決定)

### 6.20.1 テーマ選定

- **プローブ(クリックで値ラベルを立てる)+注釈**を採用。Phase 19 設計時に「第5弾以降へ」と明記した宿題の回収で、コンター(16)→選択(17)→変形(18)→断面(19)と揃った今、CAE ポスト処理の実用サイクルで残る最後の主要機能
- 基盤は整備済み: GPU ID ピック(17)で三角形ヒットが取れ、節点スカラー(16)から値補間ができる。「3D ヒット点が必要な場面(将来のプローブ UI の土台)」の設計意図どおり
- グリフ表示・小粒改善(ホバープリハイライト/貫通選択/エッジ選択/複数断面/ギズモ)は引き続きバックログ

### 6.20.2 責務分担 — ハイブリッド(ヒット計算+既定ラベルは内蔵、書式は差し替え可)

- **`PickMode.Probe` を新設**。クリック → GPU ID ピック(メッシュ+三角形)→ CPU でレイ→三角形交差(変形適用後の頂点で計算)→ 重心座標でヒット 3D 点と**補間スカラー値**を求める
- ヒット情報は **`ProbeResult`**(メッシュ / 三角形 / 最近傍節点 / モデル座標 3D 点 / 補間スカラー / 節点スカラー)として **`ProbePicked` イベント**で通知
- **既定動作はライブラリが完結**: イベント未処理なら既定書式のラベル文字列を自動生成して注釈を追加。書式は **`ProbeLabelFormatter`**(`Func<ProbeResult, string>`)の差し替えで単位付き表示等に変更可能(物理量の意味づけ=アプリ責務、値補間の数学=ライブラリ責務の境界線)
- 空クリック(背景)は何もしない。注釈の削除/全削除は `Annotations` コレクション操作(アプリ責務、ギャラリーに参照実装)

### 6.20.3 注釈モデル — 節点バインド主体

- **`WcuViewport.Annotations`**(ObservableCollection<`ViewportAnnotation`>)。ライブラリがオーバーレイ描画を内蔵(選択ハイライト内蔵と同じ境界線)
- アンカーは **(Mesh, NodeIndex) の節点バインドを主体**とし、変形表示(DeformationScale×Displacements、振動アニメ、過渡再生)に毎フレーム追従する(選択ハイライトが変形に追従するのと同じ仕組み)。プローブは最近傍節点にスナップして注釈化(CAE 的にも「節点の値」が自然)
- **自由 3D 点アンカーも併用可**(モデル座標固定。寸法線的な用途、変形追従なし)
- `Text` は自由文字列。`Tag` にアプリ任意データ(ProbeResult 等)を保持可能

### 6.20.4 ラベル描画 — WPF オーバーレイ(シンプル方針)

- 軸トライアッド / ViewCube と同じ **WPF Canvas オーバーレイ**方式: チップ(角丸ボーダー+トークン配色)+アンカーへのリーダーライン。テーマ追従はトークン参照で自動
- ラベルは画面固定オフセット(右上方向)に配置。**オクルージョン判定なし**(常に手前表示。毎フレームの深度読み出しは重く、Ansys 等も既定は常時表示)。ドラッグ移動なし(将来 DP 追加で非破壊拡張)
- **非表示メッシュ・断面クリップされた節点・画面外の注釈は自動で隠す**(クリップは SectionPlane の符号付き距離を CPU でも評価)
- カメラ操作・変形・リサイズに毎フレーム追従(スクリーン射影は `ViewportPicking.ProjectToPixel` を再利用)

### 6.20.5 デモ

- **新ページ「3D Probe」**: Kirsch 円孔板(一様引張の厳密解コンター)。孔縁をプローブして応力集中係数 3.0 を「発見」する CAE らしいストーリー
- プローブモードトグル / 注釈一覧(削除・全削除)/ `ProbeLabelFormatter` で単位付き書式(σ = xxx MPa)の参照実装
- 振動アニメーション(面外モードを模した変位)を重ね、**注釈が変形に追従する**デモを含める

### 6.20.6 検証

- **xUnit**: レイ→三角形交差(変形適用込み)、重心座標補間、アンカーのスクリーン射影、クリップ済み節点の非表示判定、`ViewportAnnotation` 変更通知(純粋関数化して検証)
- **UIA**: 注釈チップは WPF 要素なので**ラベル文字列そのものを UIA で直接アサート**(ピクセル差分より強い検証)。孔縁クリックの値が Kirsch 厳密解の許容幅内であることも検査。従来のピクセル差分・両テーマスクリーンショット目視も併用

### 6.20.7 実装メモ(2026-08-01 完了)

- `ViewportProbing`(internal 純粋関数): `ComputePickRay`(viewProj 逆行列で NDC 近/遠平面を逆射影)、`TryIntersectTriangle`(Möller–Trumbore 両面、縁は重心座標 ±0.02 許容→クランプ正規化)、`Probe`(統合)。交差は**変形適用後**の頂点(`ViewportPicking.GetLocalPosition` を internal 化して共用)で行い、ヒット点のモデル座標は同じ重心座標で**変形前**の節点座標を補間して返す。サブピクセルずれでレイが外れた場合はスクリーン最近傍節点スナップにフォールバック
- `ProbeResult`(record)+`ProbePickedEventArgs.Handled`(true で自動追加を抑止)。既定ラベルは `FormatDefaultProbeLabel`(InvariantCulture、G4)。`NodeIndex` は重心座標最大の頂点
- `ViewportAnnotation`: INPC、節点バインド(Mesh+NodeIndex)/自由 3D 点(X/Y/Z)の 2 アンカー。`ToString()` が `Text` を返すため ListBox 直バインドで表示も UIA Name も注釈内容になる
- オーバーレイは新テンプレートパーツ `PART_AnnotationCanvas`(TriadCanvas の上、ViewCubeCanvas の下、IsHitTestVisible=False)。チップ=Border(角丸+`Wcu.Brush.Surface.Elevated`/`Accent.Default`、SetResourceReference でテーマ追従)+リーダーライン+アンカードット。毎フレーム `RenderFrame` 末尾で射影更新し、非表示メッシュ(可視リスト外)・クリップ節点(`ViewportSection.IsClipped` = GPU と同式)・画面外・カメラ背後を自動非表示
- プローブモードは矩形選択を無効化(クリックのみ)。ピックは既存 GPU ID パス(変形+クリップ適用済み)を共用するため、表示と完全に一致
- ギャラリー「3D Probe」: Kirsch 円孔板(von Mises、24×96 リング格子、孔側二乗詰め)+上面視点開始+単位付きフォーマッター+注釈一覧(削除/全削除)+面外たわみ変位の振動アニメで変形追従デモ
- 検証: xUnit 122 件(+19)全パス / `verify-viewport-probe.ps1` で UIA ラベル文字列を直接アサート — 遠方場 83.7 MPa(理論 ≈84)・孔縁 232.4 MPa(r≈1.1a の理論 ≈233)・異方性最小値 48.9 MPa が全て Kirsch 厳密解の許容幅内、空クリック/モード OFF/削除/全削除/振動中のチップ追従(停止後 diff=0 の決定性込み)も PASS / 既存 UIA スクリプト 3 本回帰なし / スモークテスト成功 / 両テーマ目視 OK

## 6.21 ビューポート第6弾 — ベクトルグリフ(Phase 21)

(2026-08-01 の設計インタビューで決定)

### 6.21.1 テーマ選定

- **ベクトルグリフ表示(節点ベクトル場→矢印描画)**を採用。コンター(16)→選択(17)→変形(18)→断面(19)→プローブ(20)と揃い、CAE ポスト処理の定番表示で残る最後の大物。Phase 16 から一貫してバックログに残っていた宿題の回収
- 変位ベクトル・反力・主応力方向など、スカラーコンターでは表現できない「向きを持つ量」の可視化を担う
- 小粒改善(ホバープリハイライト/貫通選択/エッジ選択/複数断面/ギズモ/変形後法線/注釈ドラッグ)は引き続きバックログ

### 6.21.2 データ API — ViewportMesh.VectorValues(確立済みパターンに追随)

- **`ViewportMesh.VectorValues`**(節点毎 3 成分の単一 double 配列)。`ScalarValues`/`Displacements` と同型で、フィールド切替(変位/反力/主応力)は**アプリが配列を差し替える**(Phase 18 の過渡再生と同じ責務分担)
- 変位ベクトルを表示する場合は `Displacements` と同じ配列を代入すればよい
- 表示制御はビューポート側 DP: `ShowGlyphs` / `GlyphScale` / `GlyphStride` / `GlyphColorScale`

### 6.21.3 描画 — GPU インスタンシング(低ポリ 3D 矢印)

- 矢印 1 本分の低ポリジオメトリ(シャフト円柱+コーンヘッド、数十三角形)を **`DrawIndexedInstanced`** で全対象節点分インスタンス描画。WARP でも動作し、既存の自作 HLSL パイプラインに自然に追加できる
- インスタンスデータ = (基点座標, 変位, ベクトル)。頂点シェーダでベクトル方向への回転基底・長さスケールを適用し、基点は `基点 + 変位 × DeformationScale`(cbuffer)で**変形表示に追従**(フレーム毎のバッファ再構築なし)
- ライティングは既存メッシュと同じ方式。断面カット(`SV_ClipDistance`)も同じ平面でクリップし、クリップされた節点の矢印は消える(`IsClippable` 準拠)
- ゼロ/微小ベクトルの節点はスキップ(縮退回転を避ける)

### 6.21.4 スケールと密度

- **`GlyphScale` DP**: 矢印長さ = |v| × GlyphScale。**`GetSuggestedGlyphScale()`** ヘルパ(最大ベクトル長がモデル代表寸法の数%に見える推奨値、`GetSuggestedDeformationScale` と同型)を提供
- **`GlyphStride` DP**(既定 1 = 全節点): n 節点ごとに 1 本の間引きで大規模メッシュの団子化を回避。空間均等サンプリングは将来拡張

### 6.21.5 色付け — 専用 GlyphColorScale

- **`GlyphColorScale`**(ColorScale 型、null 許容)で |v| → カラーマップ(CAE 慣例)。コンター用 `ColorScale` とは独立(表示中の物理量・値域が異なるため)
- 既存の ColorScale / LUT 基盤を再利用し、`ColorMapLegend` に同じインスタンスを渡せば凡例も表示できる
- null のときは単色(アクセント系)フォールバック

### 6.21.6 デモ

- **新ページ「3D Glyphs」**: 内圧を受ける厚肉円筒(Phase 19 と同じ Lamé 厳密解)の**変位ベクトル場** u(r) = Ar + B/r。全て半径方向を向くため正しさが一目で分かる(向き=放射状、長さ=内面側で最大)
- スケール/ストライドのスライダー+グリフ専用凡例+変形追従(`Displacements` に同配列)+断面カット併用(クリップ節点の矢印が消える)のフェーズ横断統合デモ

### 6.21.7 検証

- **xUnit**: ベクトル→矢印回転基底の構築、ゼロ/微小ベクトルのスキップ判定、ストライド間引きの列挙、`GetSuggestedGlyphScale`、|v|→色(純粋関数化して検証)
- **UIA**: ピクセル差分(グリフ ON/OFF・スケール変更・ストライド変更・断面クリップ併用で矢印が消える・OFF→ON の決定性 diff=0)+ 両テーマスクリーンショット目視。インスタンシング経路は WARP フォールバック込みで UIA 実行時に検証される

### 6.21.8 実装メモ(2026-08-01 完了)

- **ViewportGlyph**(internal 静的クラス): 純粋関数群。`BuildArrowGeometry`(単位矢印 +Z 方向、シャフト円柱+底面キャップ+コーン基部ディスク+コーン側面、12 セグメント既定)/ `ComputeBasis`(単位方向→正規直交基底、HLSL と同式で u×v=w の右手系)/ `BuildInstances`(節点→インスタンス float 列、ストライド間引き・ゼロ/NaN ベクトルスキップ・再センタリング・変位同梱・|v|→色)
- **インスタンスデータ = 14 float/件**(基点 3+変位 3+単位方向 3+|v| 1+RGBA 4、56B)。矢印の実長は |v| × GlyphScale を**シェーダ側**(cbuffer `DeformParams.y`)で掛けるため、スケール変更ではバッファを再構築しない。色は `ColorScale.GetColor()`(対数・範囲外・離散レベルはコンター凡例と同じ扱い)を CPU で焼き込む
- **HLSL Glyph シェーダ**: VS で方向ベクトルから回転基底を構築し、`基点 + 変位 × DeformParams.x + R·(頂点 × 実長)`。法線は回転のみ(一様スケール)でメッシュと同じ両面ヘッドライト。クリップは**アンカー節点位置**で `SV_ClipDistance` 判定し矢印ごと消す(部分カットなし、注釈の自動非表示と同じ思想)
- **GpuMesh**: `UpdateGlyphInstances`(Dynamic バッファ、容量内なら Map/WriteDiscard、不足時のみ再作成)。レンダラーはエッジ重畳の後(パス 2.2)に深度書き込みありで `DrawIndexedInstanced`(矢印プロトタイプは Immutable で全パーツ共有)
- **WcuViewport**: `ShowGlyphs`(既定 true)/ `GlyphScale`(既定 1.0、0 以上)/ `GlyphStride`(既定 1)/ `GlyphColorScale`(PropertyChanged 購読で自動追従)DP + `GetSuggestedGlyphScale()`。`_glyphDirty` フラグで VectorValues/Displacements 差し替え・ストライド・カラースケール・テーマ変更(単色フォールバックのアクセント色が焼き込みのため)・ジオメトリ再構築時にインスタンスを作り直す
- **ギャラリー「3D Glyphs」**: 厚肉円筒(a=30/b=50mm、p=100MPa、E=200GPa、ν=0.3)の Lamé 変位場 u(r) = (pa²/E(b²−a²))·((1−ν)r + (1+ν)b²/r)。内面 0.0364mm(赤)→外面 0.0281mm(青)の放射状矢印。スケールスライダー(推奨値×0.2〜3.0)/ストライド 1〜16/断面カット併用/振動アニメ変形追従/グリフ専用 ColorMapLegend の参照実装。メッシュは単色+ShowEdges=false で矢印を主役にする
- **検証**: xUnit 22 件追加(計 138)+ `.dev/scripts/verify-viewport-glyphs.ps1`(ON/OFF diff・決定性 diff=0・スケール・ストライド・クリップ併用・振動アニメ追従・両テーマスクショ)全パス。既存 verify-viewport-section.ps1 の回帰も確認
- **バックログ**: 空間均等サンプリング間引き / 矢印の画面固定サイズモード / テンソルグリフ(主応力の 3 軸表示)

## 6.22 ビューポート第7弾 — 大規模メッシュ性能(Phase 22)

(2026-08-01 の設計インタビューで決定)

### 6.22.1 テーマ選定と性能目標

- **大規模メッシュ性能**を採用。実用レベルの CAE では数千万〜数億要素のモデルを扱う必要があり、表示系の大物(コンター/選択/変形/断面/プローブ/グリフ)が出揃った今、スケール対応がライブラリ実用化の要
- **目標: 表面三角形 5,000万・節点 2,500万**をハードウェア GPU で「ロード数秒台・操作 30fps」。本ライブラリは表面三角形を描画する設計(FEM→表面抽出はアプリ責務、spec 6.16.3)のため、**数億要素のソリッドモデルも表面抽出後はこのレンジに収まる**
- 数億三角形の**直接描画**は LOD・メッシュ削減・アウトオブコアが必須の別物の仕事のため、将来フェーズへ(本フェーズの計測基盤がその前提になる)

### 6.22.2 アーキテクチャ変更(4 項目一括、全機能の動作維持)

1. **チャンク分割**: 頂点/インデックスバッファを一定サイズごとに分割して複数ドロー。D3D11 の単一リソース上限(保証 128MB、実質 1〜2GB)対策で、5,000万頂点 × 28B ≈ 1.4GB の単一バッファはロード自体が環境依存で失敗する。**GPU ID ピッキングの三角形 ID(SV_PrimitiveID)はドロー毎にリセットされる**ため、チャンク基点オフセットを cbuffer で加算してピック/矩形選択/プローブの整合を維持する
2. **並列ジオメトリ構築**: 法線計算・インターリーブ・エッジ抽出を Parallel.For 化+中間コピー削減。現状シングルスレッドでは 5,000万三角形のロードに数十秒かかる
3. **エッジ抽出の閾値制御**(6.22.4)
4. **統計 API**(6.22.5)

- 機能を削って速くするのではなく、**ピック/選択/変形/断面/グリフの全機能が大規模でも動く**ことを維持する

### 6.22.3 API 方針

- **double API は維持**(非破壊。大座標値の精度対策として再センタリング後に float 化する Phase 16 の設計根拠を踏襲)
- フルサイズの中間配列(インターリーブ用 float・法線)を**チャンク単位の構築**に変えてピークメモリを削減
- 構築は**同期のまま**(並列化で数秒台に短縮)。非同期構築(構築中の旧シーン表示+進捗イベント)は状態管理一式が必要なためバックログ

### 6.22.4 エッジ抽出の閾値

- **`WcuViewport.EdgeExtractionLimit` DP**(三角形数、既定 500万)。超過メッシュはエッジ抽出自体をスキップ(ShowEdges/非変形ワイヤフレーム重畳は無効になる)。int.MaxValue で実質無制限
- エッジは三角形数の約 1.5 倍のライン(5,000万三角形 → 約 1.8GB のインデックス)を生むため、安全網として必須。アプリの明示制御(ViewportMesh.ShowEdges)は従来どおり

### 6.22.5 統計 API

- **`GetStatistics()` → `ViewportStatistics` レコード**(三角形数/節点数/チャンク数/エッジスキップ有無/直近のジオメトリ構築時間/直近の描画時間)
- スナップショット取得メソッド方式(頻繁に変わる値を DP にするとバインディング更新が描画毎に走るため)。用途: アプリのステータスバー表示・ベンチ計測・性能回帰の検証

### 6.22.6 デモ — 新ページ「3D Benchmark」

- 合成波面メッシュ(パラメトリック生成)を **10万〜5,000万三角形の ComboBox 選択**(既定 100万。5,000万は CPU 約 1.2GB+GPU 約 1.4GB を消費するため明示選択制)
- コンター表示+**構築時間/描画時間/FPS 表示**(統計 API の参照実装)+回転アニメによる連続描画 FPS 計測+ピック動作確認

### 6.22.7 検証

- **xUnit**: チャンク分割数学(境界・三角形→チャンク割当・ピック ID オフセット逆引き)、並列法線計算の逐次版一致、エッジ閾値スキップ判定、ViewportStatistics
- **UIA**: 複数チャンクになる規模で構築 → 統計文字列(三角形数・チャンク数 ≥ 2)とピック整合をアサート+両テーマスクショ。フレーム時間の絶対値は環境依存のためアサートしない
- **Release 手動ベンチ**: 100万/1,000万/2,500万/5,000万の構築時間・FPS を計測して spec に記録

### 6.22.8 実装メモ(2026-08-01 完了)

- **ViewportChunking**(internal 静的クラス): 純粋関数群。`ComputeChunkBoundaries`(頂点上限 400万・三角形上限 800万/チャンク、ユニーク頂点数をエポック式マークで数えながら連続分割)/ `BuildChunkData`(チャンク内のグローバル頂点→ローカル添字の再マップ。`ChunkVertexRemap` はエポック方式でクリア不要、チャンク間で再利用)
- **GpuMesh のチャンク化**: `GpuMeshChunk`(頂点/変位/三角形インデックス/エッジインデックスの各バッファ+`GlobalVertices` 逆引き+`TriangleBase` ピック基点)のリストを保持。インターリーブ頂点・変位のギャザーはチャンク単位で並列化し、フルサイズ中間配列を排除。グリフインスタンスも 200万件/バッファで分割
- **ピック ID オフセット**: HLSL cbuffer に `PickParams`(x=チャンク三角形基点)を追加し、ID パスで `SV_PrimitiveID + PickParams.x` を書く。ピック/矩形選択/プローブがチャンク跨ぎでも一貫。選択面ハイライトは元メッシュのインデックス参照をやめ、位置+変位を直接持つ独立頂点バッファに変更
- **並列構築**: `ToLocalPositions`(再センタリング+float 化)と法線の正規化を `Parallel.For` 化(65,536 頂点以上で発動)。法線の面加算は競合回避のため逐次のまま(実測ボトルネックはギャザー側)
- **`EdgeExtractionLimit` DP**(既定 500万三角形): 超過メッシュはエッジ抽出をスキップし `ViewportStatistics.EdgeSkippedMeshCount` に現れる
- **`GetStatistics()`**: `ViewportStatistics` レコード(三角形/節点/メッシュ/チャンク数、エッジスキップ数、直近の構築時間/描画時間)
- **GPU 完了待ち(重要な発見)**: D3DImage 経路では `Flush()` がコマンド発行のみで GPU 完了を待たないため、大規模メッシュ(フレーム数十 ms)でテーマ切替などの単発再描画時に WPF 合成スレッドが**描画完了前の共有サーフェスをコピー**し、前フレームが表示され続ける競合が発覚。`Render()` 末尾にイベントクエリ(`D3D11_QUERY_EVENT`)での完了待ちを追加して解消。副次的に `LastRenderTime` が実フレーム時間を示すようになった(従来は発行時間のみで無意味だった)。併せて `IsFrontBufferAvailableChanged` 復帰時に `SetBackBuffer` を張り直す処理も追加
- **ギャラリー「3D Benchmark」**: 合成波面メッシュ(10万〜5,000万を ComboBox 選択、既定 100万、生成も並列化)+コンター+統計表示(GetStatistics 参照実装)+回転アニメ実測 FPS+面ピック動作確認+Top/Fit ボタン
- **Release ベンチ結果(2026-08-01、ハードウェア GPU、MSAA 4x)**:

| 三角形数 | 節点数  | チャンク | 構築時間 | 描画時間 | 回転アニメ FPS | WorkingSet |
| -------- | ------- | -------- | -------- | -------- | -------------- | ---------- |
| 100万    | 50万    | 1        | 157 ms   | 2.4 ms   | 1,049          | 372 MB     |
| 1,000万  | 500万   | 2        | 410 ms   | 6.8 ms   | 254            | 1.1 GB     |
| 2,500万  | 1,250万 | 4        | 706 ms   | 10.0 ms  | 96             | 3.2 GB     |
| 5,000万  | 2,500万 | 7        | 1,281 ms | 30.0 ms  | 31.9           | 5.8 GB     |

- 目標(5,000万三角形でロード数秒台・操作 30fps)を達成。エッジ抽出は既定閾値でスキップ(1,000万以上)
- **検証**: xUnit 13 件追加(計 151、チャンク境界/再マップ/ピック ID 逆引き/並列一致/閾値/統計)+ `.dev/scripts/verify-viewport-benchmark.ps1`(1M 統計+ピック、10M 統計+チャンク数 ≥ 2+3×3 グリッドピック 9/9 ヒットで両チャンク跨ぎを確認+両テーマスクショ)全パス。glyphs/section の既存 UIA 回帰も確認。`.dev/scripts/bench-viewport-release.ps1` で Release ベンチを自動化
- **バックログ**: 非同期構築(旧シーン表示+進捗イベント) / LOD・メッシュ削減による数億三角形直接描画 / 空間均等サンプリング

## 7. テスト方針

- **UI に依存しないロジックのみ** xUnit でテストする:
  - ツリーのフラット化・選択範囲計算
  - 値→色変換(カラーマップ)
  - 数値+単位のパース
  - ログのリングバッファ
- UI 自動テストは当面やらない(費用対効果が低い)。UI はギャラリーで目視検証

## 8. 実装フェーズ

| フェーズ                                             | 内容                                                                                                                                                                                                                                              | 状態                 |
| ---------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------- |
| **Phase 0 — 基盤**                                   | `WpfCustomUI.Controls` へ改名、`XmlnsDefinition`、デザイントークン定義、ダークテーマ辞書、ギャラリー骨格、テストプロジェクト追加                                                                                                                  | ✅ 完了 (2026-07-31) |
| **Phase 1 — 標準コントロールスタイル(コア集合)**     | Button / TextBox / ComboBox / CheckBox / ScrollBar / TabControl / Menu / ToolTip / Slider / ProgressBar など、CAE コントロールの部品になるものを優先                                                                                              | ✅ 完了 (2026-07-31) |
| **Phase 2 — 部品系 CAE コントロール**                | 単位付き数値入力、グループパネル/Expander(他の部品になるため先行)                                                                                                                                                                                 | ✅ 完了 (2026-07-31) |
| **Phase 3 — プロパティグリッド**                     | Phase 2 のエディタを利用                                                                                                                                                                                                                          | ✅ 完了 (2026-08-01) |
| **Phase 4 — モデルツリー**                           | フラット化ツリー。ロジックのテスト重点                                                                                                                                                                                                            | ✅ 完了 (2026-08-01) |
| **Phase 5 — 独立系**                                 | ログコンソール / 進捗表示 / カラーマップ凡例(相互独立なので順不同)                                                                                                                                                                                | ✅ 完了 (2026-08-01) |
| **Phase 6 — シェル軽量群**                           | GridSplitter / GroupBox / Separator / ToolBar / StatusBar / SearchBox(+PropertyGrid フィルタ改修)                                                                                                                                                 | ✅ 完了 (2026-08-01) |
| **Phase 7 — ウィンドウ系**                           | WcuWindow(クローム) / WcuDialogWindow / WcuMessageBox / ToastHost / BusyOverlay                                                                                                                                                                   | ✅ 完了 (2026-08-01) |
| **Phase 8 — DataGrid**                               | 標準 DataGrid のフルスタイル化(最大工数のため独立フェーズ)                                                                                                                                                                                        | ✅ 完了 (2026-08-01) |
| **Phase 9 — 入力・ツールバー小物**                   | DropDownButton / SplitButton / PathBox / RangeSlider / ColorPicker(ColorEditor) / Vector3Box + PropertyGrid 連携(Path/Color/Vector3 PropertyItem)                                                                                                 | ✅ 完了 (2026-08-01) |
| **Phase 10 — テーマ網羅+小物**                       | TreeView / ListView(GridView) / PasswordBox / RichTextBox / Hyperlink / Label のスタイル穴埋め + InfoBar / ToggleSwitch / ProgressRing                                                                                                            | ✅ 完了 (2026-08-01) |
| **Phase 11 — ポスト処理系小物 第2弾**                | PlaybackBar(結果アニメーション再生バー) / ColorScaleEditor(カラーマップ設定エディタ、`ColorScale.Clone()` 追加)                                                                                                                                   | ✅ 完了 (2026-08-01) |
| **Phase 12 — 小物の最終弾**                          | CheckComboBox / MatrixBox / ModelTree インライン名前変更 / KeyGestureBox / Wizard(StepIndicator)                                                                                                                                                  | ✅ 完了 (2026-08-01) |
| **Phase 13 — ドッキング**                            | `WpfCustomUI.Docking` 新設(Dirkster.AvalonDock 4.74.1)。WcuDockTheme(ResourceKeys 再配色+Wcu トークン)/ DockLayout 永続化ヘルパー / フルシェルデモ                                                                                                | ✅ 完了 (2026-08-01) |
| **Phase 14 — Charts**                                | `WpfCustomUI.Charts` 新設(ScottPlot 5)。WcuPlot/WcuChartTheme(トークン配色+ThemeChanged 追従)/ ConvergenceMonitor / HistoryChart / FrequencyResponsePlot / HistogramChart                                                                         | ✅ 完了 (2026-08-01) |
| **Phase 15 — ライトテーマ**                          | Tokens.Light.xaml(VS 2022 Light 準拠)+ セマンティック Color キー正式化(Docking/Charts 移行)+ ナビ常設テーマトグル + GetSystemTheme() + 全ページ×両テーマ検証                                                                                      | ✅ 完了 (2026-08-01) |
| **Phase 16 — 3D ビューポート(最小核)**               | `WpfCustomUI.Viewport3D` 新設(Silk.NET D3D11 + D3DImage 自作エンジン)。WcuViewport(カメラ/Fit/両投影) / ViewportMesh(三角形+節点スカラー) / ColorScale コンター / エッジ重畳 / 軸トライアッド / WARP フォールバック                               | ✅ 完了 (2026-08-01) |
| **Phase 17 — ビューポート第2弾(選択+操作系)**        | GPU ID ピッキング(パーツ/面/節点) / ViewportSelection モデル+ハイライト描画内蔵 / クリック・Ctrl トグル・矩形選択 / SetStandardView + クリック式 ViewCube(補間アニメ付き)                                                                         | ✅ 完了 (2026-08-01) |
| **Phase 18 — ビューポート第3弾(変形+アニメ)**        | ViewportMesh.Displacements + GPU 頂点シェーダ変形(スケールは cbuffer、フレーム切替は部分更新) / DeformationScale / モード振動アニメ内蔵 / 非変形ワイヤフレーム重畳 / 自動スケール推奨値 / PlaybackBar 連携デモ                                    | ✅ 完了 (2026-08-01) |
| **Phase 19 — ビューポート第4弾(断面カット)**         | SectionPlane DP(点+法線、SV_ClipDistance で全パス一貫クリップ) / ViewportMesh.IsClippable / 平面インジケータ内蔵(操作 UI はアプリ側) / 新ページ「3D Section」(厚肉円筒 Lamé 解+アプリ断面スライス参照実装)                                        | ✅ 完了 (2026-08-01) |
| **Phase 20 — ビューポート第5弾(プローブ+注釈)**      | PickMode.Probe(GPU ID ピック+レイ交差+重心補間) / ProbePicked イベント+ProbeLabelFormatter / Annotations(節点バインド主体、変形追従、WPF オーバーレイチップ+リーダーライン) / 新ページ「3D Probe」(Kirsch 円孔板)                                 | ✅ 完了 (2026-08-01) |
| **Phase 21 — ビューポート第6弾(ベクトルグリフ)**     | ViewportMesh.VectorValues + GPU インスタンシング低ポリ 3D 矢印(変形追従・断面クリップ対応) / ShowGlyphs / GlyphScale+推奨値ヘルパ / GlyphStride 間引き / GlyphColorScale(\|v\|→カラーマップ) / 新ページ「3D Glyphs」(厚肉円筒 Lamé 変位場)        | ✅ 完了 (2026-08-01) |
| **Phase 22 — ビューポート第7弾(大規模メッシュ性能)** | 表面三角形 5,000万目標。頂点/インデックスのチャンク分割(ピック ID オフセット対応) / 並列ジオメトリ構築+中間コピー削減 / EdgeExtractionLimit DP(既定 500万) / GetStatistics() 統計 API / 新ページ「3D Benchmark」(合成波面 10万〜5,000万+FPS 計測) | ✅ 完了 (2026-08-01) |
