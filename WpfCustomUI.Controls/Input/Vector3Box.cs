using System.Windows;
using System.Windows.Controls;

namespace WpfCustomUI.Controls;

/// <summary>
/// XYZ の3成分をまとめて入力する複合コントロール(spec 6.9.5)。NumericBox×3 で構成される。
/// 値は NumericBox と同じ nullable 規約(<c>double?</c>、null は未入力)で
/// X / Y / Z を個別の依存関係プロパティとして公開する。
/// 単位(<see cref="UnitProvider"/>)・範囲・増分は3軸共通で一括指定する。
/// </summary>
public class Vector3Box : Control
{
    static Vector3Box()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(Vector3Box), new FrameworkPropertyMetadata(typeof(Vector3Box)));
    }

    public static readonly DependencyProperty XProperty = DependencyProperty.Register(
        nameof(X), typeof(double?), typeof(Vector3Box),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>X 成分(基準単位)。null は未入力。</summary>
    public double? X
    {
        get => (double?)GetValue(XProperty);
        set => SetValue(XProperty, value);
    }

    public static readonly DependencyProperty YProperty = DependencyProperty.Register(
        nameof(Y), typeof(double?), typeof(Vector3Box),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>Y 成分(基準単位)。null は未入力。</summary>
    public double? Y
    {
        get => (double?)GetValue(YProperty);
        set => SetValue(YProperty, value);
    }

    public static readonly DependencyProperty ZProperty = DependencyProperty.Register(
        nameof(Z), typeof(double?), typeof(Vector3Box),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>Z 成分(基準単位)。null は未入力。</summary>
    public double? Z
    {
        get => (double?)GetValue(ZProperty);
        set => SetValue(ZProperty, value);
    }

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(Vector3Box), new PropertyMetadata(double.NegativeInfinity));

    /// <summary>3軸共通の下限(基準単位)。</summary>
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Vector3Box), new PropertyMetadata(double.PositiveInfinity));

    /// <summary>3軸共通の上限(基準単位)。</summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public static readonly DependencyProperty IncrementProperty = DependencyProperty.Register(
        nameof(Increment), typeof(double), typeof(Vector3Box), new PropertyMetadata(1.0));

    /// <summary>増減ボタン等の1ステップ量(表示単位)。</summary>
    public double Increment
    {
        get => (double)GetValue(IncrementProperty);
        set => SetValue(IncrementProperty, value);
    }

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(Vector3Box), new PropertyMetadata(null));

    /// <summary>単位ラベル(換算なしの表示専用)。UnitProvider があればそちらが優先。</summary>
    public string? Unit
    {
        get => (string?)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public static readonly DependencyProperty UnitProviderProperty = DependencyProperty.Register(
        nameof(UnitProvider), typeof(IUnitProvider), typeof(Vector3Box), new PropertyMetadata(null));

    /// <summary>表示単位と内部値の換算を担うプロバイダー(3軸共通)。</summary>
    public IUnitProvider? UnitProvider
    {
        get => (IUnitProvider?)GetValue(UnitProviderProperty);
        set => SetValue(UnitProviderProperty, value);
    }

    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(
        nameof(Format), typeof(string), typeof(Vector3Box), new PropertyMetadata("G"));

    /// <summary>表示用の数値書式。</summary>
    public string Format
    {
        get => (string)GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(Vector3Box), new PropertyMetadata(false));

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    // 軸ラベルは差し替え可能にする(spec 5 の文字列方針)
    public static readonly DependencyProperty XLabelProperty = DependencyProperty.Register(
        nameof(XLabel), typeof(string), typeof(Vector3Box), new PropertyMetadata("X"));

    public string XLabel
    {
        get => (string)GetValue(XLabelProperty);
        set => SetValue(XLabelProperty, value);
    }

    public static readonly DependencyProperty YLabelProperty = DependencyProperty.Register(
        nameof(YLabel), typeof(string), typeof(Vector3Box), new PropertyMetadata("Y"));

    public string YLabel
    {
        get => (string)GetValue(YLabelProperty);
        set => SetValue(YLabelProperty, value);
    }

    public static readonly DependencyProperty ZLabelProperty = DependencyProperty.Register(
        nameof(ZLabel), typeof(string), typeof(Vector3Box), new PropertyMetadata("Z"));

    public string ZLabel
    {
        get => (string)GetValue(ZLabelProperty);
        set => SetValue(ZLabelProperty, value);
    }
}
