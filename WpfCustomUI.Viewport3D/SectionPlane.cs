using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WpfCustomUI.Viewport3D;

/// <summary>
/// 断面カットのクリッピング平面(spec 6.19.3)。通過点+法線で定義し、
/// **法線側(正の半空間)が表示されて残る**。座標は再センタリング前のモデル座標
/// (<see cref="ViewportMesh.Positions"/> と同じ系)で指定する。
/// <para>
/// プロパティ変更は通知され、<see cref="WcuViewport.SectionPlane"/> に設定済みでも
/// 表示が自動追従する(オフセット Slider ドラッグ等)。どちら側を残すかの反転は
/// 法線の符号を逆にすればよい。
/// </para>
/// </summary>
public sealed class SectionPlane : INotifyPropertyChanged
{
    private double _originX;
    private double _originY;
    private double _originZ;
    private double _normalX;
    private double _normalY;
    private double _normalZ = 1.0;

    public SectionPlane()
    {
    }

    public SectionPlane(
        double originX, double originY, double originZ,
        double normalX, double normalY, double normalZ)
    {
        _originX = originX;
        _originY = originY;
        _originZ = originZ;
        _normalX = normalX;
        _normalY = normalY;
        _normalZ = normalZ;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>平面が通る点の X 座標(モデル座標)。</summary>
    public double OriginX
    {
        get => _originX;
        set => SetField(ref _originX, value);
    }

    public double OriginY
    {
        get => _originY;
        set => SetField(ref _originY, value);
    }

    public double OriginZ
    {
        get => _originZ;
        set => SetField(ref _originZ, value);
    }

    /// <summary>法線の X 成分(正規化不要。この側が表示されて残る)。既定は +Z。</summary>
    public double NormalX
    {
        get => _normalX;
        set => SetField(ref _normalX, value);
    }

    public double NormalY
    {
        get => _normalY;
        set => SetField(ref _normalY, value);
    }

    public double NormalZ
    {
        get => _normalZ;
        set => SetField(ref _normalZ, value);
    }

    private void SetField(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        if (field != value)
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
