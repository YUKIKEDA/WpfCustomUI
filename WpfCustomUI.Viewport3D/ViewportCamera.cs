using System.Numerics;

namespace WpfCustomUI.Viewport3D;

/// <summary>
/// ターンテーブル型カメラ(spec 6.16.4)。GPU に依存しない純粋な数学で、単体テスト可能。
/// <para>
/// 状態は「注視点(Target)+方位角(Yaw)+仰角(Pitch)+距離(Distance)」で持ち、
/// 上方向軸(<see cref="UpAxis"/>)まわりのターンテーブル回転のみを許す
/// (トラックボール回転は採用しない)。
/// 平行投影の見かけサイズは透視投影と一致するよう距離から導出するため、
/// 投影切替でモデルの大きさが変わらない。
/// </para>
/// </summary>
public sealed class ViewportCamera
{
    /// <summary>仰角の上限(真上・真下でのジンバル特異点を避ける)。</summary>
    private const double MaxPitch = 89.5 * Math.PI / 180.0;

    private Vector3 _target;
    private double _yaw = 45.0 * Math.PI / 180.0;
    private double _pitch = 30.0 * Math.PI / 180.0;
    private double _distance = 10.0;
    private double _fieldOfView = 45.0 * Math.PI / 180.0;
    private double _sceneRadius = 5.0;
    private ViewportProjection _projection = ViewportProjection.Perspective;
    private ViewportUpAxis _upAxis = ViewportUpAxis.ZUp;

    /// <summary>カメラ状態が変わったときに発火する(ビューポートが再描画に使う)。</summary>
    public event EventHandler? Changed;

    /// <summary>注視点(モデルローカル座標 = 再センタリング後の座標系)。</summary>
    public Vector3 Target
    {
        get => _target;
        set => SetField(ref _target, value);
    }

    /// <summary>方位角(ラジアン)。上方向軸まわりの回転。</summary>
    public double Yaw
    {
        get => _yaw;
        set => SetField(ref _yaw, value);
    }

    /// <summary>仰角(ラジアン)。±89.5°にクランプされる。</summary>
    public double Pitch
    {
        get => _pitch;
        set => SetField(ref _pitch, Math.Clamp(value, -MaxPitch, MaxPitch));
    }

    /// <summary>注視点からカメラまでの距離。正の値にクランプされる。</summary>
    public double Distance
    {
        get => _distance;
        set => SetField(ref _distance, Math.Max(value, 1e-9));
    }

    /// <summary>垂直視野角(ラジアン、既定 45°)。平行投影の見かけサイズ導出にも使う。</summary>
    public double FieldOfView
    {
        get => _fieldOfView;
        set => SetField(ref _fieldOfView, Math.Clamp(value, 0.01, Math.PI - 0.01));
    }

    /// <summary>シーンの代表半径。near/far クリップ面の自動決定に使う(FitToBounds で更新される)。</summary>
    public double SceneRadius
    {
        get => _sceneRadius;
        set => SetField(ref _sceneRadius, Math.Max(value, 1e-9));
    }

    public ViewportProjection Projection
    {
        get => _projection;
        set => SetField(ref _projection, value);
    }

    public ViewportUpAxis UpAxis
    {
        get => _upAxis;
        set => SetField(ref _upAxis, value);
    }

    /// <summary>上方向軸の単位ベクトル。</summary>
    public Vector3 UpDirection =>
        _upAxis == ViewportUpAxis.ZUp ? Vector3.UnitZ : Vector3.UnitY;

    /// <summary>
    /// LookAt に渡す実効 up ベクトル。真上/真下(視線と上方向軸が平行)では
    /// 特異点を避けるため水平軸へフォールバックする(Top/Bottom 標準視点用)。
    /// </summary>
    private Vector3 GetEffectiveUp(Vector3 eyeDirection)
    {
        var up = UpDirection;
        if (Math.Abs(Vector3.Dot(eyeDirection, up)) < 0.99999f)
        {
            return up;
        }

        // Z-up の真上視点は +Y が画面上、Y-up の真上視点は -Z が画面上(CAD 慣例)
        return _upAxis == ViewportUpAxis.ZUp ? Vector3.UnitY : -Vector3.UnitZ;
    }

    /// <summary>注視点→カメラ方向の単位ベクトル。</summary>
    public Vector3 GetEyeDirection()
    {
        var cosP = Math.Cos(_pitch);
        var sinP = Math.Sin(_pitch);
        var cosY = Math.Cos(_yaw);
        var sinY = Math.Sin(_yaw);

        return _upAxis == ViewportUpAxis.ZUp
            ? new Vector3((float)(cosP * cosY), (float)(cosP * sinY), (float)sinP)
            : new Vector3((float)(cosP * sinY), (float)sinP, (float)(cosP * cosY));
    }

