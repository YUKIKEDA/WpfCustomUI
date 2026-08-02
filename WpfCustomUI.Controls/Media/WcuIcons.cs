using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

namespace WpfCustomUI.Controls;

/// <summary>
/// 内蔵ベクタアイコン集(spec 4.5 / 6.27.2)。
/// 16x16 グリッドに描いたストローク方式のラインアイコンで、
/// <see cref="WcuIcon"/> コントロール(Viewbox 拡大でストロークごとスケール)で表示する。
/// XAML からは <c>{x:Static wcu:WcuIcons.Save}</c>、または WcuTheme がマージする
/// <c>Wcu.Icon.*</c> リソースキーで参照できる。
/// </summary>
public static class WcuIcons
{
    // 名前 → Path データ(16x16 キャンバス、ストローク描画前提)
    private static readonly SortedDictionary<string, string> PathData = new()
    {
        // ---- ファイル・プロジェクト ----
        ["File"] = "M4,1.5 H9.5 L12.5,4.5 V14.5 H4 Z M9.5,1.5 V4.5 H12.5",
        ["NewFile"] = "M4,1.5 H9.5 L12.5,4.5 V14.5 H4 Z M9.5,1.5 V4.5 H12.5 M8.25,7.2 V11.8 M6,9.5 H10.5",
        ["Folder"] = "M1.5,4.5 H6 L7.5,6 H14.5 V13 H1.5 Z",
        ["FolderOpen"] = "M1.5,13 V4.5 H6 L7.5,6 H13 V8 M1.5,13 L3.5,8 H15 L13,13 Z",
        ["Save"] = "M2.5,2.5 H11 L13.5,5 V13.5 H2.5 Z M5,2.5 V6 H10.5 V2.5 M4.5,13.5 V9 H11.5 V13.5",
        ["SaveAs"] = "M2,2 H10 L12.5,4.5 V8.5 M4.5,2 V5 H9.5 V2 M4,12.5 H2 V8.5 M13,10.5 V14.5 M11,12.5 H15",
        ["History"] = "M14,8 A6,6 0 1 1 2,8 A6,6 0 1 1 14,8 M8,4.5 V8 L10.8,9.6",
        ["Settings"] = "M11,8 A3,3 0 1 1 5,8 A3,3 0 1 1 11,8 M12.6,8 H14.4 M11.25,4.75 L12.53,3.47 M8,3.4 V1.6 M4.75,4.75 L3.47,3.47 M3.4,8 H1.6 M4.75,11.25 L3.47,12.53 M8,12.6 V14.4 M11.25,11.25 L12.53,12.53",
        ["Download"] = "M8,2 V10.2 M4.6,6.8 L8,10.2 L11.4,6.8 M2.5,13.5 H13.5",
        ["Upload"] = "M8,10.2 V2 M4.6,5.4 L8,2 L11.4,5.4 M2.5,13.5 H13.5",
        ["Close"] = "M3.5,3.5 L12.5,12.5 M12.5,3.5 L3.5,12.5",

        // ---- 編集 ----
        ["Undo"] = "M3.5,6 H10.5 A4,4 0 0 1 10.5,14 H6 M6.5,3 L3.5,6 L6.5,9",
        ["Redo"] = "M12.5,6 H5.5 A4,4 0 0 0 5.5,14 H10 M9.5,3 L12.5,6 L9.5,9",
        ["Delete"] = "M2.5,4 H13.5 M6,4 V2.5 H10 V4 M3.8,4 L4.7,14 H11.3 L12.2,4 M6.6,6.5 V11.5 M9.4,6.5 V11.5",
        ["Add"] = "M8,3 V13 M3,8 H13",
        ["Remove"] = "M3,8 H13",
        ["Copy"] = "M2.5,2.5 H10.5 V5.5 M2.5,2.5 V10.5 H5.5 M5.5,5.5 H13.5 V13.5 H5.5 Z",
        ["Edit"] = "M3,13 L3.6,10.4 L10.8,3.2 A1.55,1.55 0 0 1 13,5.4 L5.8,12.6 L3,13 Z M9.7,4.3 L11.9,6.5",
        ["Search"] = "M11,7 A4,4 0 1 1 3,7 A4,4 0 1 1 11,7 M9.9,9.9 L14,14",
        ["Filter"] = "M2.5,3 H13.5 L9.5,8.5 V13.5 L6.5,11.5 V8.5 Z",
        ["Refresh"] = "M13.4,8 A5.4,5.4 0 1 1 10.6,3.27 M13.4,2.6 V6.1 H9.9",

        // ---- 表示 ----
        ["ZoomIn"] = "M11,7 A4,4 0 1 1 3,7 A4,4 0 1 1 11,7 M9.9,9.9 L14,14 M7,5.2 V8.8 M5.2,7 H8.8",
        ["ZoomOut"] = "M11,7 A4,4 0 1 1 3,7 A4,4 0 1 1 11,7 M9.9,9.9 L14,14 M5.2,7 H8.8",
        ["ZoomToFit"] = "M2,5.5 V2 H5.5 M10.5,2 H14 V5.5 M14,10.5 V14 H10.5 M5.5,14 H2 V10.5 M6,6 H10 V10 H6 Z",
        ["Eye"] = "M1.5,8 C4,4.7 12,4.7 14.5,8 C12,11.3 4,11.3 1.5,8 Z M9.8,8 A1.8,1.8 0 1 1 6.2,8 A1.8,1.8 0 1 1 9.8,8",
        ["EyeOff"] = "M1.5,8 C4,4.7 12,4.7 14.5,8 C12,11.3 4,11.3 1.5,8 Z M3,13.5 L13,2.5",
        ["Layout"] = "M1.5,2.5 H14.5 V13.5 H1.5 Z M5.75,2.5 V13.5 M5.75,8.5 H14.5",
        ["Sun"] = "M10.6,8 A2.6,2.6 0 1 1 5.4,8 A2.6,2.6 0 1 1 10.6,8 M8,3.4 V1.5 M8,12.6 V14.5 M12.6,8 H14.5 M3.4,8 H1.5 M10.97,5.03 L12.38,3.62 M5.03,5.03 L3.62,3.62 M10.97,10.97 L12.38,12.38 M5.03,10.97 L3.62,12.38",
        ["Moon"] = "M13.2,9.6 A6,6 0 1 1 6.4,2.8 A4.9,4.9 0 0 0 13.2,9.6 Z",
        ["Grid"] = "M2.5,2.5 H13.5 V13.5 H2.5 Z M2.5,6.17 H13.5 M2.5,9.83 H13.5 M6.17,2.5 V13.5 M9.83,2.5 V13.5",
        ["List"] = "M5.5,4 H13.5 M5.5,8 H13.5 M5.5,12 H13.5 M2.4,4 H2.6 M2.4,8 H2.6 M2.4,12 H2.6",
        ["Home"] = "M2,8 L8,2.5 L14,8 M3.5,7 V13.5 H6.5 V9.5 H9.5 V13.5 H12.5 V7",

        // ---- 再生 ----
        ["Play"] = "M5,3.5 L12.5,8 L5,12.5 Z",
        ["Pause"] = "M5.5,3.5 V12.5 M10.5,3.5 V12.5",
        ["Stop"] = "M4,4 H12 V12 H4 Z",

        // ---- CAE / 3D ----
        ["Mesh"] = "M8,2 L14.5,13.5 H1.5 Z M4.75,7.75 H11.25 L8,13.5 L4.75,7.75 Z",
        ["Cube"] = "M3,6 L6,3 H13 V10 L10,13 H3 Z M3,6 H10 M10,6 V13 M10,6 L13,3",
        ["Section"] = "M1.5,9.5 L8,12.5 L14.5,9.5 L8,6.5 Z M8,6.5 V2.8 M5.8,4.6 L8,2.4 L10.2,4.6",
        ["Probe"] = "M12.5,8 A4.5,4.5 0 1 1 3.5,8 A4.5,4.5 0 1 1 12.5,8 M8,1.5 V4 M8,12 V14.5 M1.5,8 H4 M12,8 H14.5 M7.9,8 H8.1",
        ["Annotation"] = "M2.5,13.5 L6,10 M6,3 H14 V10 H6 Z M8,5.8 H12 M8,7.5 H10.5",
        ["Vector"] = "M2.5,13.5 L13,3 M13,3 H8 M13,3 V8",
        ["Contour"] = "M5.5,1.5 H10.5 V14.5 H5.5 Z M5.5,4.75 H10.5 M5.5,8 H10.5 M5.5,11.25 H10.5",
        ["ChartLine"] = "M2.5,2 V13.5 H14.5 M4.5,11 L7.5,7 L10,9 L13.5,4",
        ["ChartBar"] = "M2.5,2 V13.5 H14.5 M5.5,13.5 V8.5 M8.5,13.5 V5.5 M11.5,13.5 V10",
        ["Orbit"] = "M10,8 A2,2 0 1 1 6,8 A2,2 0 1 1 10,8 M13.6,8 A5.6,5.6 0 1 1 8,2.4 M13.6,4.9 V8 H10.5",
        ["Pan"] = "M8,1.5 V14.5 M1.5,8 H14.5 M6.3,3.2 L8,1.5 L9.7,3.2 M6.3,12.8 L8,14.5 L9.7,12.8 M3.2,6.3 L1.5,8 L3.2,9.7 M12.8,6.3 L14.5,8 L12.8,9.7",
        ["Camera"] = "M1.5,5.2 H5 L6.5,3.5 H9.5 L11,5.2 H14.5 V13 H1.5 Z M10.4,8.8 A2.4,2.4 0 1 1 5.6,8.8 A2.4,2.4 0 1 1 10.4,8.8",
        ["Lock"] = "M3.5,7 H12.5 V14 H3.5 Z M5.5,7 V4.8 A2.5,2.5 0 0 1 10.5,4.8 V7 M8,9.8 V11.2",
        ["Support"] = "M9.3,3 A1.3,1.3 0 1 1 6.7,3 A1.3,1.3 0 1 1 9.3,3 M8,4.3 L11.5,11 H4.5 Z M2.5,13.5 H13.5",
        ["Layers"] = "M8,2 L14.5,5.5 L8,9 L1.5,5.5 Z M14.5,9 L8,12.5 L1.5,9",

        // ---- ステータス ----
        ["Info"] = "M14,8 A6,6 0 1 1 2,8 A6,6 0 1 1 14,8 M8,7.2 V11.2 M8,4.8 V5",
        ["Warning"] = "M8,2.2 L14.8,13.5 H1.2 Z M8,6.4 V9.8 M8,11.4 V11.6",
        ["Error"] = "M14,8 A6,6 0 1 1 2,8 A6,6 0 1 1 14,8 M5.9,5.9 L10.1,10.1 M10.1,5.9 L5.9,10.1",
        ["Success"] = "M14,8 A6,6 0 1 1 2,8 A6,6 0 1 1 14,8 M5.2,8.4 L7.2,10.4 L10.8,5.9",
        ["Check"] = "M2.5,8.5 L6.3,12.3 L13.5,4",
        ["Help"] = "M14,8 A6,6 0 1 1 2,8 A6,6 0 1 1 14,8 M6.2,6.4 A1.9,1.9 0 1 1 8.9,8.3 C8.2,8.8 8,9.2 8,10 M8,12.2 V12.4",

        // ---- リモート / ジョブ ----
        ["Server"] = "M2.5,3 H13.5 V6.8 H2.5 Z M2.5,9.2 H13.5 V13 H2.5 Z M4.7,4.9 H4.9 M4.7,11.1 H4.9 M9,4.9 H11.5 M9,11.1 H11.5",
        ["Cloud"] = "M4.6,12.5 H11.8 A2.7,2.7 0 0 0 12.1,7.1 A4.3,4.3 0 0 0 3.9,6.5 A3.2,3.2 0 0 0 4.6,12.5 Z",
        ["Hourglass"] = "M4.5,2.5 H11.5 M4.5,13.5 H11.5 M5.2,2.5 V4 C5.2,6.5 7,7 8,8 C7,9 5.2,9.5 5.2,12 V13.5 M10.8,2.5 V4 C10.8,6.5 9,7 8,8 C9,9 10.8,9.5 10.8,12 V13.5",

        // ---- 汎用矢印・ナビ ----
        ["ChevronDown"] = "M4,6 L8,10 L12,6",
        ["ChevronUp"] = "M4,10 L8,6 L12,10",
        ["ChevronLeft"] = "M10,4 L6,8 L10,12",
        ["ChevronRight"] = "M6,4 L10,8 L6,12",
        ["More"] = "M3.4,8 H3.6 M7.9,8 H8.1 M12.4,8 H12.6",
        ["Menu"] = "M2.5,4.5 H13.5 M2.5,8 H13.5 M2.5,11.5 H13.5",
        ["ArrowRight"] = "M2.5,8 H13.5 M9.5,4 L13.5,8 L9.5,12",
        ["ArrowLeft"] = "M13.5,8 H2.5 M6.5,4 L2.5,8 L6.5,12",
    };

