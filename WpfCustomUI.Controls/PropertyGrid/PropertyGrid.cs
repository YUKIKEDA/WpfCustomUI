using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace WpfCustomUI.Controls;

/// <summary>
/// 明示的アイテムモデル方式のプロパティグリッド(spec 6.2)。
/// <list type="bullet">
/// <item><see cref="ItemsSource"/> に <see cref="PropertyItem"/> のコレクションを渡す。</item>
/// <item>カテゴリごとの折りたたみと、項目名の部分一致フィルタを内蔵。</item>
/// <item>説明(<see cref="PropertyItem.Description"/>)は行ホバーのツールチップで表示。</item>
/// </list>
/// </summary>
public class PropertyGrid : Control
{
    static PropertyGrid()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(PropertyGrid), new FrameworkPropertyMetadata(typeof(PropertyGrid)));
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(PropertyGrid),
        new PropertyMetadata(null, OnItemsSourceChanged));

    /// <summary>表示する PropertyItem のコレクション。ObservableCollection なら行の増減が即時反映される。</summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty FilterTextProperty = DependencyProperty.Register(
        nameof(FilterText), typeof(string), typeof(PropertyGrid),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnFilterTextChanged));

    /// <summary>項目名の部分一致フィルタ(大文字小文字を区別しない)。</summary>
    public string? FilterText
    {
        get => (string?)GetValue(FilterTextProperty);
        set => SetValue(FilterTextProperty, value);
    }

    public static readonly DependencyProperty ShowFilterBoxProperty = DependencyProperty.Register(
        nameof(ShowFilterBox), typeof(bool), typeof(PropertyGrid), new PropertyMetadata(true));

    /// <summary>フィルタボックスを表示するか。</summary>
    public bool ShowFilterBox
    {
        get => (bool)GetValue(ShowFilterBoxProperty);
        set => SetValue(ShowFilterBoxProperty, value);
    }

    // 内蔵文字列は差し替え可能にする方針(spec 5)
    public static readonly DependencyProperty FilterPlaceholderProperty = DependencyProperty.Register(
        nameof(FilterPlaceholder), typeof(string), typeof(PropertyGrid), new PropertyMetadata("Filter..."));

    /// <summary>フィルタボックスのプレースホルダー文字列(差し替え可能)。</summary>
    public string FilterPlaceholder
    {
        get => (string)GetValue(FilterPlaceholderProperty);
        set => SetValue(FilterPlaceholderProperty, value);
    }

    private static readonly DependencyPropertyKey ItemsViewPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ItemsView), typeof(ICollectionView), typeof(PropertyGrid), new PropertyMetadata(null));

    public static readonly DependencyProperty ItemsViewProperty = ItemsViewPropertyKey.DependencyProperty;

    /// <summary>グループ化・フィルタ適用済みのビュー(テンプレートが表示に使う。読み取り専用)。</summary>
    public ICollectionView? ItemsView => (ICollectionView?)GetValue(ItemsViewProperty);

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PropertyGrid)d).CreateView();

    private static void OnFilterTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PropertyGrid)d).ItemsView?.Refresh();

    private void CreateView()
    {
        var source = ItemsSource;
        if (source is null)
        {
            SetValue(ItemsViewPropertyKey, null);
            return;
        }

        // 共有される既定ビュー(CollectionViewSource.GetDefaultView)を使うと、
        // 同じコレクションを表示する他のコントロールにフィルタが波及するため専用ビューを作る。
        var list = source as IList ?? source.Cast<object>().ToList();
        var view = new ListCollectionView(list);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PropertyItem.Category)));
        view.Filter = FilterPredicate;

        SetValue(ItemsViewPropertyKey, view);
    }

    private bool FilterPredicate(object item)
    {
        var filter = FilterText;
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return item is PropertyItem property
            && property.Name.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
