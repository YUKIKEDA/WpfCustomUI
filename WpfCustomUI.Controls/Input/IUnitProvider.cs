namespace WpfCustomUI.Controls;

/// <summary>
/// NumericBox の「表示単位と内部値(基準単位)の変換」の差し込み口(spec 6.1)。
/// ライブラリ自体は単位系を知らず、換算はアプリ(の実装)に委譲する。
/// 想定ユースケース: 内部は SI(m)、表示は mm。ユーザーが "500 mm" と入力すると内部値 0.5 になる。
/// </summary>
public interface IUnitProvider
{
    /// <summary>表示単位の記号(例: "mm")。NumericBox の単位ラベルに表示される。</summary>
    string DisplayUnit { get; }

    /// <summary>内部値(基準単位)を表示単位の値に変換する。</summary>
    double ToDisplay(double baseValue);

    /// <summary>表示単位の値を内部値(基準単位)に変換する。</summary>
    double FromDisplay(double displayValue);

    /// <summary>
    /// 単位記号付きで入力された値("500 mm" の 500 と "mm")を内部値に変換する。
    /// 未対応の単位記号であれば false を返す(NumericBox は検証エラーとして扱う)。
    /// </summary>
    bool TryConvertFrom(double value, string unitSymbol, out double baseValue);
}
