using System.Windows.Media;

namespace WpfCustomUI.Controls.Theming;

/// <summary>
/// 実行時のテーマ切替とアクセントカラー変更を提供する静的 API(spec 4.3)。
/// アプリにマージされた <see cref="WcuTheme"/> インスタンスを弱参照で追跡し、
/// 一括で更新する。UI スレッドから呼び出すこと。
/// </summary>
public static class ThemeManager
{
    private static readonly List<WeakReference<WcuTheme>> Themes = [];

    private static readonly string[] AccentKeys =
    [
        "Wcu.Brush.Accent.Default",
        "Wcu.Brush.Accent.Hover",
        "Wcu.Brush.Accent.Pressed",
        "Wcu.Brush.Accent.Muted",
        "Wcu.Brush.Border.Focus",
    ];

    /// <summary>登録済みの全 WcuTheme のバリアントを切り替える。</summary>
    public static void SetTheme(WcuThemeVariant variant)
    {
        foreach (var theme in AliveThemes())
        {
            theme.Apply(variant);
        }
    }

    /// <summary>
    /// アクセントカラーを差し替える。Hover / Pressed / Muted の派生色は
    /// 明度調整で自動計算される(spec 4.3)。
    /// </summary>
    public static void SetAccent(Color accent)
    {
        var palette = AccentPalette.FromBase(accent);
        foreach (var theme in AliveThemes())
        {
            // 外側の辞書エントリはマージ辞書のキーを覆い隠すため、
            // テーマ辞書を差し替えてもアクセント上書きは維持される。
            theme["Wcu.Brush.Accent.Default"] = CreateFrozenBrush(palette.Default);
            theme["Wcu.Brush.Accent.Hover"] = CreateFrozenBrush(palette.Hover);
            theme["Wcu.Brush.Accent.Pressed"] = CreateFrozenBrush(palette.Pressed);
            theme["Wcu.Brush.Accent.Muted"] = CreateFrozenBrush(palette.Muted);
            theme["Wcu.Brush.Border.Focus"] = CreateFrozenBrush(palette.Default);
        }
    }

    /// <summary>アクセント上書きを解除し、テーマ既定のアクセントに戻す。</summary>
    public static void ResetAccent()
    {
        foreach (var theme in AliveThemes())
        {
            foreach (var key in AccentKeys)
            {
                theme.Remove(key);
            }
        }
    }

    internal static void Register(WcuTheme theme)
    {
        Themes.RemoveAll(r => !r.TryGetTarget(out _));
        Themes.Add(new WeakReference<WcuTheme>(theme));
    }

    private static IEnumerable<WcuTheme> AliveThemes()
    {
        foreach (var reference in Themes)
        {
            if (reference.TryGetTarget(out var theme))
            {
                yield return theme;
            }
        }
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
