using System.Windows;
using System.Windows.Controls.Primitives;

namespace WpfCustomUI.Controls;

/// <summary>
/// ON/OFF トグルスイッチ(spec 6.10.3)。ToggleButton 派生のため
/// IsChecked / Command 等はそのまま使える。
/// ラベルは右側の Content で指定する(On/Off 文字列は内蔵しない)。
/// 高密度 UI 基準(ControlHeight 24px)に収まるサイズ。三状態は非対応。
/// </summary>
public class ToggleSwitch : ToggleButton
{
    static ToggleSwitch()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ToggleSwitch), new FrameworkPropertyMetadata(typeof(ToggleSwitch)));
    }

    /// <summary>三状態は非対応(spec 6.10.3)。null が来ても false 扱いにする。</summary>
    protected override void OnToggle()
    {
        SetCurrentValue(IsCheckedProperty, IsChecked != true);
    }
}
