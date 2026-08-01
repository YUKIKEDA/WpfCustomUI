using System.Collections;
using System.Windows.Media;

namespace WpfCustomUI.Controls;

/// <summary>文字列入力(TextBox エディタ)。</summary>
public class TextPropertyItem : PropertyItem
{
    private string? _value;

    public string? Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }
}

/// <summary>真偽値(CheckBox エディタ)。</summary>
public class BoolPropertyItem : PropertyItem
{
    private bool _value;

    public bool Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }
}

/// <summary>
/// 数値入力(NumericBox エディタ)。
/// 単位換算(IUnitProvider)と範囲・増分を持つ(spec 6.2)。
/// </summary>
public class NumericPropertyItem : PropertyItem
{
    private double? _value;
    private double _minimum = double.NegativeInfinity;
    private double _maximum = double.PositiveInfinity;
    private double _increment = 1.0;
    private string? _unit;
    private IUnitProvider? _unitProvider;
    private string _format = "G";

    /// <summary>内部値(基準単位)。null は未入力。</summary>
    public double? Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }

    public double Minimum
    {
        get => _minimum;
        set => SetField(ref _minimum, value);
    }

    public double Maximum
    {
        get => _maximum;
        set => SetField(ref _maximum, value);
    }

    public double Increment
    {
        get => _increment;
        set => SetField(ref _increment, value);
    }

    /// <summary>単位ラベル(換算なしの表示専用)。UnitProvider があればそちらが優先。</summary>
    public string? Unit
    {
        get => _unit;
        set => SetField(ref _unit, value);
    }

    public IUnitProvider? UnitProvider
    {
        get => _unitProvider;
        set => SetField(ref _unitProvider, value);
    }

    public string Format
    {
        get => _format;
        set => SetField(ref _format, value);
    }
}

/// <summary>選択肢からの選択(ComboBox エディタ)。</summary>
public class ChoicePropertyItem : PropertyItem
{
    private IEnumerable? _choices;
    private object? _value;

    /// <summary>選択肢のコレクション。</summary>
    public IEnumerable? Choices
    {
        get => _choices;
        set => SetField(ref _choices, value);
    }

    /// <summary>現在選択されている値。</summary>
    public object? Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }
}

/// <summary>ファイル/フォルダパス入力(PathBox エディタ。spec 6.9.6)。</summary>
public class PathPropertyItem : PropertyItem
{
    private string? _value;
    private PathBoxMode _mode = PathBoxMode.OpenFile;
    private string? _filter;
    private string? _dialogTitle;

    public string? Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }

    /// <summary>参照ボタンが開くダイアログの種類。</summary>
    public PathBoxMode Mode
    {
        get => _mode;
        set => SetField(ref _mode, value);
    }

    /// <summary>ファイルダイアログのフィルタ。</summary>
    public string? Filter
    {
        get => _filter;
        set => SetField(ref _filter, value);
    }

    public string? DialogTitle
    {
        get => _dialogTitle;
        set => SetField(ref _dialogTitle, value);
    }
}

/// <summary>色選択(ColorPicker エディタ。spec 6.9.6)。</summary>
public class ColorPropertyItem : PropertyItem
{
    private Color _value = Colors.White;
    private bool _isAlphaEnabled = true;

    public Color Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }

    /// <summary>アルファ(不透明度)の編集を許可するか。</summary>
    public bool IsAlphaEnabled
    {
        get => _isAlphaEnabled;
        set => SetField(ref _isAlphaEnabled, value);
    }
}

/// <summary>XYZ ベクトル入力(Vector3Box エディタ。spec 6.9.6)。</summary>
public class Vector3PropertyItem : PropertyItem
{
    private double? _x;
    private double? _y;
    private double? _z;
    private double _minimum = double.NegativeInfinity;
    private double _maximum = double.PositiveInfinity;
    private double _increment = 1.0;
    private string? _unit;
    private IUnitProvider? _unitProvider;
    private string _format = "G";

    /// <summary>X 成分(基準単位)。null は未入力。</summary>
    public double? X
    {
        get => _x;
        set => SetField(ref _x, value);
    }

    /// <summary>Y 成分(基準単位)。null は未入力。</summary>
    public double? Y
    {
        get => _y;
        set => SetField(ref _y, value);
    }

    /// <summary>Z 成分(基準単位)。null は未入力。</summary>
    public double? Z
    {
        get => _z;
        set => SetField(ref _z, value);
    }

    public double Minimum
    {
        get => _minimum;
        set => SetField(ref _minimum, value);
    }

    public double Maximum
    {
        get => _maximum;
        set => SetField(ref _maximum, value);
    }

    public double Increment
    {
        get => _increment;
        set => SetField(ref _increment, value);
    }

    /// <summary>単位ラベル(3軸共通)。UnitProvider があればそちらが優先。</summary>
    public string? Unit
    {
        get => _unit;
        set => SetField(ref _unit, value);
    }

    public IUnitProvider? UnitProvider
    {
        get => _unitProvider;
        set => SetField(ref _unitProvider, value);
    }

    public string Format
    {
        get => _format;
        set => SetField(ref _format, value);
    }
}
