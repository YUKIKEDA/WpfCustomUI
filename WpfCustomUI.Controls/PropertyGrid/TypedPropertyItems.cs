using System.Collections;

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
