using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WpfCustomUI.Controls;

/// <summary>
/// <see cref="ColorScale"/> の編集 UI(spec 6.11.2)。ColorMapLegend が「表示」、本コントロールが「設定」。
/// <list type="bullet">
/// <item>直接編集(ライブ反映)方式: <see cref="Scale"/> に受け取ったインスタンスのプロパティを
/// 直接書き換える。同じインスタンスを見ている凡例・コンター表示は即座に追従する。</item>
/// <item>適用/キャンセルが必要な場面は <see cref="ColorScale.Clone"/> したコピーを渡し、
/// OK 時に <see cref="ColorScale.CopyFrom"/> で書き戻す(WcuDialogWindow パターン)。</item>
/// <item>ラベル文字列は DP で差し替え可能(既定は英語。spec 5)。</item>
/// </list>
/// </summary>
public class ColorScaleEditor : Control
{
    private bool _updating;

    static ColorScaleEditor()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ColorScaleEditor), new FrameworkPropertyMetadata(typeof(ColorScaleEditor)));
    }

    #region Scale / ColorMaps / 単位

    public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(
        nameof(Scale), typeof(ColorScale), typeof(ColorScaleEditor),
        new PropertyMetadata(null, OnScaleChanged));

    /// <summary>編集対象のモデル。プロパティを直接書き換える(ライブ反映)。</summary>
    public ColorScale? Scale
    {
        get => (ColorScale?)GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    public static readonly DependencyProperty ColorMapsProperty = DependencyProperty.Register(
        nameof(ColorMaps), typeof(IEnumerable), typeof(ColorScaleEditor),
        new PropertyMetadata(ColorMap.BuiltIn));

    /// <summary>カラーマップの選択肢(既定は組み込みプリセット一覧)。</summary>
    public IEnumerable ColorMaps
    {
        get => (IEnumerable)GetValue(ColorMapsProperty);
        set => SetValue(ColorMapsProperty, value);
    }

    public static readonly DependencyProperty UnitProviderProperty = DependencyProperty.Register(
        nameof(UnitProvider), typeof(IUnitProvider), typeof(ColorScaleEditor), new PropertyMetadata(null));

    /// <summary>Min/Max 入力欄に透過する単位プロバイダー(spec 6.1)。</summary>
    public IUnitProvider? UnitProvider
    {
        get => (IUnitProvider?)GetValue(UnitProviderProperty);
        set => SetValue(UnitProviderProperty, value);
    }

    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(
        nameof(Format), typeof(string), typeof(ColorScaleEditor), new PropertyMetadata("G4"));

    /// <summary>Min/Max 入力欄の数値書式。</summary>
    public string Format
    {
        get => (string)GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    #endregion

    #region 編集用の中間 DP(テンプレートがバインドする)

    public static readonly DependencyProperty MinimumValueProperty = DependencyProperty.Register(
        nameof(MinimumValue), typeof(double?), typeof(ColorScaleEditor),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnEditValueChanged));

    /// <summary>Min 入力欄の値。null 確定は無効入力としてモデル値に戻す。</summary>
    public double? MinimumValue
    {
        get => (double?)GetValue(MinimumValueProperty);
        set => SetValue(MinimumValueProperty, value);
    }

    public static readonly DependencyProperty MaximumValueProperty = DependencyProperty.Register(
        nameof(MaximumValue), typeof(double?), typeof(ColorScaleEditor),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnEditValueChanged));

    /// <summary>Max 入力欄の値。null 確定は無効入力としてモデル値に戻す。</summary>
    public double? MaximumValue
    {
        get => (double?)GetValue(MaximumValueProperty);
        set => SetValue(MaximumValueProperty, value);
    }

    public static readonly DependencyProperty IsDiscreteProperty = DependencyProperty.Register(
        nameof(IsDiscrete), typeof(bool), typeof(ColorScaleEditor),
        new FrameworkPropertyMetadata(false,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnEditValueChanged));

    /// <summary>離散レベル分割の有効/無効(Scale.LevelCount の null ⇔ 値に対応)。</summary>
    public bool IsDiscrete
    {
        get => (bool)GetValue(IsDiscreteProperty);
        set => SetValue(IsDiscreteProperty, value);
    }

    public static readonly DependencyProperty DiscreteLevelsProperty = DependencyProperty.Register(
        nameof(DiscreteLevels), typeof(double?), typeof(ColorScaleEditor),
        new FrameworkPropertyMetadata(10.0,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnEditValueChanged));

    /// <summary>離散レベル数(2〜256)。</summary>
    public double? DiscreteLevels
    {
        get => (double?)GetValue(DiscreteLevelsProperty);
        set => SetValue(DiscreteLevelsProperty, value);
    }

    public static readonly DependencyProperty ClampBelowProperty = DependencyProperty.Register(
        nameof(ClampBelow), typeof(bool), typeof(ColorScaleEditor),
        new FrameworkPropertyMetadata(true,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnEditValueChanged));

    /// <summary>下限未満をカラーマップ下端色にクランプするか(Scale.BelowRangeColor=null に対応)。</summary>
    public bool ClampBelow
    {
        get => (bool)GetValue(ClampBelowProperty);
        set => SetValue(ClampBelowProperty, value);
    }

    public static readonly DependencyProperty BelowColorProperty = DependencyProperty.Register(
        nameof(BelowColor), typeof(Color), typeof(ColorScaleEditor),
        new FrameworkPropertyMetadata(Colors.DarkGray,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnEditValueChanged));

    /// <summary>下限未満に使う色(クランプ解除時)。</summary>
    public Color BelowColor
    {
        get => (Color)GetValue(BelowColorProperty);
        set => SetValue(BelowColorProperty, value);
    }

    public static readonly DependencyProperty ClampAboveProperty = DependencyProperty.Register(
        nameof(ClampAbove), typeof(bool), typeof(ColorScaleEditor),
        new FrameworkPropertyMetadata(true,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnEditValueChanged));

    /// <summary>上限超過をカラーマップ上端色にクランプするか(Scale.AboveRangeColor=null に対応)。</summary>
    public bool ClampAbove
    {
        get => (bool)GetValue(ClampAboveProperty);
        set => SetValue(ClampAboveProperty, value);
    }

    public static readonly DependencyProperty AboveColorProperty = DependencyProperty.Register(
        nameof(AboveColor), typeof(Color), typeof(ColorScaleEditor),
        new FrameworkPropertyMetadata(Colors.DarkGray,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnEditValueChanged));

    /// <summary>上限超過に使う色(クランプ解除時)。</summary>
    public Color AboveColor
    {
        get => (Color)GetValue(AboveColorProperty);
        set => SetValue(AboveColorProperty, value);
    }

    private static readonly DependencyPropertyKey CanUseLogPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(CanUseLog), typeof(bool), typeof(ColorScaleEditor), new PropertyMetadata(false));

    public static readonly DependencyProperty CanUseLogProperty = CanUseLogPropertyKey.DependencyProperty;

    /// <summary>対数スケールが選択可能か(Min/Max がともに正。読み取り専用)。</summary>
    public bool CanUseLog => (bool)GetValue(CanUseLogProperty);

    #endregion

    #region ラベル文字列(既定は英語。spec 5)

    public static readonly DependencyProperty ColorMapLabelProperty = DependencyProperty.Register(
        nameof(ColorMapLabel), typeof(string), typeof(ColorScaleEditor), new PropertyMetadata("Color map"));

    public string ColorMapLabel
    {
        get => (string)GetValue(ColorMapLabelProperty);
        set => SetValue(ColorMapLabelProperty, value);
    }

    public static readonly DependencyProperty MinimumLabelProperty = DependencyProperty.Register(
        nameof(MinimumLabel), typeof(string), typeof(ColorScaleEditor), new PropertyMetadata("Min"));

    public string MinimumLabel
    {
        get => (string)GetValue(MinimumLabelProperty);
        set => SetValue(MinimumLabelProperty, value);
    }

    public static readonly DependencyProperty MaximumLabelProperty = DependencyProperty.Register(
        nameof(MaximumLabel), typeof(string), typeof(ColorScaleEditor), new PropertyMetadata("Max"));

    public string MaximumLabel
    {
        get => (string)GetValue(MaximumLabelProperty);
        set => SetValue(MaximumLabelProperty, value);
    }

    public static readonly DependencyProperty DiscreteLevelsLabelProperty = DependencyProperty.Register(
        nameof(DiscreteLevelsLabel), typeof(string), typeof(ColorScaleEditor), new PropertyMetadata("Discrete levels"));

    public string DiscreteLevelsLabel
    {
        get => (string)GetValue(DiscreteLevelsLabelProperty);
        set => SetValue(DiscreteLevelsLabelProperty, value);
    }

    public static readonly DependencyProperty LogarithmicLabelProperty = DependencyProperty.Register(
        nameof(LogarithmicLabel), typeof(string), typeof(ColorScaleEditor), new PropertyMetadata("Log scale"));

    public string LogarithmicLabel
    {
        get => (string)GetValue(LogarithmicLabelProperty);
        set => SetValue(LogarithmicLabelProperty, value);
    }

    public static readonly DependencyProperty LogDisabledHintProperty = DependencyProperty.Register(
        nameof(LogDisabledHint), typeof(string), typeof(ColorScaleEditor),
        new PropertyMetadata("Log scale requires positive Min and Max."));

    public string LogDisabledHint
    {
        get => (string)GetValue(LogDisabledHintProperty);
        set => SetValue(LogDisabledHintProperty, value);
    }

    public static readonly DependencyProperty AdvancedLabelProperty = DependencyProperty.Register(
        nameof(AdvancedLabel), typeof(string), typeof(ColorScaleEditor), new PropertyMetadata("Advanced"));

    public string AdvancedLabel
    {
        get => (string)GetValue(AdvancedLabelProperty);
        set => SetValue(AdvancedLabelProperty, value);
    }

    public static readonly DependencyProperty BelowRangeLabelProperty = DependencyProperty.Register(
        nameof(BelowRangeLabel), typeof(string), typeof(ColorScaleEditor), new PropertyMetadata("Below range"));

    public string BelowRangeLabel
    {
        get => (string)GetValue(BelowRangeLabelProperty);
        set => SetValue(BelowRangeLabelProperty, value);
    }

    public static readonly DependencyProperty AboveRangeLabelProperty = DependencyProperty.Register(
        nameof(AboveRangeLabel), typeof(string), typeof(ColorScaleEditor), new PropertyMetadata("Above range"));

    public string AboveRangeLabel
    {
        get => (string)GetValue(AboveRangeLabelProperty);
        set => SetValue(AboveRangeLabelProperty, value);
    }

    public static readonly DependencyProperty NaNColorLabelProperty = DependencyProperty.Register(
        nameof(NaNColorLabel), typeof(string), typeof(ColorScaleEditor), new PropertyMetadata("NaN color"));

    public string NaNColorLabel
    {
        get => (string)GetValue(NaNColorLabelProperty);
        set => SetValue(NaNColorLabelProperty, value);
    }

    public static readonly DependencyProperty ClampLabelProperty = DependencyProperty.Register(
        nameof(ClampLabel), typeof(string), typeof(ColorScaleEditor), new PropertyMetadata("Clamp"));

    public string ClampLabel
    {
        get => (string)GetValue(ClampLabelProperty);
        set => SetValue(ClampLabelProperty, value);
    }

    #endregion

    #region 同期ロジック(Scale ⇔ 中間 DP)

    private static void OnScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (ColorScaleEditor)d;
        if (e.OldValue is ColorScale oldScale)
        {
            oldScale.PropertyChanged -= editor.OnScalePropertyChanged;
        }

        if (e.NewValue is ColorScale newScale)
        {
            newScale.PropertyChanged += editor.OnScalePropertyChanged;
        }

        editor.SyncFromScale();
    }

    private void OnScalePropertyChanged(object? sender, PropertyChangedEventArgs e) => SyncFromScale();

    /// <summary>モデル → 中間 DP。</summary>
    private void SyncFromScale()
    {
        if (_updating)
        {
            return;
        }

        _updating = true;
        try
        {
            var scale = Scale;
            if (scale is null)
            {
                SetValue(CanUseLogPropertyKey, false);
                return;
            }

            SetCurrentValue(MinimumValueProperty, scale.Minimum);
            SetCurrentValue(MaximumValueProperty, scale.Maximum);
            SetCurrentValue(IsDiscreteProperty, scale.LevelCount is not null);
            if (scale.LevelCount is int levels)
            {
                SetCurrentValue(DiscreteLevelsProperty, (double)levels);
            }

            SetCurrentValue(ClampBelowProperty, scale.BelowRangeColor is null);
            if (scale.BelowRangeColor is Color below)
            {
                SetCurrentValue(BelowColorProperty, below);
            }

            SetCurrentValue(ClampAboveProperty, scale.AboveRangeColor is null);
            if (scale.AboveRangeColor is Color above)
            {
                SetCurrentValue(AboveColorProperty, above);
            }

            SetValue(CanUseLogPropertyKey, scale.Minimum > 0 && scale.Maximum > 0);
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>中間 DP → モデル。</summary>
    private static void OnEditValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (ColorScaleEditor)d;
        if (editor._updating || editor.Scale is not ColorScale scale)
        {
            return;
        }

        editor._updating = true;
        try
        {
            if (e.Property == MinimumValueProperty || e.Property == MaximumValueProperty)
            {
                // null 確定(空欄)は無効入力としてモデル値へ戻す
                if (editor.MinimumValue is double min)
                {
                    scale.Minimum = min;
                }
                else
                {
                    editor.SetCurrentValue(MinimumValueProperty, scale.Minimum);
                }

                if (editor.MaximumValue is double max)
                {
                    scale.Maximum = max;
                }
                else
                {
                    editor.SetCurrentValue(MaximumValueProperty, scale.Maximum);
                }

                editor.SetValue(CanUseLogPropertyKey, scale.Minimum > 0 && scale.Maximum > 0);
            }
            else if (e.Property == IsDiscreteProperty || e.Property == DiscreteLevelsProperty)
            {
                if (editor.DiscreteLevels is not double levels)
                {
                    editor.SetCurrentValue(DiscreteLevelsProperty, (double)(scale.LevelCount ?? 10));
                    levels = (double)editor.DiscreteLevels!;
                }

                scale.LevelCount = editor.IsDiscrete ? (int)Math.Round(levels) : null;
            }
            else if (e.Property == ClampBelowProperty || e.Property == BelowColorProperty)
            {
                scale.BelowRangeColor = editor.ClampBelow ? null : editor.BelowColor;
            }
            else if (e.Property == ClampAboveProperty || e.Property == AboveColorProperty)
            {
                scale.AboveRangeColor = editor.ClampAbove ? null : editor.AboveColor;
            }
        }
        finally
        {
            editor._updating = false;
        }
    }

    #endregion
}
