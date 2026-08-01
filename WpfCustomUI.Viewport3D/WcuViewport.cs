using System.Collections;
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
/// D3D11 自作エンジンによる 3D ビューポートコントロール(spec 6.16)。
/// <para>
/// - 表示: <see cref="MeshSource"/> にバインドした <see cref="ViewportMesh"/> 群を
///   ライティング付き単色、または節点スカラー+<see cref="ColorScale"/> のコンターで描画する。
/// - カメラ: 中ボタンドラッグ=回転 / Shift+中ボタン=パン / ホイール=カーソル位置へズーム /
///   中ボタンダブルクリック=Fit。左ボタンは将来のピッキング用に予約(spec 6.16.4)。
/// - 描画はオンデマンド(変更・操作時のみ)。テーマ変更には <see cref="ThemeManager.ThemeChanged"/> で追従する。
/// - 表示経路はハードウェアなら D3DImage(エアスペース問題なし)、
///   WARP / D3D9 不可環境では WriteableBitmap へ自動フォールバックする。
/// </para>
/// </summary>
[TemplatePart(Name = PartImage, Type = typeof(Image))]
[TemplatePart(Name = PartTriadCanvas, Type = typeof(Canvas))]
public class WcuViewport : Control
{
    private const string PartImage = "PART_Image";
    private const string PartTriadCanvas = "PART_TriadCanvas";
    private const double TriadArmLength = 36.0;
    private const double TriadMargin = 56.0;

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

    private readonly List<(ViewportMesh Source, GpuMesh Gpu)> _gpuMeshes = [];
    private readonly List<GpuMesh> _renderList = [];

    private Image? _image;
    private Canvas? _triadCanvas;
    private ViewportRenderer? _renderer;
    private D3DImage? _d3dImage;
    private WriteableBitmap? _softwareBitmap;
    private nint _lastBackBuffer;

    private bool _renderQueued;
    private bool _geometryDirty = true;
    private bool _colorMapDirty = true;
    private bool _hasAutoFitted;
    private bool _renderBroken;
    private int _consecutiveFailures;

    private Bounds3D _localBounds = Bounds3D.Empty;

    // マウス操作状態
    private Point _lastMousePosition;
    private bool _isOrbiting;
    private bool _isPanning;

    // 軸トライアッドの WPF オーバーレイ要素
    private readonly Line[] _triadLines = new Line[3];
    private readonly TextBlock[] _triadLabels = new TextBlock[3];

    static WcuViewport()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(WcuViewport), new FrameworkPropertyMetadata(typeof(WcuViewport)));
    }

    public WcuViewport()
    {
        Camera = new ViewportCamera();
        Camera.Changed += (_, _) => InvalidateViewport();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

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

    /// <summary>カメラ。アプリから直接操作(視点の保存/復元など)できる。</summary>
    public ViewportCamera Camera { get; }

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
        SetupTriadOverlay();
        InvalidateViewport();
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
        InvalidateViewport();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        ReleaseRenderer();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => InvalidateViewport();

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
            EnsureColorMap();

            var aspect = (double)pixelWidth / pixelHeight;
            var viewProj = Camera.GetViewMatrix() * Camera.GetProjectionMatrix(aspect);
            var contour = BuildContourSettings();

            _renderList.Clear();
            foreach (var (source, gpu) in _gpuMeshes)
            {
                if (!source.IsVisible)
                {
                    continue;
                }

                gpu.Color = ToVector4(source.Color, source.Opacity);
                gpu.ShowEdges = ShowEdges && source.ShowEdges;
                _renderList.Add(gpu);
            }

            var background = GetTokenColor("Wcu.Color.Surface.Window", Color.FromRgb(0x1E, 0x1E, 0x1E));
            var edgeColor = GetTokenColor("Wcu.Color.Text.Muted", Color.FromRgb(0xC5, 0xC5, 0xC5));

            if (_renderer.CanUseD3DImage)
            {
                PresentViaD3DImage(sizeChanged, viewProj, contour, background, edgeColor);
            }
            else
            {
                PresentViaWriteableBitmap(sizeChanged, viewProj, contour, background, edgeColor);
            }

            UpdateTriadOverlay();
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
        bool sizeChanged, Matrix4x4 viewProj, ContourSettings contour, Color background, Color edgeColor)
    {
        if (_d3dImage is null)
        {
            _d3dImage = new D3DImage();
            _d3dImage.IsFrontBufferAvailableChanged += (_, _) => InvalidateViewport();
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
                _renderList, in viewProj, Camera.GetEyeDirection(),
                ToVector4(background, 1.0), in contour, ShowContours, ToVector4(edgeColor, 1.0));

            _d3dImage.AddDirtyRect(new Int32Rect(0, 0, _renderer.Width, _renderer.Height));
        }
        finally
        {
            _d3dImage.Unlock();
        }
    }

    private void PresentViaWriteableBitmap(
        bool sizeChanged, Matrix4x4 viewProj, ContourSettings contour, Color background, Color edgeColor)
    {
        _renderer!.Render(
            _renderList, in viewProj, Camera.GetEyeDirection(),
            ToVector4(background, 1.0), in contour, ShowContours, ToVector4(edgeColor, 1.0));

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

        foreach (var (_, gpu) in _gpuMeshes)
        {
            gpu.Dispose();
        }

        _gpuMeshes.Clear();
        _geometryDirty = false;

        var meshes = MeshSource?.OfType<ViewportMesh>().ToList() ?? [];
        var bounds = Bounds3D.Empty;
        foreach (var mesh in meshes)
        {
            bounds = bounds.Union(ViewportGeometry.ComputeBounds(mesh.Positions));
        }

        if (bounds.IsEmpty)
        {
            _localBounds = Bounds3D.Empty;
            return;
        }

        // シーン中心で再センタリング(spec 6.16.3: 大座標対策)
        var (ox, oy, oz) = (bounds.CenterX, bounds.CenterY, bounds.CenterZ);
        _localBounds = new Bounds3D(
            bounds.MinX - ox, bounds.MinY - oy, bounds.MinZ - oz,
            bounds.MaxX - ox, bounds.MaxY - oy, bounds.MaxZ - oz);

        foreach (var mesh in meshes)
        {
            var gpu = _renderer.CreateMesh(mesh, ox, oy, oz);
            if (gpu is not null)
            {
                _gpuMeshes.Add((mesh, gpu));
            }
        }

        if (!_hasAutoFitted)
        {
            _hasAutoFitted = true;
            Camera.FitToBounds(_localBounds);
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

    private static void OnProjectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((WcuViewport)d).Camera.Projection = (ViewportProjection)e.NewValue;

    private static void OnUpAxisChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((WcuViewport)d).Camera.UpAxis = (ViewportUpAxis)e.NewValue;

    // ================= マウス操作(spec 6.16.4) =================

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

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
        if (!_isOrbiting && !_isPanning)
        {
            return;
        }

        var position = e.GetPosition(this);
        var dx = position.X - _lastMousePosition.X;
        var dy = position.Y - _lastMousePosition.Y;
        _lastMousePosition = position;

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
        if (e.ChangedButton == MouseButton.Middle && (_isOrbiting || _isPanning))
        {
            _isOrbiting = false;
            _isPanning = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
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
    }

    private void UpdateTriadOverlay()
    {
        if (_triadCanvas is null || _triadLines[0] is null)
        {
            return;
        }

        var visible = ShowAxisTriad && ActualWidth > TriadMargin * 2 && ActualHeight > TriadMargin * 2;
        _triadCanvas.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
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

        _gpuMeshes.Clear();
        _geometryDirty = true;
        _colorMapDirty = true;
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
