using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using WpfCustomUI.Controls;
using WpfCustomUI.Viewport3D;

namespace WpfCustomUI.Gallery.Pages;

/// <summary>
/// 変形表示+アニメーションのデモ(spec 6.18.5)。
/// 片持ち梁(板)の曲げ固有モード(Euler-Bernoulli の解析解)を表示し、
/// スケール/振動アニメ/非変形重畳/PlaybackBar 過渡再生を試せる。
/// </summary>
public partial class ViewportDeformationPage : UserControl
{
    private const double BeamLength = 100.0;  // [mm] x∈[0, L]、x=0 固定端
    private const double BeamWidth = 40.0;    // [mm] y∈[-W/2, W/2]
    private const double TipAmplitude = 1.0;  // [mm] モード形状の先端振幅
    private const int DivisionsX = 50;
    private const int DivisionsY = 20;
    private const int TransientFrameCount = 90;
    private const double TransientDuration = 3.0; // [s]

    /// <summary>片持ち梁の曲げ固有値 βL(cosh·cos = -1 の根)。</summary>
    private static readonly double[] BetaL = [1.8751040687, 4.6940911330, 7.8547574382];

    private readonly ObservableCollection<ViewportMesh> _meshes = [];
    private readonly ColorScale _scale;
    private readonly ViewportMesh _plate;
    private readonly double[][] _modeShapes;      // [モード][節点] 正規化たわみ(先端=1)
    private readonly double[][] _transientFrames; // [フレーム][3N] 変位配列

    public ViewportDeformationPage()
    {
        InitializeComponent();

        _scale = new ColorScale
        {
            ColorMap = ColorMap.Jet,
            Minimum = 0.0,
            Maximum = TipAmplitude,
        };

        (_plate, var nodeX) = CreateCantileverPlate();
        _modeShapes = [.. BetaL.Select(bl => ComputeModeShape(bl, nodeX))];
        _transientFrames = ComputeTransientFrames(nodeX);
        _meshes.Add(_plate);

        ApplyMode(0);

        Viewport.ColorScale = _scale;
        Viewport.MeshSource = _meshes;
        Legend.Scale = _scale;
        Playback.FrameCount = TransientFrameCount;

        Loaded += (_, _) => RendererInfo.Text = Viewport.IsSoftwareRendering
            ? "レンダリング経路: WARP(ソフトウェア)+ WriteableBitmap フォールバック"
            : "レンダリング経路: ハードウェア D3D11 + D3DImage(共有サーフェス)";
    }

    /// <summary>片持ち板の構造格子メッシュと、各節点の x 座標を作る。</summary>
    private static (ViewportMesh Mesh, double[] NodeX) CreateCantileverPlate()
    {
        var vertexCount = (DivisionsX + 1) * (DivisionsY + 1);
        var positions = new double[vertexCount * 3];
        var nodeX = new double[vertexCount];

        for (var i = 0; i <= DivisionsX; i++)
        {
            var x = BeamLength * i / DivisionsX;
            for (var j = 0; j <= DivisionsY; j++)
            {
                var node = i * (DivisionsY + 1) + j;
                positions[node * 3] = x;
                positions[node * 3 + 1] = -BeamWidth / 2.0 + BeamWidth * j / DivisionsY;
                positions[node * 3 + 2] = 0.0;
                nodeX[node] = x;
            }
        }

        var triangles = new List<int>(DivisionsX * DivisionsY * 6);
        for (var i = 0; i < DivisionsX; i++)
        {
            for (var j = 0; j < DivisionsY; j++)
            {
                var i00 = i * (DivisionsY + 1) + j;
                var i01 = i00 + 1;
                var i10 = (i + 1) * (DivisionsY + 1) + j;
                var i11 = i10 + 1;

                triangles.Add(i00);
                triangles.Add(i10);
                triangles.Add(i11);
                triangles.Add(i00);
                triangles.Add(i11);
                triangles.Add(i01);
            }
        }

        var mesh = new ViewportMesh
        {
            Name = "片持ち板",
            Positions = positions,
            TriangleIndices = [.. triangles],
        };
        return (mesh, nodeX);
    }

