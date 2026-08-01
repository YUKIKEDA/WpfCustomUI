using System.Numerics;
using WpfCustomUI.Viewport3D;

namespace WpfCustomUI.Viewport3D.Tests;

public class StandardViewTests
{
    private const float Tolerance = 1e-4f;

    private static ViewportCamera CreateCamera(ViewportUpAxis upAxis) => new()
    {
        Target = Vector3.Zero,
        Distance = 10.0,
        SceneRadius = 5.0,
        UpAxis = upAxis,
    };

    [Theory]
    [InlineData(ViewportStandardView.Front, 0.0f, -1.0f, 0.0f)]
    [InlineData(ViewportStandardView.Back, 0.0f, 1.0f, 0.0f)]
    [InlineData(ViewportStandardView.Right, 1.0f, 0.0f, 0.0f)]
    [InlineData(ViewportStandardView.Left, -1.0f, 0.0f, 0.0f)]
    [InlineData(ViewportStandardView.Top, 0.0f, 0.0f, 1.0f)]
    [InlineData(ViewportStandardView.Bottom, 0.0f, 0.0f, -1.0f)]
    public void StandardView_ZUp_EyeDirectionMatchesAxis(
        ViewportStandardView view, float x, float y, float z)
    {
        var camera = CreateCamera(ViewportUpAxis.ZUp);
        camera.SetStandardView(view);

        var dir = camera.GetEyeDirection();
        Assert.Equal(x, dir.X, Tolerance);
        Assert.Equal(y, dir.Y, Tolerance);
        Assert.Equal(z, dir.Z, Tolerance);
    }

    [Theory]
    [InlineData(ViewportStandardView.Front, 0.0f, 0.0f, 1.0f)]
    [InlineData(ViewportStandardView.Back, 0.0f, 0.0f, -1.0f)]
    [InlineData(ViewportStandardView.Right, 1.0f, 0.0f, 0.0f)]
    [InlineData(ViewportStandardView.Left, -1.0f, 0.0f, 0.0f)]
    [InlineData(ViewportStandardView.Top, 0.0f, 1.0f, 0.0f)]
    [InlineData(ViewportStandardView.Bottom, 0.0f, -1.0f, 0.0f)]
    public void StandardView_YUp_EyeDirectionMatchesAxis(
        ViewportStandardView view, float x, float y, float z)
    {
        var camera = CreateCamera(ViewportUpAxis.YUp);
        camera.SetStandardView(view);

        var dir = camera.GetEyeDirection();
        Assert.Equal(x, dir.X, Tolerance);
        Assert.Equal(y, dir.Y, Tolerance);
        Assert.Equal(z, dir.Z, Tolerance);
    }

    [Theory]
    [InlineData(ViewportUpAxis.ZUp)]
    [InlineData(ViewportUpAxis.YUp)]
    public void StandardView_Isometric_EyeDirectionIsDiagonal(ViewportUpAxis upAxis)
    {
        var camera = CreateCamera(upAxis);
        camera.SetStandardView(ViewportStandardView.Isometric);

        var dir = camera.GetEyeDirection();
        var expected = 1.0f / MathF.Sqrt(3.0f);

        // 各成分の絶対値が 1/√3(等角)で、上方向軸成分が正
        Assert.Equal(expected, Math.Abs(dir.X), Tolerance);
        Assert.Equal(expected, Math.Abs(dir.Y), Tolerance);
        Assert.Equal(expected, Math.Abs(dir.Z), Tolerance);
        var upComponent = upAxis == ViewportUpAxis.ZUp ? dir.Z : dir.Y;
        Assert.True(upComponent > 0.0f);
    }

    [Theory]
    [InlineData(ViewportStandardView.Top)]
    [InlineData(ViewportStandardView.Bottom)]
    public void StandardView_TopBottom_ViewMatrixIsValid(ViewportStandardView view)
    {
        // 真上/真下は視線と上方向軸が平行になる特異点。up フォールバックで行列が壊れないこと
        var camera = CreateCamera(ViewportUpAxis.ZUp);
        camera.SetStandardView(view);

        var viewMatrix = camera.GetViewMatrix();
        var projMatrix = camera.GetProjectionMatrix(1.5);
        var viewProj = viewMatrix * projMatrix;

        Assert.False(float.IsNaN(viewMatrix.M11));

        // 注視点は画面中央に写る
        var clip = Vector4.Transform(new Vector4(Vector3.Zero, 1.0f), viewProj);
        Assert.True(clip.W > 0.0f);
        Assert.Equal(0.0f, clip.X / clip.W, 1e-3f);
        Assert.Equal(0.0f, clip.Y / clip.W, 1e-3f);
    }

    [Fact]
    public void StandardView_TopZUp_ScreenUpIsPlusY()
    {
        // Z-up の真上視点は +Y が画面上(CAD 慣例)
        var camera = CreateCamera(ViewportUpAxis.ZUp);
        camera.SetStandardView(ViewportStandardView.Top);

        var (right, up) = camera.GetViewBasis();
        Assert.Equal(1.0f, up.Y, Tolerance);
        Assert.Equal(1.0f, right.X, Tolerance);
    }

    [Fact]
    public void SetOrientation_AllowsExactVertical_WhilePitchPropertyClamps()
    {
        var camera = CreateCamera(ViewportUpAxis.ZUp);

        camera.SetOrientation(0.0, Math.PI / 2.0);
        Assert.Equal(Math.PI / 2.0, camera.Pitch, 1e-9);

        // プロパティ経由(インタラクティブ操作の経路)は従来どおりクランプ
        camera.Pitch = Math.PI / 2.0;
        Assert.True(camera.Pitch < Math.PI / 2.0);
    }

    [Fact]
    public void SetStandardView_PreservesTargetAndDistance()
    {
        var camera = CreateCamera(ViewportUpAxis.ZUp);
        camera.Target = new Vector3(1.0f, 2.0f, 3.0f);
        camera.Distance = 42.0;

        camera.SetStandardView(ViewportStandardView.Isometric);

        Assert.Equal(1.0f, camera.Target.X, Tolerance);
        Assert.Equal(42.0, camera.Distance, 1e-9);
    }

    [Theory]
    [InlineData(0, ViewportUpAxis.ZUp, ViewportStandardView.Right)]
    [InlineData(1, ViewportUpAxis.ZUp, ViewportStandardView.Left)]
    [InlineData(2, ViewportUpAxis.ZUp, ViewportStandardView.Back)]
    [InlineData(3, ViewportUpAxis.ZUp, ViewportStandardView.Front)]
    [InlineData(4, ViewportUpAxis.ZUp, ViewportStandardView.Top)]
    [InlineData(5, ViewportUpAxis.ZUp, ViewportStandardView.Bottom)]
    [InlineData(2, ViewportUpAxis.YUp, ViewportStandardView.Top)]
    [InlineData(4, ViewportUpAxis.YUp, ViewportStandardView.Front)]
    public void ViewCubeFaceMapping_MatchesUpAxisConvention(
        int faceIndex, ViewportUpAxis upAxis, ViewportStandardView expected)
    {
        Assert.Equal(expected, ViewCubeOverlay.GetFaceView(faceIndex, upAxis));
    }
}
