using CaeStudio.App.Behaviors;
using CaeStudio.App.Services;
using CaeStudio.Application;
using CaeStudio.Domain.Meshing;
using CaeStudio.Domain.Models;
using CaeStudio.Domain.Solving;
using R3;
using System.Collections.ObjectModel;
using System.Globalization;
using WpfCustomUI.Charts;
using WpfCustomUI.Controls;
using WpfCustomUI.Controls.Theming;
using WpfCustomUI.Viewport3D;

namespace CaeStudio.App.ViewModels;

/// <summary>
/// メインシェルの VM。ProjectStore(単一情報源)+AnalysisRunner(ユースケース)を
/// R3 ストリームで UI 状態へ合成する。View への参照は持たない(spec 6.26.3)。
/// </summary>
public sealed class MainViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly ProjectStore _store;
    private readonly AnalysisRunner _runner;
    private readonly IJobClient _jobs;
    private readonly IDialogService _dialogs;
    private readonly IProjectRepository _repository;
    private readonly ISettingsService _settings;
    private readonly Subject<ToastRequest> _toasts = new();
    private readonly SynchronizationContext _uiContext;
    private readonly Dictionary<TreeNode, string?> _nodeCategories = [];
    private readonly HashSet<Guid> _announcedJobs = [];

    /// <summary>位相スイープ 1 周のフレーム数(PlaybackBar.FrameCount と一致)。</summary>
    public const int PhaseFrameCount = 60;

    /// <summary>リボンのタブ位置(モデル/解析/結果/表示。ワークフロー順)。</summary>
    public const int RibbonTabModel = 0;
    public const int RibbonTabAnalysis = 1;
    public const int RibbonTabResults = 2;
    public const int RibbonTabView = 3;

    private ConvergenceSeries? _activeSeries;
    private CaeProjectData? _resultProject;
    private CaeProjectData? _runProject;
    private ModalResult? _shownModalResult;
    private TreeNode? _rootNode;
    private bool _updatingFromPropertyGrid;
    private bool _pendingFit;
    private string? _categoryFilter;
    private int _runNumber;
    private int _viewRequestSequence;

    public MainViewModel(
        ProjectStore store, AnalysisRunner runner, IJobClient jobs, IDialogService dialogs,
        IProjectRepository repository, ISettingsService settings)
    {
        _store = store;
        _runner = runner;
        _jobs = jobs;
        _dialogs = dialogs;
        _repository = repository;
        _settings = settings;
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("MainViewModel は UI スレッドで生成してください。");

        BuildTree();
        RebuildPropertyItems();
        SyncRecentFiles();
        IsLightTheme = Register(new BindableReactiveProperty<bool>(settings.Current.Theme == "Light"));
        RunGestureText = Register(new BindableReactiveProperty<string>(settings.Current.RunGesture));

        // ---- ドキュメント表示名(タイトルバーはアプリ名固定、文書名は TitleBar 内に別表示) ----
        Register(_store.Current
            .CombineLatest(_store.IsDirty, (project, dirty) => $"{project.Name}{(dirty ? " *" : "")}")
            .Subscribe(this, static (title, self) => self.DocumentTitle.Value = title));

        // ---- テーマトグル(表示メニュー)→ 即適用+設定保存 ----
        Register(IsLightTheme.Skip(1).Subscribe(this, static (light, self) =>
        {
            ThemeManager.SetTheme(light ? WcuThemeVariant.Light : WcuThemeVariant.Dark);
            self._settings.Update(s => s with { Theme = light ? "Light" : "Dark" });
        }));

        // ---- 材料変更 → 弾性マトリクス D の表示更新(MatrixBox) ----
        Register(_store.Current
            .Select(static project => project.Material)
            .DistinctUntilChanged()
            .Subscribe(this, static (material, self) => self.UpdateElasticityMatrix(material)));

        // ---- ツリーフィルタ(SearchBox)----
        Register(TreeFilter.Subscribe(this, static (_, self) => self.ApplyTreeFilter()));

        // ---- 表示項目(CheckComboBox)→ ビューポート反映 ----
        SelectedDisplayOptions.CollectionChanged += (_, _) => ApplyDisplayOptions();

        // ---- パーツ色(ColorPropertyItem)→ プレビューへ即時反映 ----
        Register(PartColor.Skip(1).Subscribe(this, static (_, self) => self.ApplyPartColor()));

        // ---- 入力変更 → 結果の陳腐化判定+ツリー/プロパティ追従 ----
        Register(_store.Current.Subscribe(this, static (project, self) =>
        {
            if (self._resultProject is not null && !ReferenceEquals(project, self._resultProject))
            {
                self.IsResultStale.Value = true;
                self.IsJobSnapshotMismatch.Value = true;
            }

            if (self._rootNode is not null)
            {
                self._rootNode.Name = project.Name;
            }

            if (!self._updatingFromPropertyGrid)
            {
                self.RebuildPropertyItems();
            }
        }));

        // ---- 入力変更 → メッシュプレビュー(デバウンス+スレッドプール生成) ----
        Register(_store.Current.Skip(1)
            .Subscribe(this, static (_, self) => self.IsMeshBuilding.Value = true));
        Register(_store.Current
            .Debounce(TimeSpan.FromMilliseconds(250))
            .ObserveOnThreadPool()
            .Select(static project =>
            {
                try
                {
                    return (Project: project, Mesh: (Mesh2D?)MeshGenerator.Generate(project.Geometry), Error: (string?)null);
                }
                catch (Exception exception)
                {
                    return (project, null, exception.Message);
                }
            })
            .ObserveOn(_uiContext)
            .Subscribe(this, static (preview, self) => self.ApplyPreview(preview.Project, preview.Mesh, preview.Error)));

        // ---- 解析状態 → UI 状態 ----
        Register(_runner.State
            .ObserveOn(_uiContext)
            .Subscribe(this, static (state, self) => self.OnAnalysisStateChanged(state)));

        // ---- 残差ストリーム: 収束モニタへ直結(スレッドセーフ Append)+ステータス表示 ----
        Register(_runner.Residuals
            .Subscribe(this, static (iteration, self) => self._activeSeries?.Append(iteration.RelativeResidual)));
        Register(_runner.Residuals
            .ThrottleLast(TimeSpan.FromMilliseconds(120))
            .ObserveOn(_uiContext)
            .Subscribe(this, static (iteration, self) => self.ProgressText.Value =
                string.Create(CultureInfo.InvariantCulture,
                    $"反復 {iteration.Iteration:N0} / 残差 {iteration.RelativeResidual:E2}")));

        // ---- コマンド ----
        RunCommand = Register(IsRunning.Select(static running => !running).ToReactiveCommand());
        Register(RunCommand.SubscribeAwait(
            async (_, _) => await RunAnalysisAsync(), AwaitOperation.Drop));

        CancelCommand = Register(IsRunning.AsObservable().ToReactiveCommand());
        Register(CancelCommand.Subscribe(this, static (_, self) => self._runner.Cancel()));

        NewProjectCommand = Register(new ReactiveCommand());
        Register(NewProjectCommand.Subscribe(this, static (_, self) => self.NewProject()));

        RunStaticCommand = Register(IsRunning.Select(static running => !running).ToReactiveCommand());
        Register(RunStaticCommand.SubscribeAwait(
            async (_, _) => await RunAsAsync(AnalysisType.Static), AwaitOperation.Drop));

        RunModalCommand = Register(IsRunning.Select(static running => !running).ToReactiveCommand());
        Register(RunModalCommand.SubscribeAwait(
            async (_, _) => await RunAsAsync(AnalysisType.Modal), AwaitOperation.Drop));

        OpenCommand = Register(new ReactiveCommand());
        Register(OpenCommand.SubscribeAwait(
            async (_, _) => await OpenAsync(null), AwaitOperation.Drop));

        OpenRecentCommand = Register(new ReactiveCommand<string>());
        Register(OpenRecentCommand.SubscribeAwait(
            async (path, _) => await OpenAsync(path), AwaitOperation.Drop));

        SaveCommand = Register(new ReactiveCommand());
        Register(SaveCommand.SubscribeAwait(
            async (_, _) => await SaveAsync(saveAs: false), AwaitOperation.Drop));

        SaveAsCommand = Register(new ReactiveCommand());
        Register(SaveAsCommand.SubscribeAwait(
            async (_, _) => await SaveAsync(saveAs: true), AwaitOperation.Drop));

        SettingsCommand = Register(new ReactiveCommand());
        Register(SettingsCommand.Subscribe(this, static (_, self) => self.OpenSettings()));

        StudyCommand = Register(IsRunning.CombineLatest(
                IsStudyRunning, static (running, study) => !running && !study)
            .ToReactiveCommand());
        Register(StudyCommand.SubscribeAwait(
            async (_, _) => await RunStudyAsync(), AwaitOperation.Drop));

        FitViewCommand = Register(new ReactiveCommand());
        Register(FitViewCommand.Subscribe(this, static (_, self) => self.FitRequest.Value++));

        TreeSelectionCommand = Register(new ReactiveCommand<IReadOnlyList<ITreeNode>>());
        Register(TreeSelectionCommand.Subscribe(this, static (selected, self) => self.OnTreeSelectionChanged(selected)));

        SetViewCommand = Register(new ReactiveCommand<string>());
        Register(SetViewCommand.Subscribe(this, static (view, self) =>
            self.ViewRequest.Value = new ViewRequest(
                ++self._viewRequestSequence, Enum.Parse<ViewportStandardView>(view))));

        ProbeCommand = Register(new ReactiveCommand<ProbeResult>());
        Register(ProbeCommand.Subscribe(this, static (probe, self) => self.OnProbePicked(probe)));

        ClearAnnotationsCommand = Register(new ReactiveCommand());
        Register(ClearAnnotationsCommand.Subscribe(this, static (_, self) => self.AnnotationClearRequest.Value++));

        // リボン「モデル」タブの編集ボタン → PropertyGrid のカテゴリ絞り込み
        SelectCategoryCommand = Register(new ReactiveCommand<string>());
        Register(SelectCategoryCommand.Subscribe(this, static (category, self) =>
        {
            self._categoryFilter = category;
            self.RebuildPropertyItems();
        }));

        // 実行ボタンのビジー表示(spec 6.27.3: スピナー+「解析中…」)
        Register(IsRunning.Subscribe(this, static (running, self) =>
            self.RunButtonLabel.Value = running ? "解析中…" : "解析実行"));

        // ---- ジョブ投入模擬(spec 6.27.4) ----
        SubmitJobCommand = Register(IsRunning.Select(static running => !running).ToReactiveCommand());
        Register(SubmitJobCommand.SubscribeAwait(
            async (_, _) => await SubmitJobAsync(), AwaitOperation.Drop));

        ShowJobsCommand = Register(new ReactiveCommand());
        Register(ShowJobsCommand.Subscribe(this, static (_, self) =>
        {
            self.IsJobsVisible.Value = true;
            self.RibbonTabIndex.Value = RibbonTabAnalysis;
        }));

        CancelJobCommand = Register(new ReactiveCommand<Guid>());
        Register(CancelJobCommand.SubscribeAwait(
            async (id, _) => await _jobs.CancelAsync(id), AwaitOperation.Parallel));

        LoadJobResultCommand = Register(new ReactiveCommand<Guid>());
        Register(LoadJobResultCommand.SubscribeAwait(
            async (id, _) => await LoadJobResultAsync(id), AwaitOperation.Drop));

        ResubmitJobCommand = Register(IsRunning.Select(static running => !running).ToReactiveCommand<Guid>());
        Register(ResubmitJobCommand.SubscribeAwait(
            async (id, _) => await ResubmitJobAsync(id), AwaitOperation.Drop));

        Register(_jobs.Jobs.ObserveOn(_uiContext).Subscribe(this, static (list, self) =>
            self.OnJobsChanged(list)));

        ProbeLabelFormatter = FormatProbeLabel;

        // ---- プローブトグル → ピックモード ----
        Register(IsProbeEnabled
            .Subscribe(this, static (enabled, self) =>
                self.PickMode.Value = enabled ? ViewportPickMode.Probe : ViewportPickMode.None));

        // ---- 位相スイープ: 基準スケール × cos(2πk/N)(固有値解析時のみ) ----
        Register(DeformationScale.CombineLatest(
                PhaseFrame, IsModalResult,
                static (scale, frame, modal) => modal
                    ? scale * Math.Cos(2.0 * Math.PI * frame / PhaseFrameCount)
                    : scale)
            .Subscribe(this, static (effective, self) => self.EffectiveDeformationScale.Value = effective));

        // ---- コンター範囲(RangeSlider)→ 共有 ColorScale ----
        Register(RangeLower.CombineLatest(RangeUpper, static (lower, upper) => (lower, upper))
            .Subscribe(this, static (range, self) =>
            {
                if (range.upper > range.lower)
                {
                    self.ResultScale.Minimum = range.lower;
                    self.ResultScale.Maximum = range.upper;
                }
            }));

        // ---- モード選択 → ビューポート差し替え ----
        Register(SelectedMode.Subscribe(this, static (row, self) => self.ShowMode(row)));

        Log.Append(LogLevel.Info, "CaeStudio を起動しました(MVVM+レイヤードアーキテクチャ参照実装)");
    }

    // ================= バインディング公開面 =================

    public LogBuffer Log { get; } = new(capacity: 5000);

    /// <summary>結果コンター用カラースケール(凡例・ビューポートで共有)。</summary>
    public ColorScale ResultScale { get; } = new() { ColorMap = ColorMap.Jet, Minimum = 0.0, Maximum = 1.0 };

    public ObservableCollection<ViewportMesh> Meshes { get; } = [];

    public ObservableCollection<ConvergenceSeries> ResidualSeries { get; } = [];

    public ObservableCollection<TreeNode> TreeItems { get; } = [];

    /// <summary>トースト通知要求(View の ToastBehavior が購読)。</summary>
    public Observable<ToastRequest> Toasts => _toasts;

    /// <summary>タイトルバー右側に出す文書名(アプリ名はウィンドウ Title 側で固定)。</summary>
    public BindableReactiveProperty<string> DocumentTitle { get; } = new("CaeStudio");

    public BindableReactiveProperty<string> StatusText { get; } = new("準備完了");

    public BindableReactiveProperty<string> ProgressText { get; } = new("");

    public BindableReactiveProperty<string> MeshStatsText { get; } = new("");

    public BindableReactiveProperty<bool> IsRunning { get; } = new(false);

    /// <summary>入力が変わって結果が古くなったか(InfoBar と連動、ユーザが閉じられるよう TwoWay)。</summary>
    public BindableReactiveProperty<bool> IsResultStale { get; } = new(false);

    /// <summary>ViewportFitBehavior が監視するカウンタ。</summary>
    public BindableReactiveProperty<int> FitRequest { get; } = new(0);

    public BindableReactiveProperty<double> DeformationScale { get; } = new(1.0);

    /// <summary>ビューポートへ渡す実効変形スケール(位相スイープを掛けた後)。</summary>
    public BindableReactiveProperty<double> EffectiveDeformationScale { get; } = new(1.0);

    /// <summary>位相スイープの現在フレーム(PlaybackBar と TwoWay)。</summary>
    public BindableReactiveProperty<int> PhaseFrame { get; } = new(0);

    /// <summary>固有値解析の結果を表示中か(モードパネル/PlaybackBar の表示制御)。</summary>
    public BindableReactiveProperty<bool> IsModalResult { get; } = new(false);

    /// <summary>固有振動数テーブルの行。</summary>
    public ObservableCollection<ModeRow> ModeRows { get; } = [];

    public BindableReactiveProperty<ModeRow?> SelectedMode { get; } = new(null);

    /// <summary>パスプロット(円孔付き平板の静解析のみ、他は null)。</summary>
    public BindableReactiveProperty<PathPlotData?> PathPlot { get; } = new(null);

    /// <summary>von Mises ヒストグラムの値列(静解析のみ)。</summary>
    public BindableReactiveProperty<double[]?> HistogramValues { get; } = new(null);

    /// <summary>周波数応答(モード重ね合わせ、固有値解析のみ)。</summary>
    public ObservableCollection<FrequencyResponseSeries> FrfSeries { get; } = [];

    /// <summary>プローブモードのトグル状態(ToolBar の ToggleSwitch と TwoWay)。</summary>
    public BindableReactiveProperty<bool> IsProbeEnabled { get; } = new(false);

    public BindableReactiveProperty<ViewportPickMode> PickMode { get; } = new(ViewportPickMode.None);

    /// <summary>プローブ注釈のラベル書式(結果種別に応じて単位を切替)。</summary>
    public Func<ProbeResult, string> ProbeLabelFormatter { get; }

    /// <summary>ViewportProbeBehavior が監視する注釈全削除カウンタ。</summary>
    public BindableReactiveProperty<int> AnnotationClearRequest { get; } = new(0);

    /// <summary>標準視点の変更要求(ViewportStandardViewBehavior が監視)。</summary>
    public BindableReactiveProperty<ViewRequest?> ViewRequest { get; } = new(null);

    /// <summary>コンター範囲スライダの下限/上限(TwoWay)とスライダ最大値。</summary>
    public BindableReactiveProperty<double> RangeLower { get; } = new(0.0);

    public BindableReactiveProperty<double> RangeUpper { get; } = new(1.0);

    public BindableReactiveProperty<double> RangeMaximum { get; } = new(1.0);

    /// <summary>凡例タイトル(静解析: von Mises / 固有値解析: 正規化変位)。</summary>
    public BindableReactiveProperty<string> LegendTitle { get; } = new("von Mises [MPa]");

    public BindableReactiveProperty<IReadOnlyList<PropertyItem>> PropertyItems { get; } =
        new(Array.Empty<PropertyItem>());

    /// <summary>テーマ(表示メニューのチェック項目と TwoWay)。</summary>
    public BindableReactiveProperty<bool> IsLightTheme { get; }

    /// <summary>解析実行のショートカット表示(設定変更で更新、View がキーバインドを再構築)。</summary>
    public BindableReactiveProperty<string> RunGestureText { get; }

    /// <summary>最近使ったプロジェクトファイル(ファイルメニュー)。</summary>
    public ObservableCollection<string> RecentFiles { get; } = [];

    /// <summary>メッシュプレビュー再生成中か(BusyOverlay)。</summary>
    public BindableReactiveProperty<bool> IsMeshBuilding { get; } = new(false);

    /// <summary>モデルツリーのフィルタ文字列(SearchBox と TwoWay)。</summary>
    public BindableReactiveProperty<string> TreeFilter { get; } = new("");

    /// <summary>表示項目フィルタ(CheckComboBox)。</summary>
    public IReadOnlyList<string> DisplayOptions { get; } = [DisplayOptionEdges, DisplayOptionUndeformed];

    public ObservableCollection<object> SelectedDisplayOptions { get; } = [DisplayOptionEdges];

    /// <summary>非変形ワイヤフレーム重畳(ビューポート DP へ OneWay)。</summary>
    public BindableReactiveProperty<bool> ShowUndeformedWireframe { get; } = new(false);

    /// <summary>パーツ色(プレビューメッシュの色。PropertyGrid の ColorPropertyItem)。</summary>
    public BindableReactiveProperty<System.Windows.Media.Color> PartColor { get; } =
        new(System.Windows.Media.Color.FromRgb(0x8F, 0x9B, 0xA8));

    /// <summary>平面応力の弾性マトリクス D(MatrixBox 表示用、読み取り専用)。</summary>
    public BindableReactiveProperty<double[,]?> ElasticityMatrix { get; } = new(null);

    // ---- Phase 27: フロー誘導+段階連動パネル(spec 6.27.3) ----

    /// <summary>解析結果が存在するか(「結果」タブ/結果系コマンドの活性・エンプティステートガイド)。</summary>
    public BindableReactiveProperty<bool> HasResult { get; } = new(false);

    /// <summary>リボンの選択タブ。解析完了で「結果」へ自動切替(TwoWay)。</summary>
    public BindableReactiveProperty<int> RibbonTabIndex { get; } = new(RibbonTabModel);

    /// <summary>実行ボタンのラベル(実行中は「解析中…」)。</summary>
    public BindableReactiveProperty<string> RunButtonLabel { get; } = new("解析実行");

    /// <summary>段階連動パネルの表示状態(表示タブのトグルと TwoWay、View がドックへ反映)。</summary>
    public BindableReactiveProperty<bool> IsConvergenceVisible { get; } = new(false);

    public BindableReactiveProperty<bool> IsPathPlotVisible { get; } = new(false);

    public BindableReactiveProperty<bool> IsHistogramVisible { get; } = new(false);

    public BindableReactiveProperty<bool> IsFrfVisible { get; } = new(false);

    public BindableReactiveProperty<bool> IsStudyVisible { get; } = new(false);

    public BindableReactiveProperty<bool> IsModesVisible { get; } = new(false);

    public BindableReactiveProperty<bool> IsMaterialVisible { get; } = new(false);

    public BindableReactiveProperty<bool> IsLegendVisible { get; } = new(false);

    /// <summary>ジョブモニタパネルの表示状態(初回投入で自動表示)。</summary>
    public BindableReactiveProperty<bool> IsJobsVisible { get; } = new(false);

    /// <summary>ジョブ結果のスナップショットが現行モデルと不一致か(InfoBar)。</summary>
    public BindableReactiveProperty<bool> IsJobSnapshotMismatch { get; } = new(false);

    /// <summary>ジョブ一覧(DataGrid)。</summary>
    public ObservableCollection<JobRow> JobRows { get; } = [];

    /// <summary>収束モニタをアクティブ化する要求カウンタ(解析開始時に増加、View が監視)。</summary>
    public BindableReactiveProperty<int> ActivateConvergenceRequest { get; } = new(0);

    /// <summary>メッシュ細分化スタディの履歴(HistoryChart)。</summary>
    public ObservableCollection<ChartSeries> StudySeries { get; } = [];

    public BindableReactiveProperty<bool> IsStudyRunning { get; } = new(false);

    public BindableReactiveProperty<string> StudyStatusText { get; } =
        new("「スタディ実行」で分割数を 0.5～2 倍に変えて解析し、メッシュ収束履歴を描画します。");

    public ReactiveCommand<Unit> RunCommand { get; }

    public ReactiveCommand<Unit> CancelCommand { get; }

    public ReactiveCommand<Unit> NewProjectCommand { get; }

    public ReactiveCommand<Unit> FitViewCommand { get; }

    public ReactiveCommand<IReadOnlyList<ITreeNode>> TreeSelectionCommand { get; }

    /// <summary>標準視点コマンド(パラメータは ViewportStandardView 名)。</summary>
    public ReactiveCommand<string> SetViewCommand { get; }

    public ReactiveCommand<ProbeResult> ProbeCommand { get; }

    public ReactiveCommand<Unit> ClearAnnotationsCommand { get; }

    /// <summary>SplitButton メニュー: 解析タイプを切り替えて実行。</summary>
    public ReactiveCommand<Unit> RunStaticCommand { get; }

    public ReactiveCommand<Unit> RunModalCommand { get; }

    public ReactiveCommand<Unit> OpenCommand { get; }

    public ReactiveCommand<string> OpenRecentCommand { get; }

    public ReactiveCommand<Unit> SaveCommand { get; }

    public ReactiveCommand<Unit> SaveAsCommand { get; }

    public ReactiveCommand<Unit> SettingsCommand { get; }

    public ReactiveCommand<Unit> StudyCommand { get; }

    /// <summary>リボン「モデル」タブ: PropertyGrid のカテゴリ絞り込み(パラメータはカテゴリ名)。</summary>
    public ReactiveCommand<string> SelectCategoryCommand { get; }

    /// <summary>ジョブとして投入(外部 HPC 模擬)。</summary>
    public ReactiveCommand<Unit> SubmitJobCommand { get; }

    /// <summary>ジョブモニタパネルを表示。</summary>
    public ReactiveCommand<Unit> ShowJobsCommand { get; }

    /// <summary>ジョブのキャンセル(パラメータ=ジョブ ID)。</summary>
    public ReactiveCommand<Guid> CancelJobCommand { get; }

    /// <summary>完了ジョブの結果をビューポートへ読込。</summary>
    public ReactiveCommand<Guid> LoadJobResultCommand { get; }

    /// <summary>同じスナップショットで再投入。</summary>
    public ReactiveCommand<Guid> ResubmitJobCommand { get; }

    private const string DisplayOptionEdges = "メッシュエッジ";
    private const string DisplayOptionUndeformed = "非変形ワイヤフレーム";

    // ================= 解析実行 =================

    /// <summary>SplitButton メニューから解析タイプを切り替えて実行する。</summary>
    private async Task RunAsAsync(AnalysisType type)
    {
        if (_store.Current.CurrentValue.AnalysisType != type)
        {
            _store.Update(p => p with { AnalysisType = type });
        }

        await RunAnalysisAsync();
    }

    private async Task RunAnalysisAsync()
    {
        var project = _store.Current.CurrentValue;
        _runProject = project;
        _runNumber++;

        var kind = project.AnalysisType == AnalysisType.Modal ? "固有値" : "静";
        _activeSeries = new ConvergenceSeries($"{kind}解析 #{_runNumber}");
        ResidualSeries.Add(_activeSeries);
        while (ResidualSeries.Count > 3)
        {
            ResidualSeries.RemoveAt(0);
        }

        ProgressText.Value = "";
        Log.Append(LogLevel.Info, $"{kind}解析を開始: {project.Name}({DescribeMesh(project)})");

        // 段階連動: 解析中は収束モニタを自動表示+アクティブ化(spec 6.27.3)
        IsConvergenceVisible.Value = true;
        ActivateConvergenceRequest.Value++;

        await _runner.RunAsync(project);
    }

    private async Task SubmitJobAsync()
    {
        var project = _store.Current.CurrentValue;
        var id = await _jobs.SubmitAsync(project);
        IsJobsVisible.Value = true;
        RibbonTabIndex.Value = RibbonTabAnalysis;
        Log.Append(LogLevel.Info, $"ジョブを投入しました: {project.Name}({id:N})");
        _toasts.OnNext(new ToastRequest("ジョブをキューに投入しました", ToastLevel.Info));
        StatusText.Value = "ジョブ投入済み(ジョブモニタで進捗を確認)";
    }

    private void OnJobsChanged(IReadOnlyList<JobInfo> jobs)
    {
        // DataGrid 行を ID 順で同期(進捗更新のたびに全置換)
        JobRows.Clear();
        foreach (var job in jobs)
        {
            JobRows.Add(JobRow.From(job));

            if (_announcedJobs.Contains(job.Id))
            {
                continue;
            }

            if (job.State is JobState.Completed or JobState.Failed or JobState.Cancelled)
            {
                _announcedJobs.Add(job.Id);
                AnnounceJobTerminal(job);
            }
        }
    }

    private void AnnounceJobTerminal(JobInfo job)
    {
        switch (job.State)
        {
            case JobState.Completed:
                Log.Append(LogLevel.Info, $"ジョブ完了: {job.Name}");
                _toasts.OnNext(new ToastRequest(
                    $"ジョブ完了: {job.Name}(モニタから結果を読込)", ToastLevel.Success));
                StatusText.Value = $"ジョブ完了: {job.Name}";
                break;
            case JobState.Failed:
                Log.Append(LogLevel.Error, $"ジョブ失敗: {job.Name} — {job.ErrorMessage}");
                _toasts.OnNext(new ToastRequest($"ジョブ失敗: {job.ErrorMessage}", ToastLevel.Error));
                break;
            case JobState.Cancelled:
                Log.Append(LogLevel.Warning, $"ジョブキャンセル: {job.Name}");
                _toasts.OnNext(new ToastRequest($"ジョブをキャンセルしました: {job.Name}", ToastLevel.Warning));
                break;
        }
    }

    private async Task LoadJobResultAsync(Guid jobId)
    {
        var result = await _jobs.TryGetResultAsync(jobId);
        if (result is null)
        {
            _toasts.OnNext(new ToastRequest("結果を取得できません(未完了または失敗)", ToastLevel.Warning));
            return;
        }

        _resultProject = result.Snapshot;
        IsJobSnapshotMismatch.Value = !ReferenceEquals(_store.Current.CurrentValue, result.Snapshot);
        IsResultStale.Value = IsJobSnapshotMismatch.Value;

        if (result.ModalResult is { } modal)
        {
            ApplyModalResult(modal);
        }
        else if (result.StaticResult is { } staticResult)
        {
            ApplyStaticResult(staticResult);
        }
        else
        {
            _toasts.OnNext(new ToastRequest("ジョブ結果が空です", ToastLevel.Warning));
            return;
        }

        HasResult.Value = true;
        IsLegendVisible.Value = true;
        GoToResultsTab();
        Log.Append(LogLevel.Info, $"ジョブ結果を読込: {result.Snapshot.Name}");
        _toasts.OnNext(new ToastRequest("ジョブ結果を表示しました", ToastLevel.Success));
    }

    private async Task ResubmitJobAsync(Guid jobId)
    {
        var job = _jobs.Jobs.CurrentValue.FirstOrDefault(j => j.Id == jobId);
        if (job is null)
        {
            return;
        }

        // 元スナップショットを再投入(現行モデルではなく投入時入力を再現)
        var id = await _jobs.SubmitAsync(job.Snapshot);
        IsJobsVisible.Value = true;
        Log.Append(LogLevel.Info, $"ジョブを再投入: {job.Name} → {id:N}");
        _toasts.OnNext(new ToastRequest("ジョブを再投入しました", ToastLevel.Info));
    }

    private void OnAnalysisStateChanged(AnalysisState state)
    {
        IsRunning.Value = state == AnalysisState.Running;

        switch (state)
        {
            case AnalysisState.Running:
                StatusText.Value = "解析実行中...";
                break;

            case AnalysisState.Completed:
                OnAnalysisCompleted();
                break;

            case AnalysisState.Cancelled:
                StatusText.Value = "解析をキャンセルしました";
                ProgressText.Value = "";
                Log.Append(LogLevel.Warning, "解析をキャンセルしました");
                _toasts.OnNext(new ToastRequest("解析をキャンセルしました", ToastLevel.Warning));
                break;

            case AnalysisState.Failed:
                StatusText.Value = "解析エラー";
                ProgressText.Value = "";
                Log.Append(LogLevel.Error, $"解析エラー: {_runner.ErrorMessage.CurrentValue}");
                _toasts.OnNext(new ToastRequest($"解析エラー: {_runner.ErrorMessage.CurrentValue}", ToastLevel.Error));
                break;

            case AnalysisState.Idle:
                StatusText.Value = "準備完了";
                break;
        }
    }

    private void OnAnalysisCompleted()
    {
        _resultProject = _runProject;
        IsResultStale.Value = !ReferenceEquals(_store.Current.CurrentValue, _resultProject);

        if (_runProject?.AnalysisType == AnalysisType.Modal &&
            _runner.ModalResult.CurrentValue is { } modal)
        {
            ApplyModalResult(modal);
        }
        else if (_runner.StaticResult.CurrentValue is { } result)
        {
            ApplyStaticResult(result);
        }

        // フロー誘導: 結果系を活性化し「結果」タブへ自動切替(spec 6.27.3)
        HasResult.Value = true;
        IsLegendVisible.Value = true;
        GoToResultsTab();

        _toasts.OnNext(new ToastRequest("解析が完了しました", ToastLevel.Success));
    }

    /// <summary>
    /// 結果タブへ切替。HasResult で IsEnabled が立つ前に SelectedIndex を書くと
    /// 無効タブへの切替が握りつぶされることがあるため、UI キューへ遅延する。
    /// </summary>
    private void GoToResultsTab() =>
        _uiContext.Post(_ => RibbonTabIndex.Value = RibbonTabResults, null);

    private void ApplyStaticResult(StaticResult result)
    {
        var name = _resultProject?.Name ?? "結果";

        _shownModalResult = null;
        IsModalResult.Value = false;
        ModeRows.Clear();
        SelectedMode.Value = null;
        FrfSeries.Clear();

        // 上限は実最大値(最大応力点が凡例の最上色=赤で表示される。CAE の慣例)
        SetContourRange(result.MaxVonMises > 0 ? result.MaxVonMises : 1.0);
        LegendTitle.Value = "von Mises [MPa]";
        DeformationScale.Value = SuggestDeformationScale(result);

        // コンターを覆い隠さないようエッジ表示は自動 OFF(CheckComboBox で再有効化可)
        SelectedDisplayOptions.Remove(DisplayOptionEdges);
        Meshes.Clear();
        Meshes.Add(ViewportMeshFactory.CreateStaticResult(result, name));
        MeshStatsText.Value = DescribeMeshCounts(result.Mesh);

        // ポスト処理チャート: パスプロット(円孔平板のみ)+ヒストグラム
        PathPlot.Value = _resultProject is not null
            ? PostProcessing.CreateKirschPath(_resultProject, result) : null;
        HistogramValues.Value = result.NodalVonMises;

        // 段階連動: 静解析の成果があるパネルだけ自動表示(spec 6.27.3)
        IsPathPlotVisible.Value = PathPlot.Value is not null;
        IsHistogramVisible.Value = true;
        IsFrfVisible.Value = false;
        IsModesVisible.Value = false;

        StatusText.Value = string.Create(CultureInfo.InvariantCulture,
            $"解析完了({result.Iterations:N0} 反復, {result.SolveTime.TotalSeconds:0.00} s)");
        Log.Append(LogLevel.Info, string.Create(CultureInfo.InvariantCulture,
            $"解析完了: 反復 {result.Iterations:N0} / 残差 {result.FinalResidual:E2} / " +
            $"組立 {result.BuildTime.TotalMilliseconds:N0} ms / 求解 {result.SolveTime.TotalMilliseconds:N0} ms"));
        Log.Append(LogLevel.Info, string.Create(CultureInfo.InvariantCulture,
            $"最大 von Mises = {result.MaxVonMises:0.0} MPa / 最大変位 = {result.MaxDisplacement:E3} mm"));
    }

    private void ApplyModalResult(ModalResult result)
    {
        _shownModalResult = result;

        // 固有振動数テーブル(片持ち板の曲げ卓越モードのみ Euler-Bernoulli 理論値と比較。
        // FEM には軸振動モードも現れるため、変位方向で曲げ/軸を判別して対応付ける)
        var beam = _resultProject?.Geometry as CantileverPlateGeometry;
        var material = _resultProject?.Material;
        var bendingCount = 0;
        ModeRows.Clear();
        foreach (var mode in result.Modes)
        {
            double? theory = null;
            if (beam is not null && material is not null &&
                bendingCount < ExactSolutions.CantileverBetaL.Length &&
                IsTransverseDominant(mode.Shape))
            {
                theory = ExactSolutions.CantileverFrequency(
                    bendingCount, material.YoungsModulus, material.Density, beam.Length, beam.Height);
                bendingCount++;
            }

            ModeRows.Add(new ModeRow(
                mode.Index, mode,
                string.Create(CultureInfo.InvariantCulture, $"{mode.FrequencyHz:N1}"),
                theory is { } t ? string.Create(CultureInfo.InvariantCulture, $"{t:N1}") : "-",
                theory is { } th ? string.Create(CultureInfo.InvariantCulture,
                    $"{(mode.FrequencyHz - th) / th * 100.0:+0.0;-0.0} %") : "-"));
        }

        // コンター: 正規化変位量 |u| ∈ [0, 1]。エッジ表示は自動 OFF(静解析と同じ)
        SelectedDisplayOptions.Remove(DisplayOptionEdges);
        SetContourRange(1.0);
        LegendTitle.Value = "|u| 正規化 [-]";
        DeformationScale.Value = SuggestModalScale(result.Mesh);
        PhaseFrame.Value = 0;
        IsModalResult.Value = true;

        PathPlot.Value = null;
        HistogramValues.Value = null;
        FrfSeries.Clear();
        if (result.Modes.Count > 0)
        {
            FrfSeries.Add(PostProcessing.CreateFrf(result));
        }

        // 段階連動: 固有値解析の成果があるパネルだけ自動表示(spec 6.27.3)
        IsPathPlotVisible.Value = false;
        IsHistogramVisible.Value = false;
        IsFrfVisible.Value = FrfSeries.Count > 0;
        IsModesVisible.Value = true;

        MeshStatsText.Value = DescribeMeshCounts(result.Mesh);

        // 1 次モードを選択(SelectedMode の購読が ShowMode を呼ぶ)
        SelectedMode.Value = ModeRows.FirstOrDefault();
        if (SelectedMode.Value is null)
        {
            Meshes.Clear();
        }

        StatusText.Value = string.Create(CultureInfo.InvariantCulture,
            $"固有値解析完了({result.Modes.Count} モード, {result.SolveTime.TotalSeconds:0.00} s)");
        Log.Append(LogLevel.Info, string.Create(CultureInfo.InvariantCulture,
            $"固有値解析完了: CG 総反復 {result.TotalCgIterations:N0} / " +
            $"組立 {result.BuildTime.TotalMilliseconds:N0} ms / 求解 {result.SolveTime.TotalMilliseconds:N0} ms"));
        foreach (var row in ModeRows)
        {
            Log.Append(LogLevel.Info,
                $"  モード {row.Index}: {row.FrequencyText} Hz(理論 {row.TheoryText} Hz / 誤差 {row.ErrorText})");
        }
    }

    /// <summary>選択モードのモード形状をビューポートへ表示する。</summary>
    private void ShowMode(ModeRow? row)
    {
        if (row is null || _shownModalResult is null)
        {
            return;
        }

        var mesh = ViewportMeshFactory.CreateModeShape(
            _shownModalResult, row.Mode, $"モード {row.Index}({row.FrequencyText} Hz)");
        mesh.ShowEdges = SelectedDisplayOptions.Contains(DisplayOptionEdges);
        Meshes.Clear();
        Meshes.Add(mesh);
    }

    /// <summary>y 方向変位が卓越するモードか(片持ち板の面内曲げ ↔ 軸振動の判別)。</summary>
    private static bool IsTransverseDominant(double[] shape)
    {
        var (sumX2, sumY2) = (0.0, 0.0);
        for (var node = 0; node < shape.Length / 2; node++)
        {
            sumX2 += shape[node * 2] * shape[node * 2];
            sumY2 += shape[node * 2 + 1] * shape[node * 2 + 1];
        }

        return sumY2 > sumX2;
    }

    private void SetContourRange(double maximum)
    {
        RangeMaximum.Value = maximum;
        RangeLower.Value = 0.0;
        RangeUpper.Value = maximum;
        ResultScale.Minimum = 0.0;
        ResultScale.Maximum = maximum;
    }

    // ================= プローブ =================

    private void OnProbePicked(ProbeResult probe)
    {
        Log.Append(LogLevel.Info, string.Create(CultureInfo.InvariantCulture,
            $"プローブ: {FormatProbeLabel(probe)} @ ({probe.X:0.0}, {probe.Y:0.0}) mm"));
    }

    /// <summary>注釈ラベル: 結果種別に応じた単位付き書式(spec 6.20 フォーマッター差し替えの参照実装)。</summary>
    private string FormatProbeLabel(ProbeResult probe)
    {
        var value = probe.NodeScalarValue ?? probe.ScalarValue;
        if (value is not { } v)
        {
            return $"N{probe.NodeIndex}";
        }

        return _shownModalResult is not null
            ? string.Create(CultureInfo.InvariantCulture, $"N{probe.NodeIndex}: |u| = {v:0.000}(正規化)")
            : string.Create(CultureInfo.InvariantCulture, $"N{probe.NodeIndex}: σv = {v:0.0} MPa");
    }

    // ================= メッシュプレビュー =================

    private void ApplyPreview(CaeProjectData project, Mesh2D? mesh, string? error)
    {
        IsMeshBuilding.Value = false;
        if (mesh is null)
        {
            StatusText.Value = "形状パラメータが不正です";
            Log.Append(LogLevel.Warning, $"メッシュ生成エラー: {error}");
            return;
        }

        MeshStatsText.Value = DescribeMeshCounts(mesh);

        // 現在の入力に対応する結果を表示中なら差し替えない(コンターを維持)
        var resultIsCurrent = _resultProject is not null
            && ReferenceEquals(project, _resultProject)
            && (_runner.StaticResult.CurrentValue is not null
                || _runner.ModalResult.CurrentValue is not null);
        if (!resultIsCurrent)
        {
            var preview = ViewportMeshFactory.CreatePreview(mesh, project.Name);
            preview.Color = PartColor.Value;
            preview.ShowEdges = SelectedDisplayOptions.Contains(DisplayOptionEdges);
            Meshes.Clear();
            Meshes.Add(preview);
        }

        if (_pendingFit)
        {
            _pendingFit = false;
            FitRequest.Value++;
        }
    }

    /// <summary>CheckComboBox の表示項目選択をビューポートへ反映する。</summary>
    private void ApplyDisplayOptions()
    {
        var edges = SelectedDisplayOptions.Contains(DisplayOptionEdges);
        ShowUndeformedWireframe.Value = SelectedDisplayOptions.Contains(DisplayOptionUndeformed);
        foreach (var mesh in Meshes)
        {
            mesh.ShowEdges = edges;
        }
    }

    /// <summary>PropertyGrid の色編集をプレビューメッシュ(単色表示)へ即時反映する。</summary>
    private void ApplyPartColor()
    {
        foreach (var mesh in Meshes)
        {
            if (mesh.ScalarValues is null)
            {
                mesh.Color = PartColor.Value;
            }
        }
    }

    /// <summary>平面応力の弾性マトリクス D(3×3)を MatrixBox 表示用に更新する。</summary>
    private void UpdateElasticityMatrix(Material material)
    {
        var e = material.YoungsModulus;
        var nu = material.PoissonsRatio;
        var factor = e / (1.0 - nu * nu);
        ElasticityMatrix.Value = new[,]
        {
            { factor, factor * nu, 0.0 },
            { factor * nu, factor, 0.0 },
            { 0.0, 0.0, factor * (1.0 - nu) / 2.0 },
        };
    }

    // ================= メッシュ細分化スタディ =================

    private async Task RunStudyAsync()
    {
        var project = _store.Current.CurrentValue;
        var metricLabel = RefinementStudy.MetricLabel(project.AnalysisType);

        IsStudyRunning.Value = true;
        IsStudyVisible.Value = true;
        StudyStatusText.Value = "スタディ実行中...";
        Log.Append(LogLevel.Info, $"メッシュ細分化スタディを開始: {project.Name}({metricLabel})");

        var xs = new List<double>();
        var ys = new List<double>();
        var series = new ChartSeries { Name = $"{project.Name}: {metricLabel}" };
        if (StudySeries.Count >= 3)
        {
            StudySeries.RemoveAt(0);
        }

        StudySeries.Add(series);

        try
        {
            await Task.Run(() => RefinementStudy.Run(project, RefinementStudy.DefaultFactors, point =>
                _uiContext.Post(_ =>
                {
                    xs.Add(point.Dofs);
                    ys.Add(point.Metric);
                    series.X = [.. xs];
                    series.Y = [.. ys];
                    StudyStatusText.Value = string.Create(CultureInfo.InvariantCulture,
                        $"スタディ {xs.Count} 点目: {point.Dofs:N0} 自由度 → {point.Metric:G5}");
                    Log.Append(LogLevel.Info, string.Create(CultureInfo.InvariantCulture,
                        $"  スタディ点: {point.Dofs:N0} 自由度 / {metricLabel} = {point.Metric:G6} / " +
                        $"{point.Elapsed.TotalMilliseconds:N0} ms"));
                }, null)));

            StudyStatusText.Value = "スタディ完了(横軸: 自由度数、縦軸: " + metricLabel + ")";
            Log.Append(LogLevel.Info, "メッシュ細分化スタディ完了");
        }
        catch (Exception exception)
        {
            StudyStatusText.Value = $"スタディ失敗: {exception.Message}";
            Log.Append(LogLevel.Error, $"スタディ失敗: {exception.Message}");
        }
        finally
        {
            IsStudyRunning.Value = false;
        }
    }

    // ================= 新規/開く/保存 =================

    private void NewProject()
    {
        if (_store.IsDirty.CurrentValue &&
            !_dialogs.ConfirmDiscardChanges(_store.Current.CurrentValue.Name))
        {
            return;
        }

        if (_dialogs.ShowNewProjectWizard() is not { } project)
        {
            return;
        }

        ResetWorkspace(project, filePath: null);
        Log.Append(LogLevel.Info, $"新規プロジェクトを作成: {project.Name}");
    }

    private async Task OpenAsync(string? filePath)
    {
        if (_store.IsDirty.CurrentValue &&
            !_dialogs.ConfirmDiscardChanges(_store.Current.CurrentValue.Name))
        {
            return;
        }

        filePath ??= _dialogs.ShowOpenProjectDialog(_settings.Current.DefaultProjectDirectory);
        if (filePath is null)
        {
            return;
        }

        try
        {
            var project = await _repository.LoadAsync(filePath);
            ResetWorkspace(project, filePath);
            AddRecentFile(filePath);
            Log.Append(LogLevel.Info, $"プロジェクトを開きました: {filePath}");
        }
        catch (Exception exception)
        {
            Log.Append(LogLevel.Error, $"プロジェクトを開けません: {exception.Message}");
            _dialogs.ShowError($"プロジェクトを開けません。\n{exception.Message}");
        }
    }

    private async Task SaveAsync(bool saveAs)
    {
        var filePath = !saveAs ? _store.FilePath.CurrentValue : null;
        filePath ??= _dialogs.ShowSaveProjectDialog(
            $"{_store.Current.CurrentValue.Name}.wcuproj",
            _settings.Current.DefaultProjectDirectory);
        if (filePath is null)
        {
            return;
        }

        try
        {
            await _repository.SaveAsync(_store.Current.CurrentValue, filePath);
            _store.MarkSaved(filePath);
            AddRecentFile(filePath);
            Log.Append(LogLevel.Info, $"プロジェクトを保存しました: {filePath}");
            _toasts.OnNext(new ToastRequest("プロジェクトを保存しました", ToastLevel.Success));
        }
        catch (Exception exception)
        {
            Log.Append(LogLevel.Error, $"保存に失敗しました: {exception.Message}");
            _dialogs.ShowError($"保存に失敗しました。\n{exception.Message}");
        }
    }

    /// <summary>ウィンドウを閉じてよいか(未保存確認)。View の Closing から呼ばれる。</summary>
    public bool ConfirmClose() =>
        !_store.IsDirty.CurrentValue ||
        _dialogs.ConfirmDiscardChanges(_store.Current.CurrentValue.Name);

    private void OpenSettings()
    {
        if (_dialogs.ShowSettingsDialog())
        {
            IsLightTheme.Value = _settings.Current.Theme == "Light";
            RunGestureText.Value = _settings.Current.RunGesture;
            Log.Append(LogLevel.Info,
                $"設定を更新しました(テーマ: {_settings.Current.Theme} / 実行: {_settings.Current.RunGesture})");
        }
    }

    private void AddRecentFile(string filePath)
    {
        _settings.Update(s => s.WithRecentFile(filePath));
        SyncRecentFiles();
    }

    private void SyncRecentFiles()
    {
        RecentFiles.Clear();
        foreach (var file in _settings.Current.RecentFiles)
        {
            RecentFiles.Add(file);
        }
    }

    /// <summary>プロジェクト差し替え時の共通リセット(結果・チャート・注釈・ツリー)。</summary>
    private void ResetWorkspace(CaeProjectData project, string? filePath)
    {
        _runner.Invalidate();
        _resultProject = null;
        _shownModalResult = null;
        IsResultStale.Value = false;
        IsModalResult.Value = false;
        ModeRows.Clear();
        SelectedMode.Value = null;
        FrfSeries.Clear();
        PathPlot.Value = null;
        HistogramValues.Value = null;
        AnnotationClearRequest.Value++;
        _pendingFit = true;
        _categoryFilter = null;

        // フロー誘導: 新規/読込直後は結果なし → 「モデル」タブへ戻し結果系パネルを隠す
        HasResult.Value = false;
        RibbonTabIndex.Value = RibbonTabModel;
        IsConvergenceVisible.Value = false;
        IsPathPlotVisible.Value = false;
        IsHistogramVisible.Value = false;
        IsFrfVisible.Value = false;
        IsModesVisible.Value = false;
        IsLegendVisible.Value = false;
        IsJobSnapshotMismatch.Value = false;

        _store.Replace(project, filePath);
    }

    // ================= モデルツリー+PropertyGrid 連動 =================

    private readonly List<TreeNode> _categoryNodes = [];

    private void BuildTree()
    {
        var geometry = new TreeNode { Name = "形状" };
        var load = new TreeNode { Name = "荷重" };
        var material = new TreeNode { Name = "材料" };
        var solver = new TreeNode { Name = "ソルバ" };
        _nodeCategories[geometry] = ProjectPropertyFactory.CategoryGeometry;
        _nodeCategories[load] = ProjectPropertyFactory.CategoryLoad;
        _nodeCategories[material] = ProjectPropertyFactory.CategoryMaterial;
        _nodeCategories[solver] = ProjectPropertyFactory.CategorySolver;
        _categoryNodes.AddRange([geometry, load, material, solver]);

        _rootNode = new TreeNode { Name = _store.Current.CurrentValue.Name, IsExpanded = true };
        _nodeCategories[_rootNode] = null;
        foreach (var child in _categoryNodes)
        {
            _rootNode.Children.Add(child);
        }

        TreeItems.Add(_rootNode);
    }

    /// <summary>SearchBox のフィルタ文字列でカテゴリノードを絞り込む。</summary>
    private void ApplyTreeFilter()
    {
        if (_rootNode is null)
        {
            return;
        }

        var filter = TreeFilter.Value;
        _rootNode.Children.Clear();
        foreach (var node in _categoryNodes)
        {
            if (string.IsNullOrWhiteSpace(filter) ||
                node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                _rootNode.Children.Add(node);
            }
        }
    }

    private void OnTreeSelectionChanged(IReadOnlyList<ITreeNode> selected)
    {
        var node = selected.OfType<TreeNode>().FirstOrDefault();
        _categoryFilter = node is not null ? _nodeCategories.GetValueOrDefault(node) : null;
        RebuildPropertyItems();
    }

    private void RebuildPropertyItems()
    {
        var items = new List<PropertyItem>(ProjectPropertyFactory.Build(
            _store.Current.CurrentValue, UpdateFromPropertyGrid, _categoryFilter));

        // 表示カテゴリ(プロジェクト入力ではなく VM の表示状態。ColorPropertyItem の実演)
        if (_categoryFilter is null)
        {
            var color = new ColorPropertyItem
            {
                Name = "パーツ色",
                Category = "表示",
                Value = PartColor.Value,
                IsAlphaEnabled = false,
                Description = "メッシュプレビュー(単色表示)のパーツ色",
            };
            color.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ColorPropertyItem.Value))
                {
                    PartColor.Value = color.Value;
                }
            };
            items.Add(color);
        }

        PropertyItems.Value = items;
    }

    /// <summary>
    /// PropertyGrid のアイテム編集 → 不変レコード差し替え。
    /// 編集中のアイテム列を再構築しない(フォーカス維持)ためフラグで区別し、
    /// 材料切替のように表示内容が変わる場合のみ再構築する。
    /// </summary>
    private void UpdateFromPropertyGrid(Func<CaeProjectData, CaeProjectData> mutate)
    {
        var before = _store.Current.CurrentValue;
        _updatingFromPropertyGrid = true;
        try
        {
            _store.Update(mutate);
        }
        finally
        {
            _updatingFromPropertyGrid = false;
        }

        if (!ReferenceEquals(before.Material, _store.Current.CurrentValue.Material))
        {
            RebuildPropertyItems();
        }
    }

    // ================= ヘルパ =================

    private static string DescribeMesh(CaeProjectData project) => project.Geometry switch
    {
        PlateWithHoleGeometry p => $"半径 {p.RadialDivisions} × 周 {p.AngularDivisions} 分割",
        CantileverPlateGeometry b => $"{b.DivisionsX} × {b.DivisionsY} 分割",
        _ => "",
    };

    private static string DescribeMeshCounts(Mesh2D mesh) =>
        string.Create(CultureInfo.InvariantCulture,
            $"節点 {mesh.NodeCount:N0} / 三角形 {mesh.TriangleCount:N0}");

    /// <summary>最大変位がモデル代表寸法の約 5% に見えるスケールを提案する。</summary>
    private static double SuggestDeformationScale(StaticResult result)
    {
        if (result.MaxDisplacement <= 0)
        {
            return 1.0;
        }

        var (minX, minY, maxX, maxY) = (double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
        var positions = result.Mesh.Positions;
        for (var node = 0; node < result.Mesh.NodeCount; node++)
        {
            minX = Math.Min(minX, positions[node * 2]);
            maxX = Math.Max(maxX, positions[node * 2]);
            minY = Math.Min(minY, positions[node * 2 + 1]);
            maxY = Math.Max(maxY, positions[node * 2 + 1]);
        }

        var size = Math.Max(maxX - minX, maxY - minY);
        var raw = 0.05 * size / result.MaxDisplacement;
        var magnitude = Math.Pow(10.0, Math.Floor(Math.Log10(raw)));
        return Math.Max(1.0, Math.Round(raw / magnitude) * magnitude);
    }

    /// <summary>
    /// モード形状(最大変位 1 mm に正規化済み)がモデル代表寸法の約 5% に見えるスケール。
    /// </summary>
    private static double SuggestModalScale(Mesh2D mesh)
    {
        var (minX, minY, maxX, maxY) = (double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
        for (var node = 0; node < mesh.NodeCount; node++)
        {
            minX = Math.Min(minX, mesh.Positions[node * 2]);
            maxX = Math.Max(maxX, mesh.Positions[node * 2]);
            minY = Math.Min(minY, mesh.Positions[node * 2 + 1]);
            maxY = Math.Max(maxY, mesh.Positions[node * 2 + 1]);
        }

        var size = Math.Max(maxX - minX, maxY - minY);
        return Math.Max(1.0, Math.Round(0.05 * size));
    }

    private T Register<T>(T disposable) where T : IDisposable
    {
        _disposables.Add(disposable);
        return disposable;
    }

    public void Dispose()
    {
        _toasts.Dispose();
        _disposables.Dispose();
    }
}

/// <summary>固有振動数テーブルの 1 行(理論値は片持ち板のみ、他は "-")。</summary>
public sealed record ModeRow(
    int Index, ModalMode Mode, string FrequencyText, string TheoryText, string ErrorText);

/// <summary>ジョブモニタ DataGrid の 1 行(IJobClient の JobInfo を表示用に整形)。</summary>
public sealed record JobRow(
    Guid Id,
    string Name,
    string AnalysisTypeText,
    string StateText,
    double Progress,
    string ElapsedText,
    string? ErrorMessage,
    bool CanCancel,
    bool CanLoadResult,
    bool CanResubmit)
{
    public static JobRow From(JobInfo job) => new(
        job.Id,
        job.Name,
        job.AnalysisType == AnalysisType.Modal ? "固有値" : "静解析",
        job.State switch
        {
            JobState.Queued => "待機中",
            JobState.Running => "実行中",
            JobState.Completed => "完了",
            JobState.Cancelled => "キャンセル",
            JobState.Failed => "失敗",
            _ => job.State.ToString(),
        },
        job.Progress,
        job.Elapsed.TotalSeconds < 60
            ? string.Create(CultureInfo.InvariantCulture, $"{job.Elapsed.TotalSeconds:0.0} s")
            : string.Create(CultureInfo.InvariantCulture, $"{job.Elapsed.TotalMinutes:0.0} min"),
        job.ErrorMessage,
        CanCancel: job.State is JobState.Queued or JobState.Running,
        CanLoadResult: job.State == JobState.Completed,
        CanResubmit: job.State is JobState.Completed or JobState.Failed or JobState.Cancelled);
}
