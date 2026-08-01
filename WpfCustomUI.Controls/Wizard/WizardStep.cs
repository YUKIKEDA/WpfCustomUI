using System.Windows;
using System.Windows.Controls;

namespace WpfCustomUI.Controls;

/// <summary>
/// <see cref="Wizard"/> の1ステップ(spec 6.12.5)。
/// Header がステップインジケーターの表示名、Content がページ本体になる。
/// </summary>
public class WizardStep : HeaderedContentControl
{
    static WizardStep()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(WizardStep), new FrameworkPropertyMetadata(typeof(WizardStep)));
    }
}
