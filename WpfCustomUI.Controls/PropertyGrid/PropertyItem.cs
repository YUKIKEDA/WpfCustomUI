using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WpfCustomUI.Controls;

/// <summary>
/// PropertyGrid の1行を表す明示的アイテムモデルの基底クラス(spec 6.2)。
/// アプリはこの派生クラスのコレクションを組み立てて <see cref="PropertyGrid.ItemsSource"/> に渡す。
/// エディタは派生型に対応する DataTemplate で自動選択されるため、
/// アプリ独自のエディタは「派生クラス+DataTemplate を1つ書く」だけで追加できる。
/// </summary>
public abstract class PropertyItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string? _category;
    private string? _description;
    private bool _isReadOnly;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>行に表示するプロパティ名。フィルタの対象。</summary>
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    /// <summary>カテゴリ名。同じカテゴリの行が折りたたみ可能なグループにまとまる。</summary>
    public string? Category
    {
        get => _category;
        set => SetField(ref _category, value);
    }

    /// <summary>説明文。行ホバーのツールチップに表示される(spec 6.2)。</summary>
    public string? Description
    {
        get => _description;
        set => SetField(ref _description, value);
    }

    public bool IsReadOnly
    {
        get => _isReadOnly;
        set
        {
            if (SetField(ref _isReadOnly, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditable)));
            }
        }
    }

    /// <summary>IsReadOnly の反転。エディタの IsEnabled バインド用。</summary>
    public bool IsEditable => !_isReadOnly;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
