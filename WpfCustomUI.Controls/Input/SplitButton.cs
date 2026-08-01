using System.Windows;

namespace WpfCustomUI.Controls;

/// <summary>
/// 左半分が通常のボタン(Command 実行)、右の ▼ がメニューを開くボタン(spec 6.9.1)。
/// メニューは <see cref="DropDownButton.DropDownMenu"/> に指定する。
/// </summary>
public class SplitButton : DropDownButton
{
    static SplitButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SplitButton), new FrameworkPropertyMetadata(typeof(SplitButton)));
    }

    /// <summary>本体クリックは通常のボタン動作のみ。メニューはテンプレートの PART_DropDownButton で開く。</summary>
    protected override bool OpensOnClick => false;
}