    /// <summary>カメラ(視点)のワールド位置。</summary>
    public Vector3 GetEyePosition() => _target + GetEyeDirection() * (float)_distance;

    /// <summary>ビュー行列(右手系)。</summary>
    public Matrix4x4 GetViewMatrix()
    {
        var eyeDir = GetEyeDirection();
        return Matrix4x4.CreateLookAt(_target + eyeDir * (float)_distance, _target, GetEffectiveUp(eyeDir));
    }

    /// <summary>
    /// 投影行列(右手系)。near/far は距離とシーン半径から自動決定する。
    /// 平行投影の高さは透視投影の注視点深度での視野高さに一致させる。
    /// </summary>
    public Matrix4x4 GetProjectionMatrix(double aspectRatio)
    {
        var aspect = Math.Max(aspectRatio, 1e-6);
        var far = _distance + _sceneRadius * 3.0;
        var near = Math.Max(_sceneRadius * 1e-3, _distance - _sceneRadius * 3.0);

        if (_projection == ViewportProjection.Perspective)
        {
            return Matrix4x4.CreatePerspectiveFieldOfView(
                (float)_fieldOfView, (float)aspect, (float)near, (float)far);
        }

        var height = GetViewHeightAtTarget();
        return Matrix4x4.CreateOrthographic(
            (float)(height * aspect), (float)height, (float)near, (float)far);
    }

    /// <summary>注視点深度でのビューの世界座標高さ(パン・ズーム・平行投影サイズの共通基準)。</summary>
    public double GetViewHeightAtTarget() =>
        2.0 * _distance * Math.Tan(_fieldOfView / 2.0);

    /// <summary>ビューポート 1 ピクセルに相当する世界座標長(注視点深度)。</summary>
    public double GetWorldPerPixel(double viewportHeightPixels) =>
        GetViewHeightAtTarget() / Math.Max(viewportHeightPixels, 1.0);

    /// <summary>カメラの右方向・上方向の単位ベクトル(パン・ズーム位置計算用)。</summary>
    public (Vector3 Right, Vector3 Up) GetViewBasis()
    {
        var eyeDir = GetEyeDirection();
        var forward = -eyeDir; // 視線方向(注視点向き)
        var right = Vector3.Normalize(Vector3.Cross(forward, GetEffectiveUp(eyeDir)));
        var up = Vector3.Normalize(Vector3.Cross(right, forward));
        return (right, up);
    }

    /// <summary>ターンテーブル回転。dYaw / dPitch はラジアン。</summary>
    public void Orbit(double deltaYaw, double deltaPitch)
    {
        _yaw = NormalizeAngle(_yaw + deltaYaw);
        _pitch = Math.Clamp(_pitch + deltaPitch, -MaxPitch, MaxPitch);
        RaiseChanged();
    }

    /// <summary>
    /// パン(平行移動)。ピクセル単位のマウス移動量から注視点を動かす。
    /// マウスの動きにモデルが追従する向き(ドラッグ方向へモデルが動く)。
    /// </summary>
    public void Pan(double deltaXPixels, double deltaYPixels, double viewportHeightPixels)
    {
        var wpp = GetWorldPerPixel(viewportHeightPixels);
        var (right, up) = GetViewBasis();
        _target -= right * (float)(deltaXPixels * wpp);
        _target += up * (float)(deltaYPixels * wpp); // スクリーン Y は下向き
        RaiseChanged();
    }

    /// <summary>距離を factor 倍にする単純ズーム(factor &lt; 1 で接近)。</summary>
    public void Zoom(double factor)
    {
        _distance = Math.Max(_distance * factor, 1e-9);
        RaiseChanged();
    }

    /// <summary>
    /// カーソル位置固定ズーム。cursorX/Y はビューポート左上原点のピクセル座標。
    /// ズーム後もカーソル直下の点(注視点深度)が動かないよう注視点を平行移動する。
    /// </summary>
    public void ZoomAt(
        double factor,
        double cursorXPixels, double cursorYPixels,
        double viewportWidthPixels, double viewportHeightPixels)
    {
        var wpp = GetWorldPerPixel(viewportHeightPixels);
        var dx = cursorXPixels - viewportWidthPixels / 2.0;
        var dy = cursorYPixels - viewportHeightPixels / 2.0;

        var (right, up) = GetViewBasis();
        var cursorOffset = right * (float)(dx * wpp) - up * (float)(dy * wpp);

        _target += cursorOffset * (float)(1.0 - factor);
        _distance = Math.Max(_distance * factor, 1e-9);
        RaiseChanged();
    }

