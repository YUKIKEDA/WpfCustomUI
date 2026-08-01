using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;

namespace WpfCustomUI.Controls;

/// <summary>PathBox が開くダイアログの種類。</summary>
public enum PathBoxMode
{
    OpenFile,
    SaveFile,
    Folder,
}

/// <summary>
/// 参照ボタン付きのファイル/フォルダパス入力欄(spec 6.9.2)。
/// 参照ボタンは Microsoft.Win32 の標準ダイアログを内蔵で開く(ゼロ依存を維持)。
/// アプリ独自のダイアログに差し替えたい場合は <see cref="BrowseRequested"/> を
/// 購読して Handled = true にする。
/// </summary>
[TemplatePart(Name = PartBrowseButton, Type = typeof(ButtonBase))]
public class PathBox : Control
{
    private const string PartBrowseButton = "PART_BrowseButton";

    private ButtonBase? _browseButton;

    static PathBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(PathBox), new FrameworkPropertyMetadata(typeof(PathBox)));
    }

    public static readonly DependencyProperty SelectedPathProperty = DependencyProperty.Register(
        nameof(SelectedPath), typeof(string), typeof(PathBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>現在のパス。テキスト編集・ダイアログ選択の両方で更新される。</summary>
    public string SelectedPath
    {
        get => (string)GetValue(SelectedPathProperty);
        set => SetValue(SelectedPathProperty, value);
    }

    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(PathBoxMode), typeof(PathBox), new PropertyMetadata(PathBoxMode.OpenFile));

    /// <summary>参照ボタンが開くダイアログの種類。</summary>
    public PathBoxMode Mode
    {
        get => (PathBoxMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public static readonly DependencyProperty FilterProperty = DependencyProperty.Register(
        nameof(Filter), typeof(string), typeof(PathBox), new PropertyMetadata(null));

    /// <summary>ファイルダイアログのフィルタ(例: "Mesh files (*.msh)|*.msh|All files (*.*)|*.*")。</summary>
    public string? Filter
    {
        get => (string?)GetValue(FilterProperty);
        set => SetValue(FilterProperty, value);
    }

    public static readonly DependencyProperty DialogTitleProperty = DependencyProperty.Register(
        nameof(DialogTitle), typeof(string), typeof(PathBox), new PropertyMetadata(null));

    /// <summary>ダイアログのタイトル(null なら OS 既定)。</summary>
    public string? DialogTitle
    {
        get => (string?)GetValue(DialogTitleProperty);
        set => SetValue(DialogTitleProperty, value);
    }

    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(PathBox), new PropertyMetadata(false));

    /// <summary>true ならテキスト編集・参照ボタンの両方を無効化する。</summary>
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public static readonly RoutedEvent BrowseRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(BrowseRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(PathBox));

    /// <summary>
    /// 参照ボタン押下時に内蔵ダイアログより先に発火する。
    /// Handled = true にすると内蔵ダイアログは開かない(アプリ独自ダイアログへの差し替え口)。
    /// </summary>
    public event RoutedEventHandler BrowseRequested
    {
        add => AddHandler(BrowseRequestedEvent, value);
        remove => RemoveHandler(BrowseRequestedEvent, value);
    }

    public override void OnApplyTemplate()
    {
        if (_browseButton is not null)
        {
            _browseButton.Click -= OnBrowseClick;
        }

        base.OnApplyTemplate();

        _browseButton = GetTemplateChild(PartBrowseButton) as ButtonBase;

        if (_browseButton is not null)
        {
            _browseButton.Click += OnBrowseClick;
        }
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e) => Browse();

    /// <summary>参照ダイアログを開き、確定されたパスを <see cref="SelectedPath"/> に反映する。</summary>
    public void Browse()
    {
        var args = new RoutedEventArgs(BrowseRequestedEvent, this);
        RaiseEvent(args);
        if (args.Handled)
        {
            return;
        }

        var owner = Window.GetWindow(this);
        var current = SelectedPath;

        switch (Mode)
        {
            case PathBoxMode.OpenFile:
            {
                var dialog = new OpenFileDialog { Filter = Filter ?? string.Empty };
                InitializeFileDialog(dialog, current);
                if (dialog.ShowDialog(owner) == true)
                {
                    SetCurrentValue(SelectedPathProperty, dialog.FileName);
                }

                break;
            }

            case PathBoxMode.SaveFile:
            {
                var dialog = new SaveFileDialog { Filter = Filter ?? string.Empty };
                InitializeFileDialog(dialog, current);
                if (dialog.ShowDialog(owner) == true)
                {
                    SetCurrentValue(SelectedPathProperty, dialog.FileName);
                }

                break;
            }

            case PathBoxMode.Folder:
            {
                var dialog = new OpenFolderDialog();
                if (DialogTitle is { } title)
                {
                    dialog.Title = title;
                }

                if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
                {
                    dialog.InitialDirectory = current;
                }

                if (dialog.ShowDialog(owner) == true)
                {
                    SetCurrentValue(SelectedPathProperty, dialog.FolderName);
                }

                break;
            }
        }
    }

    /// <summary>現在のパスからファイル名・初期フォルダを引き継いでダイアログを初期化する。</summary>
    private void InitializeFileDialog(FileDialog dialog, string? current)
    {
        if (DialogTitle is { } title)
        {
            dialog.Title = title;
        }

        if (string.IsNullOrWhiteSpace(current))
        {
            return;
        }

        try
        {
            dialog.FileName = Path.GetFileName(current);
            if (Path.GetDirectoryName(current) is { Length: > 0 } directory && Directory.Exists(directory))
            {
                dialog.InitialDirectory = directory;
            }
        }
        catch (ArgumentException)
        {
            // 入力途中の不正なパスは無視して既定の場所で開く
        }
    }
}
