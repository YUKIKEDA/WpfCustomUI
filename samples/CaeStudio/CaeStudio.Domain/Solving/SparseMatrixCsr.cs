namespace CaeStudio.Domain.Solving;

/// <summary>
/// CSR(Compressed Sparse Row)形式の対称正定値疎行列。
/// FEM 剛性行列の格納と、CG 内の行列ベクトル積(行並列)に使う。
/// </summary>
public sealed class SparseMatrixCsr
{
    private readonly int[] _rowPointers;
    private readonly int[] _columns;
    private readonly double[] _values;

    private SparseMatrixCsr(int[] rowPointers, int[] columns, double[] values)
    {
        _rowPointers = rowPointers;
        _columns = columns;
        _values = values;
    }

    /// <summary>行数(=列数)。</summary>
    public int Size => _rowPointers.Length - 1;

    /// <summary>非ゼロ要素数。</summary>
    public long NonZeroCount => _values.LongLength;

    /// <summary>行ごとの辞書(組み立て用の中間表現)から CSR に圧縮する。</summary>
    public static SparseMatrixCsr FromRows(Dictionary<int, double>[] rows)
    {
        var rowPointers = new int[rows.Length + 1];
        for (var i = 0; i < rows.Length; i++)
        {
            rowPointers[i + 1] = rowPointers[i] + rows[i].Count;
        }

        var columns = new int[rowPointers[^1]];
        var values = new double[rowPointers[^1]];
        Parallel.For(0, rows.Length, i =>
        {
            var write = rowPointers[i];
            foreach (var (column, value) in rows[i].OrderBy(kv => kv.Key))
            {
                columns[write] = column;
                values[write] = value;
                write++;
            }
        });

        return new SparseMatrixCsr(rowPointers, columns, values);
    }

    /// <summary>result = A · x(行並列)。</summary>
    public void Multiply(double[] x, double[] result)
    {
        var (rowPointers, columns, values) = (_rowPointers, _columns, _values);
        Parallel.For(0, Size, row =>
        {
            var sum = 0.0;
            var end = rowPointers[row + 1];
            for (var k = rowPointers[row]; k < end; k++)
            {
                sum += values[k] * x[columns[k]];
            }

            result[row] = sum;
        });
    }

    /// <summary>対角成分(Jacobi 前処理用)。</summary>
    public double[] GetDiagonal()
    {
        var diagonal = new double[Size];
        for (var row = 0; row < Size; row++)
        {
            var end = _rowPointers[row + 1];
            for (var k = _rowPointers[row]; k < end; k++)
            {
                if (_columns[k] == row)
                {
                    diagonal[row] = _values[k];
                    break;
                }
            }
        }

        return diagonal;
    }
}