    /// <summary>
    /// 方位角・仰角を直接設定する(標準視点・視点アニメーション用)。
    /// <see cref="Pitch"/> プロパティと異なり真上/真下(±90°)まで許可する
    /// (ビュー行列側で up ベクトルの特異点を処理する)。
    /// </summary>
    public void SetOrientation(double yaw, double pitch)
    {
        _yaw = NormalizeAngle(yaw);
        _pitch = Math.Clamp(pitch, -Math.PI / 2.0, Math.PI / 2.0);
        RaiseChanged();
    }

    /// <summary>標準視点へ即座にジャンプする(補間は <see cref="WcuViewport.SetStandardView"/> が担う)。</summary>
    public void SetStandardView(ViewportStandardView view)
    {
        var (yaw, pitch) = GetStandardViewAngles(view, _upAxis);
        SetOrientation(yaw, pitch);
    }

    /// <summary>
    /// 標準視点の (Yaw, Pitch) ラジアン。
    /// Z-up: Front=-Y 側 / Right=+X 側から見る(CAD 慣例)。
    /// Y-up: Front=+Z 側 / Right=+X 側から見る(DCC 慣例)。
    /// Isometric は (1,±1,1) 方向(上方向軸成分が正)の等角投影。
    /// </summary>
    public static (double Yaw, double Pitch) GetStandardViewAngles(
        ViewportStandardView view, ViewportUpAxis upAxis)
    {
        var halfPi = Math.PI / 2.0;
        var isoPitch = Math.Asin(1.0 / Math.Sqrt(3.0)); // ≈ 35.264°

        // Yaw の定義: Z-up は eyeDir=(cosP·cosY, cosP·sinY, sinP)、
        //             Y-up は eyeDir=(cosP·sinY, sinP, cosP·cosY)
        return (view, upAxis) switch
        {
            (ViewportStandardView.Front, ViewportUpAxis.ZUp) => (-halfPi, 0.0),
            (ViewportStandardView.Back, ViewportUpAxis.ZUp) => (halfPi, 0.0),
            (ViewportStandardView.Right, ViewportUpAxis.ZUp) => (0.0, 0.0),
            (ViewportStandardView.Left, ViewportUpAxis.ZUp) => (Math.PI, 0.0),
            (ViewportStandardView.Top, ViewportUpAxis.ZUp) => (-halfPi, halfPi),
            (ViewportStandardView.Bottom, ViewportUpAxis.ZUp) => (-halfPi, -halfPi),
            (ViewportStandardView.Isometric, ViewportUpAxis.ZUp) => (-Math.PI / 4.0, isoPitch),

            (ViewportStandardView.Front, ViewportUpAxis.YUp) => (0.0, 0.0),
            (ViewportStandardView.Back, ViewportUpAxis.YUp) => (Math.PI, 0.0),
            (ViewportStandardView.Right, ViewportUpAxis.YUp) => (halfPi, 0.0),
            (ViewportStandardView.Left, ViewportUpAxis.YUp) => (-halfPi, 0.0),
            (ViewportStandardView.Top, ViewportUpAxis.YUp) => (0.0, halfPi),
            (ViewportStandardView.Bottom, ViewportUpAxis.YUp) => (0.0, -halfPi),
            (ViewportStandardView.Isometric, ViewportUpAxis.YUp) => (Math.PI / 4.0, isoPitch),

            _ => (0.0, 0.0),
        };
    }

    /// <summary>境界球にビュー全体を合わせる(注視点=中心、距離=全体が収まる距離)。</summary>
    public void FitToBounds(Bounds3D bounds, double margin = 1.1)
    {
        if (bounds.IsEmpty)
        {
            return;
        }

        var radius = Math.Max(bounds.Radius, 1e-9);
        _target = new Vector3((float)bounds.CenterX, (float)bounds.CenterY, (float)bounds.CenterZ);
        _sceneRadius = radius;
        _distance = radius * margin / Math.Sin(Math.Min(_fieldOfView, Math.PI / 2.0) / 2.0);
        RaiseChanged();
    }

    private static double NormalizeAngle(double angle)
    {
        var twoPi = 2.0 * Math.PI;
        angle %= twoPi;
        return angle < 0 ? angle + twoPi : angle;
    }

    private void SetField<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            RaiseChanged();
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
