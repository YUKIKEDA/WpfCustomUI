namespace CaeStudio.Application;

/// <summary>
/// ユーザ設定(不変レコード)。テーマ / 最近使ったファイル / 既定保存先 /
/// 解析実行ショートカット / ドックレイアウト XML を保持する(spec 6.26.6)。
/// </summary>
public sealed record UserSettings
{
    /// <summary>"Dark" または "Light"。</summary>
    public string Theme { get; init; } = "Dark";

    /// <summary>最近使ったプロジェクトファイル(新しい順、最大 5 件)。</summary>
    public IReadOnlyList<string> RecentFiles { get; init; } = [];

    /// <summary>プロジェクトファイルの既定保存先(null は マイドキュメント)。</summary>
    public string? DefaultProjectDirectory { get; init; }

    /// <summary>解析実行のキーボードショートカット(KeyGesture 文字列、例 "F5")。</summary>
    public string RunGesture { get; init; } = "F5";

    /// <summary>ドックレイアウトのシリアライズ XML(null は既定レイアウト)。</summary>
    public string? DockLayoutXml { get; init; }

    /// <summary>ファイルを最近使ったリストの先頭へ移動した新しい設定を返す。</summary>
    public UserSettings WithRecentFile(string filePath) => this with
    {
        RecentFiles =
        [
            filePath,
            .. RecentFiles.Where(f => !string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase)).Take(4),
        ],
    };
}

/// <summary>ユーザ設定の永続化(Infrastructure が JSON で実装する)。</summary>
public interface ISettingsService
{
    /// <summary>現在の設定(起動時にロード済み)。</summary>
    UserSettings Current { get; }

    /// <summary>設定を更新して保存する。</summary>
    void Update(Func<UserSettings, UserSettings> mutate);
}
