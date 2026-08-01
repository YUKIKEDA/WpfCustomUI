using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Numerics;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WpfCustomUI.Controls;
using WpfCustomUI.Controls.Theming;
using WpfCustomUI.Viewport3D.Rendering;

namespace WpfCustomUI.Viewport3D;

/// <summary>
/// D3D11 自作エンジンによる 3D ビューポートコントロール(spec 6.16 / 6.17)。
/// <para>
/// - 表示: <see cref="MeshSource"/> にバインドした <see cref="ViewportMesh"/> 群を
///   ライティング付き単色、または節点スカラー+<see cref="ColorScale"/> のコンターで描画する。
/// - カメラ: 中ボタンドラッグ=回転 / Shift+中ボタン=パン / ホイール=カーソル位置へズーム /
///   中ボタンダブルクリック=Fit(spec 6.16.4)。
/// - 選択: <see cref="PickMode"/> を設定すると左クリック=置換選択 / Ctrl+クリック=トグル /
///   左ドラッグ=矩形選択(可視のみ)。結果は <see cref="Selection"/> モデルに反映され、
///   ハイライト描画はコントロールが内蔵する(spec 6.17)。
/// - 視点: <see cref="SetStandardView"/> と右上のクリック式 ViewCube(補間アニメーション付き)。
/// - 描画はオンデマンド(変更・操作時のみ)。テーマ変更には <see cref="ThemeManager.ThemeChanged"/> で追従する。
/// - 表示経路はハードウェアなら D3DImage(エアスペース問題なし)、
///   WARP / D3D9 不可環境では WriteableBitmap へ自動フォールバックする。
/// </para>
/// </summary>
[TemplatePart(Name = PartImage, Type = typeof(Image))]
[TemplatePart(Name = PartTriadCanvas, Type = typeof(Canvas))]
[TemplatePart(Name = PartAnnotationCanvas, Type = typeof(Canvas))]
[TemplatePart(Name = PartViewCubeCanvas, Type = typeof(Canvas))]
public class WcuViewport : Control
{
    private const string PartImage = "PART_Image";
    private const string PartTriadCanvas = "PART_TriadCanvas";
    private const string PartAnnotationCanvas = "PART_AnnotationCanvas";
    private const string PartViewCubeCanvas = "PART_ViewCubeCanvas";
    private const double TriadArmLength = 36.0;
    private const double TriadMargin = 56.0;
    private const double RubberBandThresholdDip = 4.0;
    private const double ViewAnimationDurationMs = 150.0;
    private const double AnnotationLeaderLengthDip = 18.0;

    public static readonly DependencyProperty MeshSourceProperty =
        DependencyProperty.Register(
            nameof(MeshSource), typeof(IEnumerable), typeof(WcuViewport),
            new PropertyMetadata(null, OnMeshSourceChanged));

    public static readonly DependencyProperty ColorScaleProperty =
        DependencyProperty.Register(
            nameof(ColorScale), typeof(ColorScale), typeof(WcuViewport),
            new PropertyMetadata(null, OnColorScaleChanged));

    public static readonly DependencyProperty ShowContoursProperty =
        DependencyProperty.Register(
            nameof(ShowContours), typeof(bool), typeof(WcuViewport),
            new PropertyMetadata(true, OnVisualOptionChanged));

    public static readonly DependencyProperty ShowEdgesProperty =
        DependencyProperty.Register(
            nameof(ShowEdges), typeof(bool), typeof(WcuViewport),
            new PropertyMetadata(true, OnVisualOptionChanged));

    public static readonly DependencyProperty ProjectionProperty =
        DependencyProperty.Register(
            nameof(Projection), typeof(ViewportProjection), typeof(WcuViewport),
            new PropertyMetadata(ViewportProjection.Perspective, OnProjectionChanged));

    public static readonly DependencyProperty UpAxisProperty =
        DependencyProperty.Register(
            nameof(UpAxis), typeof(ViewportUpAxis), typeof(WcuViewport),
            new PropertyMetadata(ViewportUpAxis.ZUp, OnUpAxisChanged));

    public static readonly DependencyProperty ShowAxisTriadProperty =
        DependencyProperty.Register(
            nameof(ShowAxisTriad), typeof(bool), typeof(WcuViewport),
            new PropertyMetadata(true, OnVisualOptionChanged));

    public static readonly DependencyProperty ShowViewCubeProperty =
        DependencyProperty.Register(
            nameof(ShowViewCube), typeof(bool), typeof(WcuViewport),
            new PropertyMetadata(true, OnVisualOptionChanged));

    public static readonly DependencyProperty PickModeProperty =
        DependencyProperty.Register(
            nameof(PickMode), typeof(ViewportPickMode), typeof(WcuViewport),
            new PropertyMetadata(ViewportPickMode.None, OnPickModeChanged));

    public static readonly DependencyProperty DeformationScaleProperty =
        DependencyProperty.Register(
            nameof(DeformationScale), typeof(double), typeof(WcuViewport),
            new PropertyMetadata(1.0, OnVisualOptionChanged),
            v => double.IsFinite((double)v));

    public static readonly DependencyProperty IsDeformationAnimatedProperty =
        DependencyProperty.Register(
            nameof(IsDeformationAnimated), typeof(bool), typeof(WcuViewport),
            new PropertyMetadata(false, OnIsDeformationAnimatedChanged));

    public static readonly DependencyProperty DeformationAnimationPeriodProperty =
        DependencyProperty.Register(
            nameof(DeformationAnimationPeriod), typeof(TimeSpan), typeof(WcuViewport),
            new PropertyMetadata(TimeSpan.FromSeconds(1.0)),
            v => (TimeSpan)v > TimeSpan.Zero);

    public static readonly DependencyProperty ShowUndeformedWireframeProperty =
        DependencyProperty.Register(
            nameof(ShowUndeformedWireframe), typeof(bool), typeof(WcuViewport),
            new PropertyMetadata(false, OnVisualOptionChanged));

    public static readonly DependencyProperty SectionPlaneProperty =
        DependencyProperty.Register(
            nameof(SectionPlane), typeof(SectionPlane), typeof(WcuViewport),
            new PropertyMetadata(null, OnSectionPlaneChanged));

    public static readonly DependencyProperty ShowSectionPlaneIndicatorProperty =
        DependencyProperty.Register(
            nameof(ShowSectionPlaneIndicator), typeof(bool), typeof(WcuViewport),
            new PropertyMetadata(true, OnVisualOptionChanged));

    public static readonly DependencyProperty ShowGlyphsProperty =
        DependencyProperty.Register(
            nameof(ShowGlyphs), typeof(bool), typeof(WcuViewport),
            new PropertyMetadata(true, OnVisualOptionChanged));

    public static readonly DependencyProperty GlyphScaleProperty =
        DependencyProperty.Register(
            nameof(GlyphScale), typeof(double), typeof(WcuViewport),
            new PropertyMetadata(1.0, OnVisualOptionChanged),
            v => double.IsFinite((double)v) && (double)v >= 0.0);

    public static readonly DependencyProperty GlyphStrideProperty =
        DependencyProperty.Register(
            nameof(GlyphStride), typeof(int), typeof(WcuViewport),
            new PropertyMetadata(1, OnGlyphDataOptionChanged),
            v => (int)v >= 1);

    public static readonly DependencyProperty GlyphColorScaleProperty =
        DependencyProperty.Register(
            nameof(GlyphColorScale), typeof(ColorScale), typeof(WcuViewport),
            new PropertyMetadata(null, OnGlyphColorScaleChanged));

    public static readonly DependencyProperty EdgeExtractionLimitProperty =
        DependencyProperty.Register(
            nameof(EdgeExtractionLimit), typeof(int), typeof(WcuViewport),
            new PropertyMetadata(5_000_000, OnGeometryOptionChanged),
            v => (int)v >= 0);

    public static readonly DependencyProperty InteractiveLodThresholdProperty =
        DependencyProperty.Register(
            nameof(InteractiveLodThreshold), typeof(int), typeof(WcuViewport),
            new PropertyMetadata(5_000_000, OnGeometryOptionChanged),
            v => (int)v >= 0);

    private readonly List<(ViewportMesh Source, GpuMesh Gpu)> _gpuMeshes = [];
    private readonly HashSet<ViewportMesh> _displacementDirtyMeshes = [];
    private readonly List<RenderItem> _renderItems = [];
    private readonly List<GpuMesh> _visibleGpus = [];
    private readonly List<ViewportMesh> _visibleSources = [];
    private readonly Dictionary<ViewportMesh, GpuSelectionMesh> _selectionGpu = [];

    private Image? _image;
    private Canvas? _triadCanvas;
    private Canvas? _viewCubeCanvas;
    private ViewCubeOverlay? _viewCube;
    private ViewportRenderer? _renderer;
    private D3DImage? _d3dImage;
    private WriteableBitmap? _softwareBitmap;
    private nint _lastBackBuffer;

    private bool _renderQueued;
    private bool _geometryDirty = true;
    private bool _colorMapDirty = true;
    private bool _selectionDirty = true;
    private bool _glyphDirty = true;
    private bool _hasAutoFitted;
    private bool _renderBroken;
    private int _consecutiveFailures;

    private Bounds3D _localBounds = Bounds3D.Empty;
    private double _originX;
    private double _originY;
    private double _originZ;

    // マウス操作状態
    private Point _lastMousePosition;
    private bool _isOrbiting;
    private bool _isPanning;
    private bool _isPicking;
    private bool _isRubberBanding;
    private Point _pickStart;