    /// <summary>
    /// Euler-Bernoulli 片持ち梁のモード形状 φ(x) = cosh βx − cos βx − σ(sinh βx − sin βx)。
    /// 先端たわみが 1 になるよう正規化する。
    /// </summary>
    private static double[] ComputeModeShape(double betaL, double[] nodeX)
    {
        var beta = betaL / BeamLength;
        var sigma = (Math.Sinh(betaL) - Math.Sin(betaL)) / (Math.Cosh(betaL) + Math.Cos(betaL));

        double Phi(double x) =>
            Math.Cosh(beta * x) - Math.Cos(beta * x)
            - sigma * (Math.Sinh(beta * x) - Math.Sin(beta * x));

        var tip = Phi(BeamLength);
        return [.. nodeX.Select(x => Phi(x) / tip)];
    }

    /// <summary>
    /// 過渡応答フレーム列: 先端初期変位からの減衰自由振動。
    /// w(x,t) = Σ aᵢ φᵢ(x) cos(ωᵢt) e^(−ζωᵢt)、ωᵢ ∝ (βᵢL)²(1 次を 1 Hz に正規化)。
    /// </summary>
    private double[][] ComputeTransientFrames(double[] nodeX)
    {
        double[] amplitudes = [0.7, 0.22, 0.08]; // モード寄与(合計 = 先端初期変位 1mm)
        const double zeta = 0.04;                // 減衰比
        var omega1 = 2.0 * Math.PI;              // 1 次 = 1 Hz 相当

        var frames = new double[TransientFrameCount][];
        for (var f = 0; f < TransientFrameCount; f++)
        {
            var t = TransientDuration * f / (TransientFrameCount - 1);
            var displacements = new double[nodeX.Length * 3];

            for (var mode = 0; mode < BetaL.Length; mode++)
            {
                var omega = omega1 * (BetaL[mode] * BetaL[mode]) / (BetaL[0] * BetaL[0]);
                var factor = amplitudes[mode] * TipAmplitude
                    * Math.Cos(omega * t) * Math.Exp(-zeta * omega * t);
                var shape = _modeShapes[mode];
                for (var node = 0; node < nodeX.Length; node++)
                {
                    displacements[node * 3 + 2] += factor * shape[node];
                }
            }

            frames[f] = displacements;
        }

        return frames;
    }

    /// <summary>選択モードの静的モード形状を Displacements / ScalarValues に適用する。</summary>
    private void ApplyMode(int modeIndex)
    {
        var shape = _modeShapes[modeIndex];
        var displacements = new double[shape.Length * 3];
        var scalars = new double[shape.Length];
        for (var node = 0; node < shape.Length; node++)
        {
            var w = TipAmplitude * shape[node];
            displacements[node * 3 + 2] = w;
            scalars[node] = Math.Abs(w);
        }

        // ScalarValues の差し替えはジオメトリ再構築(重い経路)、
        // Displacements の差し替えは変位バッファのみの部分更新(軽い経路)
        _plate.ScalarValues = scalars;
        _plate.Displacements = displacements;
    }

    // 注意: XAML パース中(フィールド初期化前)にもイベントが発火しうるため null ガードが必要

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_modeShapes is not null && ModeCombo.SelectedIndex >= 0)
        {
            Playback.IsPlaying = false;
            ApplyMode(ModeCombo.SelectedIndex);
        }
    }

    private void OnAutoScaleClick(object sender, RoutedEventArgs e) =>
        ScaleSlider.Value = Math.Min(Viewport.GetSuggestedDeformationScale(), ScaleSlider.Maximum);

    private void OnFitClick(object sender, RoutedEventArgs e) => Viewport.FitToView();

    private void OnPlaybackFrameChanged(object sender, RoutedPropertyChangedEventArgs<int> e)
    {
        if (_transientFrames is null || e.NewValue < 0 || e.NewValue >= _transientFrames.Length)
        {
            return;
        }

        var t = TransientDuration * e.NewValue / (TransientFrameCount - 1);
        Playback.FrameLabel = $"t = {t:0.00} s";

        // Displacements の差し替えは変位バッファのみの部分更新(軽い経路)なので
        // 毎フレーム呼んでよい。コンター(ScalarValues)は差し替えるとジオメトリ再構築が
        // 走るため再生中は静的なまま(実アプリでも一般的な見せ方)
        _plate.Displacements = _transientFrames[e.NewValue];
    }
}
