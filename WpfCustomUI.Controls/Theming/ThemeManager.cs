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

    /// <summary>SetAccent による上書き中のアクセント基準色。null なら上書きなし。</summary>
    private static Color? _accentOverride;

    private static readonly string[] AccentKeys =
    [
        "Wcu.Brush.Accent.Default",
        "Wcu.Brush.Accent.Hover",
        "Wcu.Brush.Accent.Pressed",
        "Wcu.Brush.Accent.Muted",
        "Wcu.Brush.Border.Focus",
        "Wcu.Color.Accent.Default",
        "Wcu.Color.Accent.Hover",
        "Wcu.Color.Accent.Pressed",
        "Wcu.Color.Accent.Muted",
    ];

    /// <summary>
    /// テーマ・アクセントが実行時に変更されたときに発火する。
    /// DynamicResource を使えない消費者(WpfCustomUI.Charts のようなラスタ描画系)が
    /// 再配色・再描画するためのフック。UI スレッドで発火する。
    /// </summary>
    public static event EventHandler? ThemeChanged;

    /// <summary>
    /// Windows の「アプリモード」設定(ライト/ダーク)を読み取る(spec 6.15.3)。
    /// 起動時に <see cref="SetTheme"/> へ渡すかどうか、実行中の設定変更に
    /// 追従するかどうかはアプリの責務(自動追従は提供しない)。
    /// 読み取れない場合は OS 既定の Light を返す。
    /// </summary>
    public static WcuThemeVariant GetSystemTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0
                ? WcuThemeVariant.Dark
                : WcuThemeVariant.Light;
        }
        catch (Exception e) when (e is System.Security.SecurityException or System.IO.IOException)
        {
            return WcuThemeVariant.Light;
        }
    }

    /// <summary>登録済みの全 WcuTheme のバリアントを切り替える。</summary>
    public static void SetTheme(WcuThemeVariant variant)
    {
        foreach (var theme in AliveThemes())
        {
            theme.Apply(variant);

            // アクセント上書き中なら、Muted の派生方向(暗く/淡く)が
            // バリアントに依存するため新バリアントで再計算する。
            if (_accentOverride is { } accent)
            {
                ApplyAccentTo(theme, AccentPalette.FromBase(accent, variant));
            }
        }

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// アクセントカラーを差し替える。Hover / Pressed / Muted の派生色は
    /// 明度調整で自動計算される(spec 4.3)。
    /// </summary>
    public static void SetAccent(Color accent)
    {
        _accentOverride = accent;
        foreach (var theme in AliveThemes())
        {
            ApplyAccentTo(theme, AccentPalette.FromBase(accent, theme.Theme));
        }

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>アクセント上書きを解除し、テーマ既定のアクセントに戻す。</summary>
    public static void ResetAccent()
    {
        _accentOverride = null;
        foreach (var theme in AliveThemes())
        {
            foreach (var key in AccentKeys)
            {
                theme.Remove(key);
            }
        }

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void ApplyAccentTo(WcuTheme theme, AccentPalette palette)
    {
        // 外側の辞書エントリはマージ辞書のキーを覆い隠すため、
        // テーマ辞書を差し替えてもアクセント上書きは維持される。
        theme["Wcu.Brush.Accent.Default"] = CreateFrozenBrush(palette.Default);
        theme["Wcu.Brush.Accent.Hover"] = CreateFrozenBrush(palette.Hover);
        theme["Wcu.Brush.Accent.Pressed"] = CreateFrozenBrush(palette.Pressed);
        theme["Wcu.Brush.Accent.Muted"] = CreateFrozenBrush(palette.Muted);
        theme["Wcu.Brush.Border.Focus"] = CreateFrozenBrush(palette.Default);

        // 色プリミティブも更新する。ブラシキーを参照できない消費者
        // (WpfCustomUI.Docking のドックテーマ等)がアクセント変更に追従するため。
        theme["Wcu.Color.Accent.Default"] = palette.Default;
        theme["Wcu.Color.Accent.Hover"] = palette.Hover;
        theme["Wcu.Color.Accent.Pressed"] = palette.Pressed;
        theme["Wcu.Color.Accent.Muted"] = palette.Muted;
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
