using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace WpfCustomUI.Controls;

/// <summary>
/// 数値行列の入力コントロール(spec 6.12.2)。異方性材料・座標変換・慣性テンソル入力用。
/// <list type="bullet">
/// <item>値は <see cref="Values"/>(double[,])。セル確定のたびに新しい配列インスタンスを
/// 作って DP にセットする(TwoWay バインドで VM に届く)。</item>
/// <item><see cref="IsSymmetric"/> で (i,j) 編集時に (j,i) へ自動ミラー。</item>
/// <item>単位・書式・範囲は全セルの NumericBox へ一括透過(Vector3Box と同じ流儀)。</item>
/// </list>
/// </summary>
[TemplatePart(Name = PartGrid, Type = typeof(Grid))]
public class MatrixBox : Control
{
    private const string PartGrid = "PART_Grid";

    private Grid? _grid;
    private MatrixCell[,]? _cells;
    private bool _updating;

    static MatrixBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(MatrixBox), new FrameworkPropertyMetadata(typeof(MatrixBox)));
    }

    #region Dependency properties

    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(double[,]), typeof(MatrixBox),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValuesChanged));

    /// <summary>
    /// 行列値(基準単位)。編集確定のたびに新しいインスタンスに差し替わる。
    /// null または次元不足の位置は未入力(空欄)として表示される。
    /// </summary>
    public double[,]? Values
    {
        get => (double[,]?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(int), typeof(MatrixBox),
        new PropertyMetadata(3, OnStructureChanged), v => (int)v >= 1);

    /// <summary>行数(既定 3)。Values の次元と不一致ならこちらを優先して表示する。</summary>
    public int Rows
    {
        get => (int)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(MatrixBox),
        new PropertyMetadata(3, OnStructureChanged), v => (int)v >= 1);

    /// <summary>列数(既定 3)。</summary>
    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public static readonly DependencyProperty IsSymmetricProperty = DependencyProperty.Register(
        nameof(IsSymmetric), typeof(bool), typeof(MatrixBox), new PropertyMetadata(false));

    /// <summary>true にすると (i,j) の編集が (j,i) にミラーされる(剛性・コンプライアンス行列用)。</summary>
    public bool IsSymmetric
    {
        get => (bool)GetValue(IsSymmetricProperty);
        set => SetValue(IsSymmetricProperty, value);
    }

    public static readonly DependencyProperty RowHeadersProperty = DependencyProperty.Register(
        nameof(RowHeaders), typeof(IEnumerable), typeof(MatrixBox),
        new PropertyMetadata(null, OnStructureChanged));

    /// <summary>行ヘッダー(例 X/Y/Z)。null なら非表示。</summary>
    public IEnumerable? RowHeaders
    {
        get => (IEnumerable?)GetValue(RowHeadersProperty);
        set => SetValue(RowHeadersProperty, value);
    }

    public static readonly DependencyProperty ColumnHeadersProperty = DependencyProperty.Register(
        nameof(ColumnHeaders), typeof(IEnumerable), typeof(MatrixBox),
        new PropertyMetadata(null, OnStructureChanged));

    /// <summary>列ヘッダー。null なら非表示。</summary>
    public IEnumerable? ColumnHeaders
    {
        get => (IEnumerable?)GetValue(ColumnHeadersProperty);
        set => SetValue(ColumnHeadersProperty, value);
    }

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(MatrixBox), new PropertyMetadata(double.NegativeInfinity));

    /// <summary>全セル共通の下限(基準単位)。</summary>
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(MatrixBox), new PropertyMetadata(double.PositiveInfinity));

    /// <summary>全セル共通の上限(基準単位)。</summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public static readonly DependencyProperty IncrementProperty = DependencyProperty.Register(
        nameof(Increment), typeof(double), typeof(MatrixBox), new PropertyMetadata(1.0));

    /// <summary>全セル共通の増減ステップ。</summary>
    public double Increment
    {
        get => (double)GetValue(IncrementProperty);
        set => SetValue(IncrementProperty, value);
    }

    public static readonly DependencyProperty UnitProviderProperty = DependencyProperty.Register(
        nameof(UnitProvider), typeof(IUnitProvider), typeof(MatrixBox), new PropertyMetadata(null));

    /// <summary>全セル共通の単位プロバイダー(spec 6.1)。</summary>
    public IUnitProvider? UnitProvider
    {
        get => (IUnitProvider?)GetValue(UnitProviderProperty);
        set => SetValue(UnitProviderProperty, value);
    }

    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(
        nameof(Format), typeof(string), typeof(MatrixBox), new PropertyMetadata("G"));

    /// <summary>全セル共通の数値書式。</summary>
    public string Format
    {
        get => (string)GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    #endregion

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _grid = GetTemplateChild(PartGrid) as Grid;
        RebuildGrid();
    }

    private static void OnStructureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MatrixBox)d).RebuildGrid();

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (MatrixBox)d;
        if (!box._updating)
        {
            box.SyncCellsFromValues();
        }
    }

    /// <summary>グリッド構造(行数・列数・ヘッダー)を作り直す。</summary>
    private void RebuildGrid()
    {
        if (_grid is null)
        {
            return;
        }

        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        _grid.ColumnDefinitions.Clear();

        var rows = Rows;
        var cols = Columns;
        var rowHeaders = RowHeaders?.Cast<object>().ToArray();
        var colHeaders = ColumnHeaders?.Cast<object>().ToArray();
        var headerCol = rowHeaders is not null ? 1 : 0;
        var headerRow = colHeaders is not null ? 1 : 0;

        if (headerCol > 0)
        {
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        for (var c = 0; c < cols; c++)
        {
            _grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
                MinWidth = 56,
            });
        }

        if (headerRow > 0)
        {
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var r = 0; r < rows; r++)
        {
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var c = 0; c < cols && colHeaders is not null; c++)
        {
            _grid.Children.Add(CreateHeader(colHeaders.ElementAtOrDefault(c), 0, c + headerCol));
        }

        for (var r = 0; r < rows && rowHeaders is not null; r++)
        {
            _grid.Children.Add(CreateHeader(rowHeaders.ElementAtOrDefault(r), r + headerRow, 0));
        }

        _cells = new MatrixCell[rows, cols];
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                var cell = new MatrixCell(this, r, c);
                _cells[r, c] = cell;

                var numericBox = new NumericBox
                {
                    DataContext = cell,
                    Margin = new Thickness(c == 0 ? 0 : 2, r == 0 ? 0 : 2, 0, 0),
                };
                numericBox.SetBinding(NumericBox.ValueProperty, new System.Windows.Data.Binding(nameof(MatrixCell.Value))
                {
                    Mode = System.Windows.Data.BindingMode.TwoWay,
                });
                numericBox.SetBinding(NumericBox.MinimumProperty, BindSelf(nameof(Minimum)));
                numericBox.SetBinding(NumericBox.MaximumProperty, BindSelf(nameof(Maximum)));
                numericBox.SetBinding(NumericBox.IncrementProperty, BindSelf(nameof(Increment)));
                numericBox.SetBinding(NumericBox.UnitProviderProperty, BindSelf(nameof(UnitProvider)));
                numericBox.SetBinding(NumericBox.FormatProperty, BindSelf(nameof(Format)));

                Grid.SetRow(numericBox, r + headerRow);
                Grid.SetColumn(numericBox, c + headerCol);
                _grid.Children.Add(numericBox);
            }
        }

        SyncCellsFromValues();
        return;

        System.Windows.Data.Binding BindSelf(string path) => new(path) { Source = this };
    }

    private static TextBlock CreateHeader(object? content, int row, int col)
    {
        var text = new TextBlock
        {
            Text = content?.ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 4, 2),
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Wcu.Brush.Text.Secondary");
        Grid.SetRow(text, row);
        Grid.SetColumn(text, col);
        return text;
    }

    /// <summary>Values 配列 → セル表示(範囲外・null は空欄)。</summary>
    private void SyncCellsFromValues()
    {
        if (_cells is null)
        {
            return;
        }

        _updating = true;
        try
        {
            var values = Values;
            for (var r = 0; r < _cells.GetLength(0); r++)
            {
                for (var c = 0; c < _cells.GetLength(1); c++)
                {
                    _cells[r, c].SetSilently(
                        values is not null && r < values.GetLength(0) && c < values.GetLength(1)
                            ? values[r, c]
                            : null);
                }
            }
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>セル確定 → 新しい配列インスタンスを作って Values を差し替える。</summary>
    internal void OnCellEdited(int row, int col, double? newValue)
    {
        if (_updating || _cells is null)
        {
            return;
        }

        // null 確定(空欄)は無効入力として元の値に戻す
        if (newValue is not double value)
        {
            SyncCellsFromValues();
            return;
        }

        var rows = _cells.GetLength(0);
        var cols = _cells.GetLength(1);
        var source = Values;
        var next = new double[rows, cols];
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                next[r, c] = source is not null && r < source.GetLength(0) && c < source.GetLength(1)
                    ? source[r, c]
                    : 0.0;
            }
        }

        next[row, col] = value;
        if (IsSymmetric && row != col && col < rows && row < cols)
        {
            next[col, row] = value;
        }

        _updating = true;
        try
        {
            SetCurrentValue(ValuesProperty, next);

            // ミラー先セルと、配列化で 0 埋めされたセルの表示を更新
            if (IsSymmetric && row != col && col < rows && row < cols)
            {
                _cells[col, row].SetSilently(value);
            }

            if (source is null)
            {
                for (var r = 0; r < rows; r++)
                {
                    for (var c = 0; c < cols; c++)
                    {
                        _cells[r, c].SetSilently(next[r, c]);
                    }
                }
            }
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>セル1個分の内部 ViewModel(NumericBox が TwoWay バインドする)。</summary>
    internal sealed class MatrixCell(MatrixBox owner, int row, int col) : INotifyPropertyChanged
    {
        private double? _value;

        public event PropertyChangedEventHandler? PropertyChanged;

        public double? Value
        {
            get => _value;
            set
            {
                if (!Nullable.Equals(_value, value))
                {
                    _value = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
                    owner.OnCellEdited(row, col, value);
                }
            }
        }

        /// <summary>owner への通知なしで表示値を更新する(モデル → 表示の同期用)。</summary>
        public void SetSilently(double? value)
        {
            if (!Nullable.Equals(_value, value))
            {
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }
    }
}