    // 視点補間アニメーション
    private bool _isAnimatingView;
    private DateTime _animationStart;
    private double _animStartYaw;
    private double _animStartPitch;
    private double _animDeltaYaw;
    private double _animDeltaPitch;

    // モード振動アニメーション(spec 6.18.3)
    private bool _isDeformationTickAttached;
    private DateTime _deformationAnimationStart;
    private float _lastEffectiveDeformationScale = 1.0f;

    // 統計 API 用の計測値(spec 6.22.5)
    private TimeSpan _lastGeometryBuildTime;
    private TimeSpan _lastRenderTime;

    // 操作中 LOD(spec 6.23.3): カメラ操作・振動アニメの間は LOD チャンクで描画し、
    // 操作が止まって一定時間後にフル解像度へ戻す
    private static readonly TimeSpan LodRestoreDelay = TimeSpan.FromMilliseconds(300);
    private bool _lodRequested;
    private bool _hasAnyLod;
    private DispatcherTimer? _lodRestoreTimer;

    // 軸トライアッド+ラバーバンドの WPF オーバーレイ要素
    private readonly Line[] _triadLines = new Line[3];
    private readonly TextBlock[] _triadLabels = new TextBlock[3];
    private Rectangle? _rubberBand;

    // 注釈オーバーレイ(spec 6.20.4)
    private readonly List<AnnotationVisual> _annotationVisuals = [];
    private readonly List<ViewportAnnotation> _hookedAnnotations = [];
    private Canvas? _annotationCanvas;

