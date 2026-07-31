using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfCustomUI.Controls;

namespace WpfCustomUI.Gallery.Pages
{
    public partial class ModelTreePage : UserControl
    {
        private static readonly Geometry PartIcon = Geometry.Parse(
            "M8,1.5 L14,4.75 L14,11.25 L8,14.5 L2,11.25 L2,4.75 Z M8,3.2 L4,5.4 L8,7.6 L12,5.4 Z");

        private readonly TreeNode[] _roots;

        public ModelTreePage()
        {
            InitializeComponent();

            var resultSets = new TreeNode { Name = "Result Sets" };
            for (var i = 1; i <= 2000; i++)
            {
                resultSets.Children.Add(new TreeNode { Name = $"Frame {i:D4}" });
            }

            var assembly = new TreeNode { Name = "Assembly", IsExpanded = true };
            assembly.Children.Add(MakePart("Bracket-01"));
            assembly.Children.Add(MakePart("Bracket-02"));
            assembly.Children.Add(MakePart("Base-Plate"));

            var analysis = new TreeNode { Name = "解析ケース", IsExpanded = true };
            analysis.Children.Add(new TreeNode { Name = "静解析 (Case 1)" });
            analysis.Children.Add(new TreeNode { Name = "固有値解析 (Case 2)" });

            _roots = [assembly, analysis, resultSets];
            Tree.ItemsSource = _roots;
        }

        private static TreeNode MakePart(string name)
        {
            var part = new TreeNode { Name = name, Icon = PartIcon };
            part.Children.Add(new TreeNode { Name = "メッシュ" });
            part.Children.Add(new TreeNode { Name = "材料: SS400" });
            var loads = new TreeNode { Name = "境界条件" };
            loads.Children.Add(new TreeNode { Name = "固定拘束" });
            loads.Children.Add(new TreeNode { Name = "荷重 1000 N" });
            part.Children.Add(loads);
            return part;
        }

        private void OnTreeSelectionChanged(object sender, SelectionChangedEventArgs e) =>
            SelectionInfo.Text = $"選択: {Tree.GetSelectedNodes().Count} 個";

        private void OnExpandAll(object sender, RoutedEventArgs e) => SetExpandedAll(true);

        private void OnCollapseAll(object sender, RoutedEventArgs e) => SetExpandedAll(false);

        private void SetExpandedAll(bool isExpanded)
        {
            foreach (var root in _roots)
            {
                SetExpandedRecursive(root, isExpanded);
            }
        }

        private static void SetExpandedRecursive(ITreeNode node, bool isExpanded)
        {
            node.IsExpanded = isExpanded;
            foreach (var child in node.Children)
            {
                SetExpandedRecursive(child, isExpanded);
            }
        }
    }
}
