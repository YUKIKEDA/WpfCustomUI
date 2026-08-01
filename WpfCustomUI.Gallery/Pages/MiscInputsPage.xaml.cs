using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using WpfCustomUI.Controls;

namespace WpfCustomUI.Gallery.Pages
{
    public partial class MiscInputsPage : UserControl
    {
        public record LoadCase(string Name);

        private readonly ObservableCollection<string> _selectedComponents = [];
        private readonly ObservableCollection<LoadCase> _selectedCases = [];

        public MiscInputsPage()
        {
            InitializeComponent();

            // CheckComboBox: 結果成分(件数表示+すべて選択)
            ComponentCombo.ItemsSource = new[]
            {
                "Sx", "Sy", "Sz", "Sxy", "Syz", "Szx", "Von Mises", "主応力 P1", "主応力 P2", "主応力 P3",
            };
            ComponentCombo.SelectedItems = _selectedComponents;
            _selectedComponents.CollectionChanged += OnSelectionChanged;
            _selectedComponents.Add("Sx");
            _selectedComponents.Add("Von Mises");

            // CheckComboBox: 荷重ケース(名前連結表示、DisplayMemberPath)
            var cases = new[]
            {
                new LoadCase("自重"), new LoadCase("風荷重 X"), new LoadCase("風荷重 Y"),
                new LoadCase("地震 X"), new LoadCase("地震 Y"), new LoadCase("温度"),
            };
            CaseCombo.ItemsSource = cases;
            CaseCombo.SelectedItems = _selectedCases;
            _selectedCases.Add(cases[0]);
            _selectedCases.Add(cases[3]);

            // MatrixBox: 対称剛性(GPa)と座標変換
            StiffnessMatrix.RowHeaders = new[] { "X", "Y", "Z" };
            StiffnessMatrix.ColumnHeaders = new[] { "X", "Y", "Z" };
            StiffnessMatrix.Values = new[,]
            {
                { 210.0, 80.0, 80.0 },
                { 80.0, 210.0, 80.0 },
                { 80.0, 80.0, 210.0 },
            };
            StiffnessMatrix.ValuesChangedHook(UpdateMatrixInfo);

            TransformMatrix.Values = new[,]
            {
                { 1.0, 0.0, 0.0 },
                { 0.0, 1.0, 0.0 },
                { 0.0, 0.0, 1.0 },
            };

            // KeyGestureBox
            RunGestureBox.Gesture = new System.Windows.Input.KeyGesture(
                System.Windows.Input.Key.F5, System.Windows.Input.ModifierKeys.Control);
            System.ComponentModel.DependencyPropertyDescriptor
                .FromProperty(KeyGestureBox.GestureProperty, typeof(KeyGestureBox))
                .AddValueChanged(RunGestureBox, (_, _) => UpdateGestureInfo());
            UpdateGestureInfo();

            // Wizard: Step 2 のチェックボックスで CanGoNext を制御
            SetupWizard.CurrentIndex = 0;
            UpdateWizardCanGoNext();

            UpdateComboInfo();
            UpdateMatrixInfo();
        }

        private void OnSelectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateComboInfo();

        private void UpdateComboInfo() =>
            ComboInfo.Text = $"SelectedItems: [{string.Join(", ", _selectedComponents)}]";

        private void UpdateMatrixInfo()
        {
            if (StiffnessMatrix.Values is not double[,] v)
            {
                return;
            }

            var sb = new StringBuilder("Values =");
            for (var r = 0; r < v.GetLength(0); r++)
            {
                sb.AppendLine().Append("  ");
                for (var c = 0; c < v.GetLength(1); c++)
                {
                    sb.Append($"{v[r, c],7:G4}");
                }
            }

            MatrixInfo.Text = sb.ToString();
        }

        private void UpdateGestureInfo() =>
            GestureInfo.Text = RunGestureBox.Gesture is { } g
                ? $"Gesture: {new System.Windows.Input.KeyGestureConverter().ConvertToString(g)}"
                : "Gesture: (未割り当て)";

        private void ConditionCheck_Changed(object sender, RoutedEventArgs e) => UpdateWizardCanGoNext();

        private void UpdateWizardCanGoNext()
        {
            // Step 2(条件設定)ではチェック済みのときだけ「次へ」を許可
            SetupWizard.CanGoNext = SetupWizard.CurrentIndex != 1 || ConditionCheck?.IsChecked == true;
        }

        private void SetupWizard_Navigating(object sender, WizardNavigatingEventArgs e)
        {
            // デモ: 遷移のたびに CanGoNext を再評価(ステップ依存の宣言的制御)
            Dispatcher.BeginInvoke(UpdateWizardCanGoNext);
        }

        private void SetupWizard_Finished(object sender, RoutedEventArgs e)
        {
            WizardInfo.Text = "Finished イベント発火(アプリがここでダイアログを閉じる)";
            SetupWizard.CurrentIndex = 0;
        }

        private void SetupWizard_Cancelled(object sender, RoutedEventArgs e)
        {
            WizardInfo.Text = "Cancelled イベント発火";
            SetupWizard.CurrentIndex = 0;
        }
    }

    file static class MatrixBoxExtensions
    {
        /// <summary>Values DP の変更をコードビハインドで購読する小ヘルパー(デモ用)。</summary>
        public static void ValuesChangedHook(this MatrixBox box, Action callback)
        {
            System.ComponentModel.DependencyPropertyDescriptor
                .FromProperty(MatrixBox.ValuesProperty, typeof(MatrixBox))
                .AddValueChanged(box, (_, _) => callback());
        }
    }
}