    static WcuViewport()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(WcuViewport), new FrameworkPropertyMetadata(typeof(WcuViewport)));
    }

    public WcuViewport()
    {
        Camera = new ViewportCamera();
        Camera.Changed += (_, _) =>
        {
            // カメラ操作(回転/パン/ズーム/視点アニメ)は操作中 LOD の発動条件(spec 6.23.3)
            NotifyInteractiveChange();
            InvalidateViewport();
        };

        Selection = new ViewportSelection();
        Selection.Changed += OnSelectionModelChanged;

        Annotations = [];
        Annotations.CollectionChanged += OnAnnotationsChanged;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>選択内容が変わったときに発火する(<see cref="Selection"/> の Changed の転送)。</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// プローブ(<see cref="ViewportPickMode.Probe"/>)のクリックがメッシュにヒットしたときに発火する
    /// (spec 6.20.2)。<see cref="ProbePickedEventArgs.Handled"/> を true にしなければ、
    /// 既定書式(または <see cref="ProbeLabelFormatter"/>)のラベルで注釈が自動追加される。
    /// 空クリック(背景)では発火しない。
    /// </summary>
    public event EventHandler<ProbePickedEventArgs>? ProbePicked;

    /// <summary>
    /// プローブ注釈の既定ラベル書式を差し替える(単位付き表示など)。null なら組込み書式
    /// (スカラーありは「N{節点}: {値:G4}」、なしは節点番号+モデル座標)。
    /// </summary>
    public Func<ProbeResult, string>? ProbeLabelFormatter { get; set; }

    /// <summary>
    /// ビューポートに表示する注釈のコレクション(spec 6.20.3)。プローブが自動追加するほか、
    /// アプリから直接追加・削除できる(全削除は Clear())。描画はコントロールが内蔵し、
    /// 節点バインドの注釈は変形表示・断面カット・メッシュ可視状態に毎フレーム追従する。
    /// </summary>
    public ObservableCollection<ViewportAnnotation> Annotations { get; }

    /// <summary>表示するメッシュのコレクション(通常 ObservableCollection&lt;ViewportMesh&gt;)。</summary>
    public IEnumerable? MeshSource
    {
        get => (IEnumerable?)GetValue(MeshSourceProperty);
        set => SetValue(MeshSourceProperty, value);
    }

    /// <summary>コンター表示に使う値→色変換。ColorMapLegend と同一インスタンスを共有できる。</summary>
    public ColorScale? ColorScale
    {
        get => (ColorScale?)GetValue(ColorScaleProperty);
        set => SetValue(ColorScaleProperty, value);
    }

    /// <summary>コンター表示の有効/無効(無効時はパーツ単色)。</summary>
    public bool ShowContours
    {
        get => (bool)GetValue(ShowContoursProperty);
        set => SetValue(ShowContoursProperty, value);
    }

    /// <summary>エッジ(ワイヤフレーム)重畳の全体ゲート(パーツ毎の ShowEdges と AND)。</summary>
    public bool ShowEdges
    {
        get => (bool)GetValue(ShowEdgesProperty);
        set => SetValue(ShowEdgesProperty, value);
    }

    public ViewportProjection Projection
    {
        get => (ViewportProjection)GetValue(ProjectionProperty);
        set => SetValue(ProjectionProperty, value);
    }

    public ViewportUpAxis UpAxis
    {
        get => (ViewportUpAxis)GetValue(UpAxisProperty);
        set => SetValue(UpAxisProperty, value);
    }

    /// <summary>左下隅の XYZ 軸トライアッド表示。</summary>
    public bool ShowAxisTriad
    {
        get => (bool)GetValue(ShowAxisTriadProperty);
        set => SetValue(ShowAxisTriadProperty, value);
    }

    /// <summary>右上隅のクリック式 ViewCube 表示(spec 6.17.5)。</summary>
    public bool ShowViewCube
    {
        get => (bool)GetValue(ShowViewCubeProperty);
        set => SetValue(ShowViewCubeProperty, value);
    }

    /// <summary>
    /// 左ボタンピッキングの選択粒度(spec 6.17.2)。None(既定)では左ボタンは何もしない。
    /// </summary>
    public ViewportPickMode PickMode
    {
        get => (ViewportPickMode)GetValue(PickModeProperty);
        set => SetValue(PickModeProperty, value);
    }

    /// <summary>
    /// 変形表示のスケール係数(spec 6.18)。各メッシュの <see cref="ViewportMesh.Displacements"/> に
    /// この値を掛けた位置で描画する(GPU 頂点シェーダで適用)。0 で非変形表示。
    /// 推奨値は <see cref="GetSuggestedDeformationScale"/> で得られる。
    /// </summary>
    public double DeformationScale
    {
        get => (double)GetValue(DeformationScaleProperty);
        set => SetValue(DeformationScaleProperty, value);
    }

    /// <summary>
    /// モード振動アニメーション(spec 6.18.3)。true の間、表示上の変形量が
    /// DeformationScale × sin(2πt/T) で連続的に振動する(固有モードの可視化用)。
    /// オンデマンド描画の例外として毎フレーム描画になる点に注意。
    /// </summary>
    public bool IsDeformationAnimated
    {
        get => (bool)GetValue(IsDeformationAnimatedProperty);
        set => SetValue(IsDeformationAnimatedProperty, value);
    }

    /// <summary>振動アニメーションの周期(既定 1 秒)。</summary>
    public TimeSpan DeformationAnimationPeriod
    {
        get => (TimeSpan)GetValue(DeformationAnimationPeriodProperty);
        set => SetValue(DeformationAnimationPeriodProperty, value);
    }

    /// <summary>非変形形状のワイヤフレームを半透明で重畳表示する(spec 6.18.4)。</summary>
    public bool ShowUndeformedWireframe
    {
        get => (bool)GetValue(ShowUndeformedWireframeProperty);
        set => SetValue(ShowUndeformedWireframeProperty, value);
    }

    /// <summary>
    /// 断面カットのクリッピング平面(spec 6.19)。null(既定)でカット無効。
    /// 平面の法線側が表示されて残り、切り口は開放(中空に見える)。
    /// <see cref="ViewportMesh.IsClippable"/> = false のメッシュはカットされないため、
    /// アプリが計算した断面スライスをそこに重ねられる。
    /// プロパティ変更(オフセットのドラッグ等)には自動追従する。
    /// </summary>
    public SectionPlane? SectionPlane
    {
        get => (SectionPlane?)GetValue(SectionPlaneProperty);
        set => SetValue(SectionPlaneProperty, value);
    }

    /// <summary>
    /// 断面平面のインジケータ(半透明クワッド+輪郭線)を表示する(spec 6.19.4)。
    /// <see cref="SectionPlane"/> が null のときは何も描かれない。既定 true。
    /// </summary>
    public bool ShowSectionPlaneIndicator
    {
        get => (bool)GetValue(ShowSectionPlaneIndicatorProperty);
        set => SetValue(ShowSectionPlaneIndicatorProperty, value);
    }

    /// <summary>カメラ。アプリから直接操作(視点の保存/復元など)できる。</summary>
    public ViewportCamera Camera { get; }

    /// <summary>
    /// 選択状態モデル(spec 6.17.3)。ピッキング操作の結果が反映され、
    /// プログラムから操作(ModelTree 連動など)してもハイライトは自動追従する。
    /// </summary>
    public ViewportSelection Selection { get; }

    /// <summary>WARP(ソフトウェアラスタライザ)で動作しているか。</summary>
    public bool IsSoftwareRendering => _renderer?.IsSoftwareRendering ?? false;

    /// <summary>全メッシュが収まるようにカメラを合わせる。</summary>
    public void FitToView()
    {
        EnsureGeometry();
        if (!_localBounds.IsEmpty)
        {
            Camera.FitToBounds(_localBounds);
        }
    }

    /// <summary>
    /// ベクトルグリフ(矢印)の表示(spec 6.21)。<see cref="ViewportMesh.VectorValues"/> を持つ
    /// メッシュだけが対象。既定 true(ベクトル場がなければ何も描かれない)。
    /// </summary>
    public bool ShowGlyphs
    {
        get => (bool)GetValue(ShowGlyphsProperty);
        set => SetValue(ShowGlyphsProperty, value);
    }

    /// <summary>
    /// グリフスケール(spec 6.21.4)。矢印の長さ = |v| × GlyphScale(太さも比例)。
    /// 推奨値は <see cref="GetSuggestedGlyphScale"/> で得られる。0 で非表示相当。
    /// </summary>
    public double GlyphScale
    {
        get => (double)GetValue(GlyphScaleProperty);
        set => SetValue(GlyphScaleProperty, value);
    }

    /// <summary>
    /// グリフの間引き(spec 6.21.4)。n 節点ごとに 1 本の矢印を立てる(既定 1 = 全節点)。
    /// 大規模メッシュでの団子化を回避する。
    /// </summary>
    public int GlyphStride
    {
        get => (int)GetValue(GlyphStrideProperty);
        set => SetValue(GlyphStrideProperty, value);
    }

    /// <summary>
    /// グリフ配色用のカラースケール(spec 6.21.5)。|v| をこのスケールで色に変換する。
    /// コンター用 <see cref="ColorScale"/> とは独立(表示中の物理量・値域が異なるため)。
    /// <see cref="Controls.ColorMapLegend"/> に同じインスタンスを渡せば凡例も表示できる。
    /// null のときはアクセント系の単色フォールバック。
    /// </summary>
    public ColorScale? GlyphColorScale
    {
        get => (ColorScale?)GetValue(GlyphColorScaleProperty);
        set => SetValue(GlyphColorScaleProperty, value);
    }

    /// <summary>
    /// エッジ抽出を行う三角形数の上限(spec 6.22.4、既定 500万)。超過するメッシュは
    /// エッジ抽出自体をスキップし、<see cref="ViewportMesh.ShowEdges"/> や
    /// <see cref="ShowUndeformedWireframe"/> は無効になる(スキップの有無は
    /// <see cref="GetStatistics"/> で確認できる)。エッジは三角形数の約 1.5 倍のラインを
    /// 生むため、大規模メッシュでのメモリ爆発を防ぐ安全網。int.MaxValue で実質無制限。
    /// </summary>
    public int EdgeExtractionLimit
    {
        get => (int)GetValue(EdgeExtractionLimitProperty);
        set => SetValue(EdgeExtractionLimitProperty, value);
    }

    /// <summary>
    /// 操作中 LOD を構築する三角形数の閾値(spec 6.23.3、既定 500万)。超過するメッシュは
    /// グリッドクラスタリングで約 1/20 に間引いた LOD メッシュをロード時に併せて構築し、
    /// カメラ操作(回転/パン/ズーム/視点アニメ)と振動アニメーションの間は LOD で描画する。
    /// 操作が止まると自動でフル解像度に戻る。LOD 中はエッジ/グリフ/非変形重畳が非表示になるが、
    /// コンター・変形・断面カットは LOD にも適用される。ピック/プローブ/選択は常にフルメッシュ。
    /// int.MaxValue で無効(常にフル描画)。
    /// </summary>
    public int InteractiveLodThreshold
    {
        get => (int)GetValue(InteractiveLodThresholdProperty);
        set => SetValue(InteractiveLodThresholdProperty, value);
    }

    /// <summary>
    /// 現在のシーンの統計スナップショットを返す(spec 6.22.5)。
    /// 用途: アプリのステータスバー表示・ベンチ計測・性能回帰の検証。
    /// 頻繁に変わる値のため DP ではなくメソッド(バインディング更新が描画毎に走るのを避ける)。
    /// ジオメトリ未構築(初回描画前)の間はゼロ値を返す。
    /// </summary>
    public ViewportStatistics GetStatistics()
    {
        long triangles = 0;
        long vertices = 0;
        long lodTriangles = 0;
        var chunks = 0;
        var edgeSkipped = 0;
        foreach (var (_, gpu) in _gpuMeshes)
        {
            triangles += gpu.TriangleCount;
            vertices += gpu.VertexCount;
            lodTriangles += gpu.LodTriangleCount;
            chunks += gpu.Chunks.Count;
            if (gpu.EdgesSkipped)
            {
                edgeSkipped++;
            }
        }

        return new ViewportStatistics(
            triangles, vertices, _gpuMeshes.Count, chunks, edgeSkipped,
            lodTriangles,
            _renderer?.LastFrameUsedLod ?? false,
            _renderer?.LastDrawnChunkCount ?? 0,
            _lastGeometryBuildTime, _lastRenderTime);
    }

    // ================= 操作中 LOD(spec 6.23.3) =================

    /// <summary>
    /// カメラ操作・振動アニメーションが起きたことを通知する。LOD を持つメッシュがあるときは
    /// LOD 描画へ切り替え、操作が <see cref="LodRestoreDelay"/> の間止まったら
    /// フル解像度の再描画を予約する。
    /// </summary>
    private void NotifyInteractiveChange()
    {
        if (!_hasAnyLod)
        {
            return;
        }

        _lodRequested = true;

        if (_lodRestoreTimer is null)
        {
            _lodRestoreTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = LodRestoreDelay,
            };
            _lodRestoreTimer.Tick += (_, _) =>
            {
                _lodRestoreTimer!.Stop();
                _lodRequested = false;
                InvalidateViewport(); // フル解像度で描き直す
            };
        }

        // 操作が続く限りタイマーを巻き戻す(最後の操作から一定時間後に復帰)
        _lodRestoreTimer.Stop();
        _lodRestoreTimer.Start();
    }

    /// <summary>
    /// 「最大変位がモデル代表寸法(境界ボックス対角長)の 5% に見える」推奨変形スケールを返す
    /// (spec 6.18.4)。変位を持つメッシュがない場合は 1.0。適用はアプリの責務
    /// (<c>viewport.DeformationScale = viewport.GetSuggestedDeformationScale()</c>)。
    /// </summary>
    public double GetSuggestedDeformationScale(double targetFraction = ViewportDeformation.DefaultTargetFraction)
    {
        var meshes = MeshSource?.OfType<ViewportMesh>() ?? [];
        var bounds = Bounds3D.Empty;
        var maxDisplacement = 0.0;
        foreach (var mesh in meshes)
        {
            bounds = bounds.Union(ViewportGeometry.ComputeBounds(mesh.Positions));
            maxDisplacement = Math.Max(
                maxDisplacement, ViewportDeformation.GetMaxDisplacementMagnitude(mesh.Displacements));
        }

        var diagonal = bounds.IsEmpty
            ? 0.0
            : Math.Sqrt(
                (bounds.MaxX - bounds.MinX) * (bounds.MaxX - bounds.MinX)
                + (bounds.MaxY - bounds.MinY) * (bounds.MaxY - bounds.MinY)
                + (bounds.MaxZ - bounds.MinZ) * (bounds.MaxZ - bounds.MinZ));

        return ViewportDeformation.ComputeSuggestedScale(maxDisplacement, diagonal, targetFraction);
    }

    /// <summary>
    /// 「最大ベクトル長の矢印がモデル代表寸法(境界ボックス対角長)の 5% に見える」推奨グリフ
    /// スケールを返す(spec 6.21.4、<see cref="GetSuggestedDeformationScale"/> と同型)。
    /// ベクトル場を持つメッシュがない場合は 1.0。適用はアプリの責務
    /// (<c>viewport.GlyphScale = viewport.GetSuggestedGlyphScale()</c>)。
    /// </summary>
    public double GetSuggestedGlyphScale(double targetFraction = ViewportDeformation.DefaultTargetFraction)
    {
        var meshes = MeshSource?.OfType<ViewportMesh>() ?? [];
        var bounds = Bounds3D.Empty;
        var maxMagnitude = 0.0;
        foreach (var mesh in meshes)
        {
            bounds = bounds.Union(ViewportGeometry.ComputeBounds(mesh.Positions));
            maxMagnitude = Math.Max(
                maxMagnitude, ViewportDeformation.GetMaxDisplacementMagnitude(mesh.VectorValues));
        }

        var diagonal = bounds.IsEmpty
            ? 0.0
            : Math.Sqrt(
                (bounds.MaxX - bounds.MinX) * (bounds.MaxX - bounds.MinX)
                + (bounds.MaxY - bounds.MinY) * (bounds.MaxY - bounds.MinY)
                + (bounds.MaxZ - bounds.MinZ) * (bounds.MaxZ - bounds.MinZ));

        return ViewportDeformation.ComputeSuggestedScale(maxMagnitude, diagonal, targetFraction);
    }

    /// <summary>
    /// 標準視点へ切り替える(spec 6.17.5)。既定では短い補間アニメーション(150ms)で
    /// 視点を移動し、空間把握を保つ。注視点と距離は変えない。
    /// </summary>
    public void SetStandardView(ViewportStandardView view, bool animate = true)
    {
        var (yaw, pitch) = ViewportCamera.GetStandardViewAngles(view, UpAxis);
        AnimateOrientationTo(yaw, pitch, animate);
    }

    private void AnimateOrientationTo(double targetYaw, double targetPitch, bool animate)
    {
        StopViewAnimation();

        if (!animate || !IsLoaded)
        {
            Camera.SetOrientation(targetYaw, targetPitch);
            return;
        }

        _animStartYaw = Camera.Yaw;
        _animStartPitch = Camera.Pitch;
        _animDeltaYaw = NormalizeSignedAngle(targetYaw - _animStartYaw); // 最短経路
        _animDeltaPitch = targetPitch - _animStartPitch;
        _animationStart = DateTime.UtcNow;
        _isAnimatingView = true;
        CompositionTarget.Rendering += OnViewAnimationTick;
    }

    private void OnViewAnimationTick(object? sender, EventArgs e)
    {
        var t = (DateTime.UtcNow - _animationStart).TotalMilliseconds / ViewAnimationDurationMs;
        if (t >= 1.0)
        {
            Camera.SetOrientation(_animStartYaw + _animDeltaYaw, _animStartPitch + _animDeltaPitch);
            StopViewAnimation();
            return;
        }

        var eased = 1.0 - Math.Pow(1.0 - t, 3.0); // ease-out cubic
        Camera.SetOrientation(_animStartYaw + _animDeltaYaw * eased, _animStartPitch + _animDeltaPitch * eased);
    }

    private void StopViewAnimation()
    {
        if (_isAnimatingView)
        {
            _isAnimatingView = false;
            CompositionTarget.Rendering -= OnViewAnimationTick;
        }
    }

    // ================= モード振動アニメーション(spec 6.18.3) =================

    private static void OnIsDeformationAnimatedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((WcuViewport)d).UpdateDeformationAnimation();

    private void UpdateDeformationAnimation()
    {
        var shouldRun = IsDeformationAnimated && IsLoaded;
        if (shouldRun && !_isDeformationTickAttached)
        {
            _deformationAnimationStart = DateTime.UtcNow;
            _isDeformationTickAttached = true;
            CompositionTarget.Rendering += OnDeformationAnimationTick;
        }
        else if (!shouldRun && _isDeformationTickAttached)
        {
            _isDeformationTickAttached = false;
            CompositionTarget.Rendering -= OnDeformationAnimationTick;
        }

        // 停止時は静的な変形表示(係数 1)へ戻す
        InvalidateViewport();
    }

    private void OnDeformationAnimationTick(object? sender, EventArgs e)
    {
        // 振動アニメは毎フレーム描画になるため、大規模メッシュでは LOD で描く(spec 6.23.3)
        NotifyInteractiveChange();
        InvalidateViewport();
    }

    /// <summary>現在の実効グリフスケール(ShowGlyphs=false は 0 = 描画スキップ)。</summary>
    private float GetEffectiveGlyphScale() => ShowGlyphs ? (float)GlyphScale : 0.0f;

    /// <summary>現在の実効変形スケール(振動アニメの正弦係数込み)を求める。</summary>
    private float GetEffectiveDeformationScale()
    {
        var scale = DeformationScale;
        if (_isDeformationTickAttached)
        {
            scale *= ViewportDeformation.GetAnimationFactor(
                (DateTime.UtcNow - _deformationAnimationStart).TotalSeconds,
                DeformationAnimationPeriod.TotalSeconds);
        }

        return (float)scale;
    }

    /// <summary>角度差を [-π, π] へ正規化する(Yaw 補間の最短経路用)。</summary>
    private static double NormalizeSignedAngle(double angle)
    {
        var twoPi = 2.0 * Math.PI;
        angle %= twoPi;
        return angle switch
        {
            > Math.PI => angle - twoPi,
            < -Math.PI => angle + twoPi,
            _ => angle,
        };
    }

    /// <summary>UI テスト自動化からビューポート領域を特定できるようにする。</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new WcuViewportAutomationPeer(this);

    private sealed class WcuViewportAutomationPeer(WcuViewport owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(WcuViewport);

        protected override AutomationControlType GetAutomationControlTypeCore() =>
            AutomationControlType.Pane;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _image = GetTemplateChild(PartImage) as Image;
        _triadCanvas = GetTemplateChild(PartTriadCanvas) as Canvas;
        _annotationCanvas = GetTemplateChild(PartAnnotationCanvas) as Canvas;
        _viewCubeCanvas = GetTemplateChild(PartViewCubeCanvas) as Canvas;
        SetupTriadOverlay();
        RebuildAnnotationVisuals();
        SetupViewCube();
        InvalidateViewport();
    }

    private void SetupViewCube()
    {
        if (_viewCubeCanvas is null)
        {
            _viewCube = null;
            return;
        }

        _viewCubeCanvas.Children.Clear();
        _viewCube = new ViewCubeOverlay(_viewCubeCanvas);
        _viewCube.OrientationRequested += (yaw, pitch) => AnimateOrientationTo(yaw, pitch, animate: true);
    }

    // ================= 描画スケジューリング =================

    /// <summary>再描画を要求する(Dispatcher でまとめて 1 回だけ実行される)。</summary>
    public void InvalidateViewport()
    {
        if (_renderQueued)
        {
            return;
        }

        _renderQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _renderQueued = false;
            RenderFrame();
        });
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateViewport();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThemeManager.ThemeChanged += OnThemeChanged;
        _renderBroken = false;
        UpdateDeformationAnimation();
        InvalidateViewport();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        StopViewAnimation();
        UpdateDeformationAnimation();
        CancelPicking();
        _lodRestoreTimer?.Stop();
        _lodRequested = false;
        ReleaseRenderer();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        // グリフの単色フォールバックはアクセント色をインスタンスに焼き込むため作り直す
        _glyphDirty = true;
        InvalidateViewport();
    }

    private void RenderFrame()
    {
        if (!IsLoaded || _image is null || _renderBroken)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var pixelWidth = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        var pixelHeight = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return;
        }

        try
        {
            _renderer ??= new ViewportRenderer();
            var sizeChanged = pixelWidth != _renderer.Width || pixelHeight != _renderer.Height;
            _renderer.Resize(pixelWidth, pixelHeight);

            EnsureGeometry();
            EnsureDisplacements();
            EnsureGlyphs();
            EnsureColorMap();
            EnsureSelectionBuffers();
            BuildRenderLists();

            var aspect = (double)pixelWidth / pixelHeight;
            var viewProj = Camera.GetViewMatrix() * Camera.GetProjectionMatrix(aspect);
            var contour = BuildContourSettings();

            var background = GetTokenColor("Wcu.Color.Surface.Window", Color.FromRgb(0x1E, 0x1E, 0x1E));
            var edgeColor = GetTokenColor("Wcu.Color.Text.Muted", Color.FromRgb(0xC5, 0xC5, 0xC5));
            var accent = GetTokenColor("Wcu.Color.Accent.Default", Color.FromRgb(0x00, 0x7A, 0xCC));
            var highlightColor = ToVector4(accent, 0.55);
            var nodeColor = ToVector4(accent, 1.0);
            var pointSize = (float)(7.0 * dpi.DpiScaleX);

            // ピック(PickPixel 等)が直前の描画と同じ変形量を使えるよう保存する
            _lastEffectiveDeformationScale = GetEffectiveDeformationScale();

            // 断面カット(spec 6.19): クリップ係数とインジケータ頂点を組み立てる
            var clipPlane = GetCurrentClipPlane();
            float[]? sectionIndicator = null;
            if (ShowSectionPlaneIndicator && clipPlane != ViewportSection.DisabledClip)
            {
                sectionIndicator = ViewportSection.BuildIndicatorVertices(
                    clipPlane, (float)_localBounds.Radius);
            }

            var sectionFill = ToVector4(accent, 0.10);
            var sectionLine = ToVector4(accent, 0.70);

            // 描画時間の計測(spec 6.22.5)。CPU 側計測で、コマンド発行+Flush
            //(ソフトウェア経路では CPU 読み戻しも)を含む
            var renderTimer = System.Diagnostics.Stopwatch.StartNew();
            if (_renderer.CanUseD3DImage)
            {
                PresentViaD3DImage(sizeChanged, viewProj, contour, background, edgeColor,
                    highlightColor, nodeColor, pointSize, clipPlane, sectionIndicator, sectionFill, sectionLine);
            }
            else
            {
                PresentViaWriteableBitmap(sizeChanged, viewProj, contour, background, edgeColor,
                    highlightColor, nodeColor, pointSize, clipPlane, sectionIndicator, sectionFill, sectionLine);
            }

            _lastRenderTime = renderTimer.Elapsed;

            UpdateTriadOverlay();
            UpdateAnnotationOverlay(in viewProj, pixelWidth, pixelHeight, dpi.DpiScaleX, clipPlane);
            _viewCube?.Update(Camera, ActualWidth, ActualHeight, ShowViewCube);
            _consecutiveFailures = 0;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // デバイスロスト等。壊れたレンダラーを破棄し、次の描画要求で再作成を試みる。
            // 連続失敗した場合は無限ループを避けるため描画を止める。
            ReleaseRenderer();
            if (++_consecutiveFailures >= 2)
            {
                _renderBroken = true;
            }
            else
            {
                InvalidateViewport();
            }
        }
    }

    private void PresentViaD3DImage(
        bool sizeChanged, Matrix4x4 viewProj, ContourSettings contour, Color background, Color edgeColor,
        Vector4 highlightColor, Vector4 nodeColor, float pointSize,
        Vector4 clipPlane, float[]? sectionIndicator, Vector4 sectionFill, Vector4 sectionLine)
    {
        if (_d3dImage is null)
        {
            _d3dImage = new D3DImage();
            _d3dImage.IsFrontBufferAvailableChanged += (_, _) =>
            {
                // フロントバッファ喪失から復帰したときは SetBackBuffer を張り直さないと
                // 以後の描画が画面に反映されない(D3DImage の定石)。大規模シーンの
                // テーマ切替などで WPF がフロントバッファを一時的に手放すことがある
                _lastBackBuffer = 0;
                InvalidateViewport();
            };
            _softwareBitmap = null;
            _lastBackBuffer = 0;
        }

        if (!ReferenceEquals(_image!.Source, _d3dImage))
        {
            _image.Source = _d3dImage;
        }

        _d3dImage.Lock();
        try
        {
            var surface = _renderer!.BackBufferSurface;
            if (sizeChanged || surface != _lastBackBuffer)
            {
                _d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, surface, enableSoftwareFallback: true);
                _lastBackBuffer = surface;
            }

            _renderer.Render(
                _renderItems, in viewProj, Camera.GetEyeDirection(),
                ToVector4(background, 1.0), in contour, ShowContours, ToVector4(edgeColor, 1.0),
                highlightColor, nodeColor, pointSize,
                _lastEffectiveDeformationScale, GetEffectiveGlyphScale(),
                ShowUndeformedWireframe, ToVector4(edgeColor, 0.35),
                clipPlane, sectionIndicator, sectionFill, sectionLine,
                useLod: _lodRequested);

            _d3dImage.AddDirtyRect(new Int32Rect(0, 0, _renderer.Width, _renderer.Height));
        }
        finally
        {
            _d3dImage.Unlock();
        }
    }

    private void PresentViaWriteableBitmap(
        bool sizeChanged, Matrix4x4 viewProj, ContourSettings contour, Color background, Color edgeColor,
        Vector4 highlightColor, Vector4 nodeColor, float pointSize,
        Vector4 clipPlane, float[]? sectionIndicator, Vector4 sectionFill, Vector4 sectionLine)
    {
        _renderer!.Render(
            _renderItems, in viewProj, Camera.GetEyeDirection(),
            ToVector4(background, 1.0), in contour, ShowContours, ToVector4(edgeColor, 1.0),
            highlightColor, nodeColor, pointSize,
            _lastEffectiveDeformationScale, GetEffectiveGlyphScale(),
            ShowUndeformedWireframe, ToVector4(edgeColor, 0.35),
            clipPlane, sectionIndicator, sectionFill, sectionLine,
            useLod: _lodRequested);

        if (_softwareBitmap is null || sizeChanged)
        {
            _softwareBitmap = new WriteableBitmap(
                _renderer.Width, _renderer.Height, 96, 96, PixelFormats.Bgra32, null);
            _d3dImage = null;
        }

        if (!ReferenceEquals(_image!.Source, _softwareBitmap))
        {
            _image.Source = _softwareBitmap;
        }

        _softwareBitmap.Lock();
        try
        {
            _renderer.ReadPixels(_softwareBitmap.BackBuffer, _softwareBitmap.BackBufferStride);
            _softwareBitmap.AddDirtyRect(new Int32Rect(0, 0, _renderer.Width, _renderer.Height));
        }
        finally
        {
            _softwareBitmap.Unlock();
        }
    }

    // ================= ジオメトリ / カラーマップ同期 =================

    private void EnsureGeometry()
    {
        // レンダラー未作成時は次の RenderFrame で再試行される
        if (_renderer is null || !_geometryDirty)
        {
            return;
        }

        var buildTimer = System.Diagnostics.Stopwatch.StartNew();

        foreach (var (_, gpu) in _gpuMeshes)
        {
            gpu.Dispose();
        }

        _gpuMeshes.Clear();
        _displacementDirtyMeshes.Clear(); // 再構築で最新の変位が取り込まれる
        _geometryDirty = false;
        _glyphDirty = true; // GpuMesh 再作成でインスタンスバッファも失われる

        var meshes = MeshSource?.OfType<ViewportMesh>().ToList() ?? [];

        // MeshSource から消えたパーツの選択を掃除し、ジオメトリ差し替えに追従して
        // 選択バッファも作り直す
        Selection.PruneTo(meshes);
        _selectionDirty = true;

        var bounds = Bounds3D.Empty;
        var meshBounds = new List<Bounds3D>(meshes.Count);
        foreach (var mesh in meshes)
        {
            var b = ViewportGeometry.ComputeBounds(mesh.Positions);
            meshBounds.Add(b);
            bounds = bounds.Union(b);
        }

        if (bounds.IsEmpty)
        {
            _localBounds = Bounds3D.Empty;
            _hasAnyLod = false;
            _lastGeometryBuildTime = buildTimer.Elapsed;
            return;
        }

        // シーン中心で再センタリング(spec 6.16.3: 大座標対策)
        var (ox, oy, oz) = (bounds.CenterX, bounds.CenterY, bounds.CenterZ);
        (_originX, _originY, _originZ) = (ox, oy, oz);
        _localBounds = new Bounds3D(
            bounds.MinX - ox, bounds.MinY - oy, bounds.MinZ - oz,
            bounds.MaxX - ox, bounds.MaxY - oy, bounds.MaxZ - oz);

        for (var i = 0; i < meshes.Count; i++)
        {
            var gpu = _renderer.CreateMesh(
                meshes[i], ox, oy, oz, EdgeExtractionLimit, InteractiveLodThreshold, meshBounds[i]);
            if (gpu is not null)
            {
                _gpuMeshes.Add((meshes[i], gpu));
            }
        }

        _hasAnyLod = _gpuMeshes.Any(pair => pair.Gpu.HasLod);
        if (!_hasAnyLod)
        {
            _lodRequested = false;
            _lodRestoreTimer?.Stop();
        }

        _lastGeometryBuildTime = buildTimer.Elapsed;

        if (!_hasAutoFitted)
        {
            _hasAutoFitted = true;
            Camera.FitToBounds(_localBounds);
        }
    }

    /// <summary>
    /// Displacements が差し替えられたメッシュの変位バッファだけを更新する(spec 6.18.2)。
    /// ジオメトリ本体(座標・法線・スカラー)は再構築しないため、過渡再生の毎フレーム更新に耐える。
    /// </summary>
    private void EnsureDisplacements()
    {
        if (_displacementDirtyMeshes.Count == 0 || _renderer is null)
        {
            return;
        }

        foreach (var (source, gpu) in _gpuMeshes)
        {
            if (_displacementDirtyMeshes.Contains(source))
            {
                _renderer.UpdateMeshDisplacements(gpu, source.Displacements);
            }
        }

        _displacementDirtyMeshes.Clear();
    }

    /// <summary>
    /// グリフのインスタンスバッファをメッシュのベクトル場と同期する(spec 6.21)。
    /// VectorValues / Displacements / GlyphStride / GlyphColorScale / テーマ変更で再構築される。
    /// </summary>
    private void EnsureGlyphs()
    {
        if (!_glyphDirty || _renderer is null)
        {
            return;
        }

        _glyphDirty = false;
        var accent = GetTokenColor("Wcu.Color.Accent.Default", Color.FromRgb(0x00, 0x7A, 0xCC));
        var fallback = ToVector4(accent, 1.0);
        foreach (var (source, gpu) in _gpuMeshes)
        {
            var data = ViewportGlyph.BuildInstances(
                source, GlyphStride, _originX, _originY, _originZ,
                GlyphColorScale, fallback, out var count);
            _renderer.UpdateMeshGlyphs(gpu, data, count);
        }
    }

    /// <summary>選択ハイライトの GPU バッファを選択モデルと同期する(spec 6.17.3)。</summary>
    private void EnsureSelectionBuffers()
    {
        if (!_selectionDirty || _renderer is null)
        {
            return;
        }

        _selectionDirty = false;
        foreach (var gpu in _selectionGpu.Values)
        {
            gpu.Dispose();
        }

        _selectionGpu.Clear();

        foreach (var (source, _) in _gpuMeshes)
        {
            var faces = Selection.GetSelectedFaces(source);
            var nodes = Selection.GetSelectedNodes(source);
            if (faces.Count == 0 && nodes.Count == 0)
            {
                continue;
            }

            var gpu = _renderer.CreateSelectionMesh(source, faces, nodes, _originX, _originY, _originZ);
            if (gpu is not null)
            {
                _selectionGpu[source] = gpu;
            }
        }
    }

    /// <summary>可視メッシュの描画リストとピック用の対応リストを組み立てる。</summary>
    private void BuildRenderLists()
    {
        _renderItems.Clear();
        _visibleGpus.Clear();
        _visibleSources.Clear();

        foreach (var (source, gpu) in _gpuMeshes)
        {
            if (!source.IsVisible)
            {
                continue;
            }

            gpu.Color = ToVector4(source.Color, source.Opacity);
            gpu.ShowEdges = ShowEdges && source.ShowEdges;
            gpu.IsClippable = source.IsClippable;

            _selectionGpu.TryGetValue(source, out var selection);
            _renderItems.Add(new RenderItem(gpu, selection, Selection.IsPartSelected(source)));
            _visibleGpus.Add(gpu);
            _visibleSources.Add(source);
        }
    }

    private void EnsureColorMap()
    {
        if (!_colorMapDirty || _renderer is null)
        {
            return;
        }

        _colorMapDirty = false;
        var scale = ColorScale;
        if (scale is null)
        {
            return;
        }

        var rgba = new byte[256 * 4];
        for (var i = 0; i < 256; i++)
        {
            var color = scale.Sample((i + 0.5) / 256.0);
            rgba[i * 4] = color.R;
            rgba[i * 4 + 1] = color.G;
            rgba[i * 4 + 2] = color.B;
            rgba[i * 4 + 3] = color.A;
        }

        _renderer.SetColorMap(rgba);
    }

    private ContourSettings BuildContourSettings()
    {
        var scale = ColorScale;
        if (scale is null)
        {
            return new ContourSettings(0.0f, 1.0f, false, Vector4.Zero, Vector4.Zero, Vector4.One);
        }

        var useLog = scale.IsLogarithmic && scale.Minimum > 0 && scale.Maximum > 0;
        var min = useLog ? Math.Log10(scale.Minimum) : scale.Minimum;
        var max = useLog ? Math.Log10(scale.Maximum) : scale.Maximum;
        var range = max - min;
        var invRange = Math.Abs(range) < 1e-300 ? 0.0 : 1.0 / range;

        var below = scale.BelowRangeColor ?? scale.Sample(0.0);
        var above = scale.AboveRangeColor ?? scale.Sample(1.0);

        return new ContourSettings(
            (float)min, (float)invRange, useLog,
            ToVector4(scale.NaNColor, scale.NaNColor.A / 255.0),
            ToVector4(below, 1.0),
            ToVector4(above, 1.0));
    }

    // ================= MeshSource / ColorScale の変更追跡 =================

    private static void OnMeshSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var viewport = (WcuViewport)d;

        if (e.OldValue is INotifyCollectionChanged oldIncc)
        {
            oldIncc.CollectionChanged -= viewport.OnMeshCollectionChanged;
        }

        viewport.UnhookAllMeshes(e.OldValue as IEnumerable);

        if (e.NewValue is INotifyCollectionChanged newIncc)
        {
            newIncc.CollectionChanged += viewport.OnMeshCollectionChanged;
        }

        viewport.HookAllMeshes(e.NewValue as IEnumerable);
        viewport._geometryDirty = true;
        viewport._hasAutoFitted = false;
        viewport.InvalidateViewport();
    }

    private void OnMeshCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<ViewportMesh>())
            {
                item.PropertyChanged -= OnMeshPropertyChanged;
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            // Reset は個別の解除ができないため全付け直し
            UnhookAllMeshes(MeshSource);
            HookAllMeshes(MeshSource);
        }
        else if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<ViewportMesh>())
            {
                item.PropertyChanged += OnMeshPropertyChanged;
            }
        }

        _geometryDirty = true;
        InvalidateViewport();
    }

    private void HookAllMeshes(IEnumerable? meshes)
    {
        if (meshes is null)
        {
            return;
        }

        foreach (var mesh in meshes.OfType<ViewportMesh>())
        {
            mesh.PropertyChanged += OnMeshPropertyChanged;
        }
    }

    private void UnhookAllMeshes(IEnumerable? meshes)
    {
        if (meshes is null)
        {
            return;
        }

        foreach (var mesh in meshes.OfType<ViewportMesh>())
        {
            mesh.PropertyChanged -= OnMeshPropertyChanged;
        }
    }

    private void OnMeshPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewportMesh.Positions)
            or nameof(ViewportMesh.TriangleIndices)
            or nameof(ViewportMesh.ScalarValues))
        {
            _geometryDirty = true;
        }
        else if (e.PropertyName == nameof(ViewportMesh.Displacements) && sender is ViewportMesh mesh)
        {
            // 変位差し替えは軽量経路: 変位バッファのみ更新+選択節点クワッド再構築。
            // グリフのインスタンスデータにも変位が同梱されるため作り直す(spec 6.21.3)
            _displacementDirtyMeshes.Add(mesh);
            _selectionDirty = true;
            _glyphDirty = true;
        }
        else if (e.PropertyName == nameof(ViewportMesh.VectorValues))
        {
            // フィールド切替(変位/反力など)はインスタンスバッファの再構築のみ(spec 6.21.2)
            _glyphDirty = true;
        }

        InvalidateViewport();
    }

    private static void OnColorScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var viewport = (WcuViewport)d;

        if (e.OldValue is ColorScale oldScale)
        {
            oldScale.PropertyChanged -= viewport.OnColorScalePropertyChanged;
        }

        if (e.NewValue is ColorScale newScale)
        {
            newScale.PropertyChanged += viewport.OnColorScalePropertyChanged;
        }

        viewport._colorMapDirty = true;
        viewport.InvalidateViewport();
    }

    private void OnColorScalePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _colorMapDirty = true;
        InvalidateViewport();
    }

    private static void OnVisualOptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((WcuViewport)d).InvalidateViewport();

    /// <summary>ジオメトリ全体の再構築が必要な設定(EdgeExtractionLimit 等)の変更。</summary>
    private static void OnGeometryOptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var viewport = (WcuViewport)d;
        viewport._geometryDirty = true;
        viewport.InvalidateViewport();
    }

    /// <summary>インスタンスバッファの再構築が必要なグリフ設定(ストライド等)の変更。</summary>
    private static void OnGlyphDataOptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var viewport = (WcuViewport)d;
        viewport._glyphDirty = true;
        viewport.InvalidateViewport();
    }

    private static void OnGlyphColorScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var viewport = (WcuViewport)d;

        if (e.OldValue is ColorScale oldScale)
        {
            oldScale.PropertyChanged -= viewport.OnGlyphColorScalePropertyChanged;
        }

        if (e.NewValue is ColorScale newScale)
        {
            newScale.PropertyChanged += viewport.OnGlyphColorScalePropertyChanged;
        }

        viewport._glyphDirty = true;
        viewport.InvalidateViewport();
    }

    private void OnGlyphColorScalePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _glyphDirty = true;
        InvalidateViewport();
    }

    private static void OnSectionPlaneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var viewport = (WcuViewport)d;

        if (e.OldValue is SectionPlane oldPlane)
        {
            oldPlane.PropertyChanged -= viewport.OnSectionPlanePropertyChanged;
        }

        if (e.NewValue is SectionPlane newPlane)
        {
            newPlane.PropertyChanged += viewport.OnSectionPlanePropertyChanged;
        }

        viewport.InvalidateViewport();
    }

    private void OnSectionPlanePropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        InvalidateViewport();

    /// <summary>
    /// 現在の断面クリップ係数(シェーダ用)を求める。カット無効時は
    /// <see cref="ViewportSection.DisabledClip"/>(常に全表示)を返す。
    /// </summary>
    private Vector4 GetCurrentClipPlane() =>
        ViewportSection.ComputeClipCoefficients(SectionPlane, _originX, _originY, _originZ)
        ?? ViewportSection.DisabledClip;

    private static void OnPickModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var viewport = (WcuViewport)d;
        // ピックモード中は十字カーソルで「選択操作中」を示す(spec 6.17.4)
        viewport.Cursor = (ViewportPickMode)e.NewValue != ViewportPickMode.None ? Cursors.Cross : null;
        viewport.CancelPicking();
    }

    private void OnSelectionModelChanged(object? sender, EventArgs e)
    {
        _selectionDirty = true;
        InvalidateViewport();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void OnProjectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((WcuViewport)d).Camera.Projection = (ViewportProjection)e.NewValue;

    private static void OnUpAxisChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((WcuViewport)d).Camera.UpAxis = (ViewportUpAxis)e.NewValue;

    // ================= マウス操作(spec 6.16.4 / 6.17.4) =================

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.ChangedButton == MouseButton.Left)
        {
            if (PickMode == ViewportPickMode.None || _isOrbiting || _isPanning)
            {
                return;
            }

            _isPicking = true;
            _isRubberBanding = false;
            _pickStart = e.GetPosition(this);
            Focus();
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        CancelPicking();

        if (e.ClickCount == 2)
        {
            FitToView();
            e.Handled = true;
            return;
        }

        _lastMousePosition = e.GetPosition(this);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _isPanning = true;
        }
        else
        {
            _isOrbiting = true;
        }

        Focus();
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_isPicking)
        {
            var position = e.GetPosition(this);
            // プローブはクリックのみ(矩形選択なし、spec 6.20.2)
            if (!_isRubberBanding
                && PickMode != ViewportPickMode.Probe
                && (Math.Abs(position.X - _pickStart.X) > RubberBandThresholdDip
                    || Math.Abs(position.Y - _pickStart.Y) > RubberBandThresholdDip))
            {
                _isRubberBanding = true;
            }

            if (_isRubberBanding)
            {
                UpdateRubberBand(_pickStart, position);
            }

            e.Handled = true;
            return;
        }

        if (!_isOrbiting && !_isPanning)
        {
            return;
        }

        var current = e.GetPosition(this);
        var dx = current.X - _lastMousePosition.X;
        var dy = current.Y - _lastMousePosition.Y;
        _lastMousePosition = current;

        var dpi = VisualTreeHelper.GetDpi(this);

        if (_isPanning)
        {
            Camera.Pan(dx * dpi.DpiScaleX, dy * dpi.DpiScaleY, ActualHeight * dpi.DpiScaleY);
        }
        else
        {
            // 1 ピクセル ≒ 0.5 度(業界標準的な感度)
            const double sensitivity = 0.5 * Math.PI / 180.0;
            // 画面上でモデルがマウスに追従する向き: 右ドラッグで方位角を減らす
            Camera.Orbit(-dx * sensitivity, -dy * sensitivity);
        }

        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.ChangedButton == MouseButton.Left && _isPicking)
        {
            var position = e.GetPosition(this);
            var wasRubberBanding = _isRubberBanding;
            var rubberBandStart = _pickStart;
            CancelPicking();

            var additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            if (PickMode == ViewportPickMode.Probe)
            {
                ProbeAtPoint(position);
            }
            else if (wasRubberBanding)
            {
                SelectInRectangle(rubberBandStart, position, additive);
            }
            else
            {
                SelectAtPoint(position, additive);
            }

            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Middle && (_isOrbiting || _isPanning))
        {
            _isOrbiting = false;
            _isPanning = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    /// <summary>進行中のピック操作(ラバーバンド含む)を中止して状態を戻す。</summary>
    private void CancelPicking()
    {
        if (_isPicking)
        {
            _isPicking = false;
            _isRubberBanding = false;
            ReleaseMouseCapture();
        }

        if (_rubberBand is not null)
        {
            _rubberBand.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateRubberBand(Point start, Point end)
    {
        if (_rubberBand is null)
        {
            return;
        }

        var x = Math.Min(start.X, end.X);
        var y = Math.Min(start.Y, end.Y);
        Canvas.SetLeft(_rubberBand, x);
        Canvas.SetTop(_rubberBand, y);
        _rubberBand.Width = Math.Abs(end.X - start.X);
        _rubberBand.Height = Math.Abs(end.Y - start.Y);
        _rubberBand.Visibility = Visibility.Visible;
    }

    // ================= ピッキング実行(spec 6.17) =================

    /// <summary>
    /// クリック選択。additive=false は置換(空クリックで全解除)、
    /// additive=true(Ctrl)はヒット要素をトグルする。
    /// </summary>
    private void SelectAtPoint(Point positionDip, bool additive)
    {
        if (!TryGetPickContext(out var viewProj, out var dpiScale))
        {
            return;
        }

        var px = (int)(positionDip.X * dpiScale);
        var py = (int)(positionDip.Y * dpiScale);
        var hit = _renderer!.PickPixel(
            _visibleGpus, in viewProj, px, py, _lastEffectiveDeformationScale, GetCurrentClipPlane());

        Selection.BeginUpdate();
        try
        {
            if (!additive)
            {
                Selection.Clear();
            }

            if (hit is not { } h || h.MeshIndex >= _visibleSources.Count)
            {
                return;
            }

            var mesh = _visibleSources[h.MeshIndex];
            switch (PickMode)
            {
                case ViewportPickMode.Part:
                    if (additive)
                    {
                        Selection.TogglePart(mesh);
                    }
                    else
                    {
                        Selection.AddPart(mesh);
                    }

                    break;

                case ViewportPickMode.Face:
                    if (additive)
                    {
                        Selection.ToggleFace(mesh, h.TriangleIndex);
                    }
                    else
                    {
                        Selection.AddFace(mesh, h.TriangleIndex);
                    }

                    break;

                case ViewportPickMode.Node:
                    var cursor = new Vector2(px, py);
                    var node = ViewportPicking.FindNearestNodeOnTriangle(
                        mesh, h.TriangleIndex, _originX, _originY, _originZ,
                        in viewProj, _renderer.Width, _renderer.Height, cursor,
                        _lastEffectiveDeformationScale);
                    if (node is { } n)
                    {
                        if (additive)
                        {
                            Selection.ToggleNode(mesh, n);
                        }
                        else
                        {
                            Selection.AddNode(mesh, n);
                        }
                    }

                    break;
            }
        }
        finally
        {
            Selection.EndUpdate();
        }
    }

    /// <summary>矩形(ラバーバンド)選択。見えているものだけが対象(spec 6.17.4)。additive=true(Ctrl)は追加。</summary>
    private void SelectInRectangle(Point startDip, Point endDip, bool additive)
    {
        if (!TryGetPickContext(out var viewProj, out var dpiScale))
        {
            return;
        }

        var x0 = (int)(Math.Min(startDip.X, endDip.X) * dpiScale);
        var y0 = (int)(Math.Min(startDip.Y, endDip.Y) * dpiScale);
        var x1 = (int)Math.Ceiling(Math.Max(startDip.X, endDip.X) * dpiScale);
        var y1 = (int)Math.Ceiling(Math.Max(startDip.Y, endDip.Y) * dpiScale);

        var region = _renderer!.PickRegion(
            _visibleGpus, in viewProj, x0, y0, x1 - x0, y1 - y0, _lastEffectiveDeformationScale,
            GetCurrentClipPlane());

        Selection.BeginUpdate();
        try
        {
            if (!additive)
            {
                Selection.Clear();
            }

            foreach (var (meshIndex, triangles) in region)
            {
                if (meshIndex >= _visibleSources.Count)
                {
                    continue;
                }

                var mesh = _visibleSources[meshIndex];
                switch (PickMode)
                {
                    case ViewportPickMode.Part:
                        Selection.AddPart(mesh);
                        break;

                    case ViewportPickMode.Face:
                        Selection.AddFaces(mesh, triangles);
                        break;

                    case ViewportPickMode.Node:
                        var nodes = ViewportPicking.FindNodesInRectangle(
                            mesh, triangles, _originX, _originY, _originZ,
                            in viewProj, _renderer.Width, _renderer.Height,
                            new Vector2(x0, y0), new Vector2(x1, y1),
                            _lastEffectiveDeformationScale);
                        Selection.AddNodes(mesh, nodes);
                        break;
                }
            }
        }
        finally
        {
            Selection.EndUpdate();
        }
    }

    // ================= プローブ+注釈(spec 6.20) =================

    /// <summary>
    /// プローブ実行。GPU ID ピックで三角形を特定し、CPU のレイ交差+重心座標補間で
    /// ヒット点と値を求めて <see cref="ProbePicked"/> を発火する。未処理なら注釈を自動追加。
    /// 空クリック(背景)は何もしない(spec 6.20.2)。
    /// </summary>
    private void ProbeAtPoint(Point positionDip)
    {
        if (!TryGetPickContext(out var viewProj, out var dpiScale))
        {
            return;
        }

        var px = (int)(positionDip.X * dpiScale);
        var py = (int)(positionDip.Y * dpiScale);
        var hit = _renderer!.PickPixel(
            _visibleGpus, in viewProj, px, py, _lastEffectiveDeformationScale, GetCurrentClipPlane());
        if (hit is not { } h || h.MeshIndex >= _visibleSources.Count)
        {
            return;
        }

        var result = ViewportProbing.Probe(
            _visibleSources[h.MeshIndex], h.TriangleIndex, new Vector2(px, py), in viewProj,
            _renderer.Width, _renderer.Height, _originX, _originY, _originZ,
            _lastEffectiveDeformationScale);
        if (result is null)
        {
            return;
        }

        var args = new ProbePickedEventArgs(result);
        ProbePicked?.Invoke(this, args);
        if (args.Handled)
        {
            return;
        }

        Annotations.Add(new ViewportAnnotation
        {
            Mesh = result.Mesh,
            NodeIndex = result.NodeIndex,
            X = result.X,
            Y = result.Y,
            Z = result.Z,
            Text = ProbeLabelFormatter?.Invoke(result) ?? FormatDefaultProbeLabel(result),
            Tag = result,
        });
    }

    /// <summary>組込みのプローブラベル書式(カルチャ非依存)。</summary>
    internal static string FormatDefaultProbeLabel(ProbeResult result) => result.ScalarValue is { } value
        ? FormattableString.Invariant($"N{result.NodeIndex}: {value:G4}")
        : FormattableString.Invariant(
            $"N{result.NodeIndex} ({result.X:G4}, {result.Y:G4}, {result.Z:G4})");

    private void OnAnnotationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Reset で旧要素が取れないため、フック済みリストで付け外しを管理する
        foreach (var annotation in _hookedAnnotations)
        {
            annotation.PropertyChanged -= OnAnnotationItemChanged;
        }

        _hookedAnnotations.Clear();
        foreach (var annotation in Annotations)
        {
            annotation.PropertyChanged += OnAnnotationItemChanged;
            _hookedAnnotations.Add(annotation);
        }

        RebuildAnnotationVisuals();
        InvalidateViewport();
    }

    private void OnAnnotationItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewportAnnotation.Text) && sender is ViewportAnnotation annotation)
        {
            _annotationVisuals.FirstOrDefault(v => v.Annotation == annotation)?.Label
                .SetCurrentValue(TextBlock.TextProperty, annotation.Text);
        }

        InvalidateViewport();
    }

    /// <summary>注釈 1 件分のオーバーレイ要素(チップ+リーダーライン+アンカードット)。</summary>
    private sealed record AnnotationVisual(
        ViewportAnnotation Annotation, Border Chip, TextBlock Label, Line Leader, Ellipse Dot);

    private void RebuildAnnotationVisuals()
    {
        if (_annotationCanvas is null)
        {
            return;
        }

        _annotationCanvas.Children.Clear();
        _annotationVisuals.Clear();

        foreach (var annotation in Annotations)
        {
            var label = new TextBlock
            {
                Text = annotation.Text,
                FontSize = 11,
                TextAlignment = TextAlignment.Left,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Wcu.Brush.Text.Primary");

            var chip = new Border
            {
                Child = label,
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 2, 6, 2),
                Visibility = Visibility.Collapsed,
            };
            chip.SetResourceReference(Border.BackgroundProperty, "Wcu.Brush.Surface.Elevated");
            chip.SetResourceReference(Border.BorderBrushProperty, "Wcu.Brush.Accent.Default");

            var leader = new Line { StrokeThickness = 1.0, Visibility = Visibility.Collapsed };
            leader.SetResourceReference(Shape.StrokeProperty, "Wcu.Brush.Accent.Default");

            var dot = new Ellipse { Width = 5.0, Height = 5.0, Visibility = Visibility.Collapsed };
            dot.SetResourceReference(Shape.FillProperty, "Wcu.Brush.Accent.Default");

            _annotationCanvas.Children.Add(leader);
            _annotationCanvas.Children.Add(dot);
            _annotationCanvas.Children.Add(chip);
            _annotationVisuals.Add(new AnnotationVisual(annotation, chip, label, leader, dot));
        }
    }

    /// <summary>
    /// 注釈オーバーレイの毎フレーム更新(spec 6.20.4)。節点バインドの注釈は変形適用後の
    /// 節点位置に追従し、非表示メッシュ・断面クリップされた節点・画面外は自動で隠す。
    /// </summary>
    private void UpdateAnnotationOverlay(
        in Matrix4x4 viewProj, double pixelWidth, double pixelHeight, double dpiScale,
        Vector4 clipPlane)
    {
        if (_annotationCanvas is null || _annotationVisuals.Count == 0)
        {
            return;
        }

        foreach (var visual in _annotationVisuals)
        {
            var anchor = GetAnnotationAnchor(visual.Annotation, clipPlane);
            var pixel = anchor is { } a
                ? ViewportPicking.ProjectToPixel(a, in viewProj, pixelWidth, pixelHeight)
                : null;
            var visible = pixel is { } p
                && p.X >= 0.0f && p.X <= pixelWidth && p.Y >= 0.0f && p.Y <= pixelHeight;

            var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            visual.Chip.Visibility = visibility;
            visual.Leader.Visibility = visibility;
            visual.Dot.Visibility = visibility;
            if (!visible)
            {
                continue;
            }

            var ax = pixel!.Value.X / dpiScale;
            var ay = pixel.Value.Y / dpiScale;

            visual.Dot.SetCurrentValue(Canvas.LeftProperty, ax - visual.Dot.Width / 2.0);
            visual.Dot.SetCurrentValue(Canvas.TopProperty, ay - visual.Dot.Height / 2.0);

            visual.Leader.X1 = ax;
            visual.Leader.Y1 = ay;
            visual.Leader.X2 = ax + AnnotationLeaderLengthDip;
            visual.Leader.Y2 = ay - AnnotationLeaderLengthDip;

            // チップの左下角がリーダーラインの先端に付くよう配置する
            visual.Chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            visual.Chip.SetCurrentValue(Canvas.LeftProperty, ax + AnnotationLeaderLengthDip);
            visual.Chip.SetCurrentValue(
                Canvas.TopProperty,
                ay - AnnotationLeaderLengthDip - visual.Chip.DesiredSize.Height);
        }
    }

    /// <summary>
    /// 注釈アンカーのローカル座標(再センタリング+変形適用後)。自動非表示条件に該当するときは null:
    /// 節点バインドで対象メッシュが可視リストにない / 節点が範囲外 / 断面クリップされている。
    /// 自由 3D 点アンカーは常に位置を返す(画面外判定は呼び出し側)。
    /// </summary>
    private Vector3? GetAnnotationAnchor(ViewportAnnotation annotation, Vector4 clipPlane)
    {
        if (annotation.Mesh is not { } mesh)
        {
            return new Vector3(
                (float)(annotation.X - _originX),
                (float)(annotation.Y - _originY),
                (float)(annotation.Z - _originZ));
        }

        if (!_visibleSources.Contains(mesh))
        {
            return null;
        }

        var node = annotation.NodeIndex;
        var positions = mesh.Positions;
        if (node < 0 || node * 3 + 2 >= positions.Length)
        {
            return null;
        }

        var local = ViewportPicking.GetLocalPosition(
            positions, mesh.Displacements, _lastEffectiveDeformationScale, node,
            _originX, _originY, _originZ);
        if (mesh.IsClippable && ViewportSection.IsClipped(local, clipPlane))
        {
            return null;
        }

        return local;
    }

    /// <summary>
    /// ピック実行に必要な状態(レンダラー・可視リスト・ビュー射影行列)を用意する。
    /// 描画と同じ行列を使うため、ヒット判定は画面と完全に一致する。
    /// </summary>
    private bool TryGetPickContext(out Matrix4x4 viewProj, out double dpiScale)
    {
        viewProj = default;
        dpiScale = 1.0;

        if (_renderer is null || _renderBroken || _renderer.Width <= 0 || _renderer.Height <= 0)
        {
            return false;
        }

        EnsureGeometry();
        BuildRenderLists();
        if (_visibleGpus.Count == 0)
        {
            return false;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        dpiScale = dpi.DpiScaleX;
        var aspect = (double)_renderer.Width / _renderer.Height;
        viewProj = Camera.GetViewMatrix() * Camera.GetProjectionMatrix(aspect);
        return true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        // 3D ビューポートは素のホイールでズーム(spec 6.16.4)
        var factor = Math.Pow(1.0 / 1.2, e.Delta / 120.0);
        var position = e.GetPosition(this);
        var dpi = VisualTreeHelper.GetDpi(this);
        Camera.ZoomAt(
            factor,
            position.X * dpi.DpiScaleX, position.Y * dpi.DpiScaleY,
            ActualWidth * dpi.DpiScaleX, ActualHeight * dpi.DpiScaleY);
        e.Handled = true;
    }

    // ================= 軸トライアッド(WPF オーバーレイ) =================

    private static readonly Color[] TriadColors =
    [
        Color.FromRgb(0xE5, 0x51, 0x51), // X
        Color.FromRgb(0x51, 0xC0, 0x51), // Y
        Color.FromRgb(0x42, 0x8B, 0xF0), // Z
    ];

    private static readonly string[] TriadLabelTexts = ["X", "Y", "Z"];

    private void SetupTriadOverlay()
    {
        if (_triadCanvas is null)
        {
            return;
        }

        _triadCanvas.Children.Clear();
        for (var i = 0; i < 3; i++)
        {
            var brush = new SolidColorBrush(TriadColors[i]);
            _triadLines[i] = new Line
            {
                Stroke = brush,
                StrokeThickness = 2.0,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            _triadLabels[i] = new TextBlock
            {
                Text = TriadLabelTexts[i],
                Foreground = brush,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
            };
            _triadCanvas.Children.Add(_triadLines[i]);
            _triadCanvas.Children.Add(_triadLabels[i]);
        }

        // 矩形選択のラバーバンド(spec 6.17.4)。アクセント色トークンに追従
        _rubberBand = new Rectangle
        {
            StrokeThickness = 1.0,
            StrokeDashArray = [3.0, 2.0],
            Visibility = Visibility.Collapsed,
            Opacity = 0.9,
        };
        _rubberBand.SetResourceReference(Shape.StrokeProperty, "Wcu.Brush.Accent.Default");
        _rubberBand.SetResourceReference(Shape.FillProperty, "Wcu.Brush.State.Selected");
        _triadCanvas.Children.Add(_rubberBand);
    }

    private void UpdateTriadOverlay()
    {
        if (_triadCanvas is null || _triadLines[0] is null)
        {
            return;
        }

        // キャンバスはラバーバンドと共用のため、トライアッド要素だけ表示を切り替える
        var visible = ShowAxisTriad && ActualWidth > TriadMargin * 2 && ActualHeight > TriadMargin * 2;
        for (var i = 0; i < 3; i++)
        {
            _triadLines[i].Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            _triadLabels[i].Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        if (!visible)
        {
            return;
        }

        var (right, up) = Camera.GetViewBasis();
        var anchorX = TriadMargin;
        var anchorY = ActualHeight - TriadMargin;

        Vector3[] axes = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];
        for (var i = 0; i < 3; i++)
        {
            var sx = Vector3.Dot(axes[i], right) * TriadArmLength;
            var sy = -Vector3.Dot(axes[i], up) * TriadArmLength; // スクリーン Y は下向き
            var endX = anchorX + sx;
            var endY = anchorY + sy;

            _triadLines[i].X1 = anchorX;
            _triadLines[i].Y1 = anchorY;
            _triadLines[i].X2 = endX;
            _triadLines[i].Y2 = endY;

            // ラベルは軸の先端の少し外側
            Canvas.SetLeft(_triadLabels[i], endX + sx * 0.18 - 4);
            Canvas.SetTop(_triadLabels[i], endY + sy * 0.18 - 8);
        }
    }

    // ================= ヘルパー =================

    private Color GetTokenColor(string key, Color fallback) =>
        TryFindResource(key) is Color color ? color : fallback;

    private static Vector4 ToVector4(Color color, double alpha) => new(
        color.R / 255.0f, color.G / 255.0f, color.B / 255.0f, (float)Math.Clamp(alpha, 0.0, 1.0));

    private void ReleaseRenderer()
    {
        foreach (var (_, gpu) in _gpuMeshes)
        {
            gpu.Dispose();
        }

        foreach (var gpu in _selectionGpu.Values)
        {
            gpu.Dispose();
        }

        _gpuMeshes.Clear();
        _displacementDirtyMeshes.Clear();
        _selectionGpu.Clear();
        _renderItems.Clear();
        _visibleGpus.Clear();
        _visibleSources.Clear();
        _geometryDirty = true;
        _colorMapDirty = true;
        _selectionDirty = true;
        _glyphDirty = true;
        _hasAnyLod = false;
        _lodRequested = false;
        _lodRestoreTimer?.Stop();
        _lastBackBuffer = 0;
        _d3dImage = null;
        _softwareBitmap = null;
        if (_image is not null)
        {
            _image.Source = null;
        }

        _renderer?.Dispose();
        _renderer = null;
    }
}