    private static readonly Dictionary<string, Geometry> Cache = [];
    private static readonly object CacheLock = new();
    private static ResourceDictionary? _resourceDictionary;

    /// <summary>収録アイコン名の一覧(ソート済み)。</summary>
    public static IReadOnlyCollection<string> Names => PathData.Keys;

    /// <summary>アイコンのキャンバスサイズ(描画座標系は 16x16)。</summary>
    public const double CanvasSize = 16.0;

    /// <summary>名前からアイコン Geometry を取得する。未知の名前は例外。</summary>
    public static Geometry Get(string name)
    {
        if (!TryGet(name, out var geometry))
        {
            throw new KeyNotFoundException($"Unknown icon name: {name}");
        }

        return geometry;
    }

    /// <summary>名前からアイコン Geometry の取得を試みる。Geometry は Frozen(スレッド安全)。</summary>
    public static bool TryGet(string name, out Geometry geometry)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(name, out var cached))
            {
                geometry = cached;
                return true;
            }

            if (!PathData.TryGetValue(name, out var data))
            {
                geometry = Geometry.Empty;
                return false;
            }

            var parsed = Geometry.Parse(data);
            parsed.Freeze();
            Cache[name] = parsed;
            geometry = parsed;
            return true;
        }
    }

    /// <summary>
    /// 全アイコンを <c>Wcu.Icon.&lt;Name&gt;</c> キーで持つ ResourceDictionary。
    /// WcuTheme がマージするため、アプリは StaticResource/DynamicResource でも参照できる。
    /// </summary>
    public static ResourceDictionary CreateResourceDictionary()
    {
        if (_resourceDictionary is { } existing)
        {
            return existing;
        }

        var dictionary = new ResourceDictionary();
        foreach (var name in PathData.Keys)
        {
            dictionary[$"Wcu.Icon.{name}"] = Get(name);
        }

        _resourceDictionary = dictionary;
        return dictionary;
    }

    // ---- x:Static 参照用の型付きアクセサ ----
    public static Geometry File => Get(nameof(File));
    public static Geometry NewFile => Get(nameof(NewFile));
    public static Geometry Folder => Get(nameof(Folder));
    public static Geometry FolderOpen => Get(nameof(FolderOpen));
    public static Geometry Save => Get(nameof(Save));
    public static Geometry SaveAs => Get(nameof(SaveAs));
    public static Geometry History => Get(nameof(History));
    public static Geometry Settings => Get(nameof(Settings));
    public static Geometry Download => Get(nameof(Download));
    public static Geometry Upload => Get(nameof(Upload));
    public static Geometry Close => Get(nameof(Close));
    public static Geometry Undo => Get(nameof(Undo));
    public static Geometry Redo => Get(nameof(Redo));
    public static Geometry Delete => Get(nameof(Delete));
    public static Geometry Add => Get(nameof(Add));
    public static Geometry Remove => Get(nameof(Remove));
    public static Geometry Copy => Get(nameof(Copy));
    public static Geometry Edit => Get(nameof(Edit));
    public static Geometry Search => Get(nameof(Search));
    public static Geometry Filter => Get(nameof(Filter));
    public static Geometry Refresh => Get(nameof(Refresh));
    public static Geometry ZoomIn => Get(nameof(ZoomIn));
    public static Geometry ZoomOut => Get(nameof(ZoomOut));
    public static Geometry ZoomToFit => Get(nameof(ZoomToFit));
    public static Geometry Eye => Get(nameof(Eye));
    public static Geometry EyeOff => Get(nameof(EyeOff));
    public static Geometry Layout => Get(nameof(Layout));
    public static Geometry Sun => Get(nameof(Sun));
    public static Geometry Moon => Get(nameof(Moon));
    public static Geometry Grid => Get(nameof(Grid));
    public static Geometry List => Get(nameof(List));
    public static Geometry Home => Get(nameof(Home));
    public static Geometry Play => Get(nameof(Play));
    public static Geometry Pause => Get(nameof(Pause));
    public static Geometry Stop => Get(nameof(Stop));
    public static Geometry Mesh => Get(nameof(Mesh));
    public static Geometry Cube => Get(nameof(Cube));
    public static Geometry Section => Get(nameof(Section));
    public static Geometry Probe => Get(nameof(Probe));
    public static Geometry Annotation => Get(nameof(Annotation));
    public static Geometry Vector => Get(nameof(Vector));
    public static Geometry Contour => Get(nameof(Contour));
    public static Geometry ChartLine => Get(nameof(ChartLine));
    public static Geometry ChartBar => Get(nameof(ChartBar));
    public static Geometry Orbit => Get(nameof(Orbit));
    public static Geometry Pan => Get(nameof(Pan));
    public static Geometry Camera => Get(nameof(Camera));
    public static Geometry Lock => Get(nameof(Lock));
    public static Geometry Support => Get(nameof(Support));
    public static Geometry Layers => Get(nameof(Layers));
    public static Geometry Info => Get(nameof(Info));
    public static Geometry Warning => Get(nameof(Warning));
    public static Geometry Error => Get(nameof(Error));
    public static Geometry Success => Get(nameof(Success));
    public static Geometry Check => Get(nameof(Check));
    public static Geometry Help => Get(nameof(Help));
    public static Geometry Server => Get(nameof(Server));
    public static Geometry Cloud => Get(nameof(Cloud));
    public static Geometry Hourglass => Get(nameof(Hourglass));
    public static Geometry ChevronDown => Get(nameof(ChevronDown));
    public static Geometry ChevronUp => Get(nameof(ChevronUp));
    public static Geometry ChevronLeft => Get(nameof(ChevronLeft));
    public static Geometry ChevronRight => Get(nameof(ChevronRight));
    public static Geometry More => Get(nameof(More));
    public static Geometry Menu => Get(nameof(Menu));
    public static Geometry ArrowRight => Get(nameof(ArrowRight));
    public static Geometry ArrowLeft => Get(nameof(ArrowLeft));
}
