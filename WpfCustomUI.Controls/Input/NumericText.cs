using System.Globalization;

namespace WpfCustomUI.Controls;

/// <summary>
/// 数値入力文字列のパース(NumericBox のロジック部分)。
/// 指数表記("1e-3")と末尾の単位記号("500 mm"、"500mm")をサポートする。
/// UI に依存しないため単体テストの対象(spec 7)。
/// </summary>
public static class NumericText
{
    /// <summary>単位記号として許容する、文字(Letter)以外の文字。</summary>
    private const string ExtraUnitChars = "%°²³/";

    /// <summary>
    /// 文字列を「数値+省略可能な末尾の単位記号」としてパースする。
    /// </summary>
    /// <param name="text">入力文字列。</param>
    /// <param name="culture">数値書式のカルチャ。</param>
    /// <param name="number">パースされた数値。</param>
    /// <param name="unitSymbol">末尾の単位記号。なければ null。</param>
    /// <returns>数値部分がパースできた場合 true。</returns>
    public static bool TryParse(string text, IFormatProvider culture, out double number, out string? unitSymbol)
    {
        number = 0;
        unitSymbol = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();

        // 末尾から単位記号(文字・%・° 等の連なり)を切り出す。
        // "1e-3" の 'e' は数字が後続しないため単位とは誤認しない(末尾からの走査のため)。
        var unitStart = trimmed.Length;
        while (unitStart > 0 && IsUnitChar(trimmed[unitStart - 1]))
        {
            unitStart--;
        }

        var numericPart = trimmed[..unitStart].Trim();
        var unitPart = trimmed[unitStart..];

        if (numericPart.Length == 0)
        {
            return false;
        }

        if (!double.TryParse(numericPart, NumberStyles.Float, culture, out number))
        {
            return false;
        }

        unitSymbol = unitPart.Length > 0 ? unitPart : null;
        return true;
    }

    private static bool IsUnitChar(char c) => char.IsLetter(c) || ExtraUnitChars.Contains(c);
}
