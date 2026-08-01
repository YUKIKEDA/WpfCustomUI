using System.IO;
using AvalonDock;
using AvalonDock.Layout.Serialization;

namespace WpfCustomUI.Docking;

/// <summary>
/// ドックレイアウトの保存・復元ヘルパー(spec 6.13.5)。
/// AvalonDock の <see cref="XmlLayoutSerializer"/> の薄いラッパーで、
/// 復元時のコンテンツ再結線を ContentId ベースのリゾルバに委譲する。
/// <list type="bullet">
/// <item>リゾルバが null を返した ContentId はレイアウトから破棄される
/// (廃止されたツールウィンドウが残骸として復元されるのを防ぐ)。</item>
/// <item>保存後に追加された新しいツールウィンドウはレイアウト XML に
/// 含まれないため、既定のレイアウト定義側で表示しておくこと。</item>
/// </list>
/// </summary>
public static class DockLayout
{
    /// <summary>現在のレイアウトをファイルへ保存する。親ディレクトリは自動作成する。</summary>
    public static void Save(DockingManager manager, string path)
    {
        ArgumentNullException.ThrowIfNull(manager);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var writer = new StreamWriter(path);
        new XmlLayoutSerializer(manager).Serialize(writer);
    }

    /// <summary>現在のレイアウトを XML 文字列として取得する(アプリ設定への埋め込み用)。</summary>
    public static string SaveToString(DockingManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);

        using var writer = new StringWriter();
        new XmlLayoutSerializer(manager).Serialize(writer);
        return writer.ToString();
    }

    /// <summary>
    /// ファイルからレイアウトを復元する。
    /// </summary>
    /// <param name="manager">対象の <see cref="DockingManager"/>。</param>
    /// <param name="path">レイアウト XML のパス。存在しない場合は何もせず false を返す。</param>
    /// <param name="resolveContent">
    /// ContentId からペイン内容(コントロール等)を引くリゾルバ。null を返すとその項目は破棄される。
    /// </param>
    /// <returns>復元を実行したら true。ファイルが無ければ false。</returns>
    public static bool Load(DockingManager manager, string path, Func<string, object?> resolveContent)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(resolveContent);

        if (!File.Exists(path))
        {
            return false;
        }

        using var reader = new StreamReader(path);
        Deserialize(manager, reader, resolveContent);
        return true;
    }

    /// <summary><see cref="SaveToString"/> で得た XML 文字列からレイアウトを復元する。</summary>
    public static void LoadFromString(DockingManager manager, string xml, Func<string, object?> resolveContent)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(resolveContent);

        using var reader = new StringReader(xml);
        Deserialize(manager, reader, resolveContent);
    }

    private static void Deserialize(DockingManager manager, TextReader reader, Func<string, object?> resolveContent)
    {
        var serializer = new XmlLayoutSerializer(manager);
        serializer.LayoutSerializationCallback += (_, e) =>
        {
            // ContentId のない項目・リゾルバが知らない項目は復元しない
            var content = string.IsNullOrEmpty(e.Model.ContentId)
                ? null
                : resolveContent(e.Model.ContentId);

            if (content is null)
            {
                e.Cancel = true;
            }
            else
            {
                e.Content = content;
            }
        };
        serializer.Deserialize(reader);
    }
}
