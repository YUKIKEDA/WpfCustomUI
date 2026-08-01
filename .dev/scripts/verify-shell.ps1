# 統合ミニ CAE シェルの UIA 検証(Phase 25、spec 6.25.6)
# - ツリー選択 → 3D パーツハイライト(ピクセル差分)
# - 3D パーツピック → ツリー選択(双方向同期)
# - PropertyGrid の可視性/プロパティ変更 → 描画反映
# - プローブ → LogConsole 記録
# - タブ切替(ビューポート 2 インスタンス) → PlaybackBar 再生反映
# - ドキュメントのフローティング+ライトテーマ切替の複合でクラッシュなし
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
"@
Add-Type -ReferencedAssemblies System.Drawing @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class PixelDiff {
    public static int Count(string pathA, string pathB, int tolerance) {
        using (var a = new Bitmap(pathA))
        using (var b = new Bitmap(pathB)) {
            int w = Math.Min(a.Width, b.Width), h = Math.Min(a.Height, b.Height);
            var rect = new Rectangle(0, 0, w, h);
            var da = a.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var db = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try {
                var bytesA = new byte[da.Stride * h];
                var bytesB = new byte[db.Stride * h];
                Marshal.Copy(da.Scan0, bytesA, 0, bytesA.Length);
                Marshal.Copy(db.Scan0, bytesB, 0, bytesB.Length);
                int count = 0;
                for (int y = 0; y < h; y++) {
                    int rowA = y * da.Stride, rowB = y * db.Stride;
                    for (int x = 0; x < w; x++) {
                        int ia = rowA + x * 4, ib = rowB + x * 4;
                        if (Math.Abs(bytesA[ia] - bytesB[ib]) > tolerance
                            || Math.Abs(bytesA[ia + 1] - bytesB[ib + 1]) > tolerance
                            || Math.Abs(bytesA[ia + 2] - bytesB[ib + 2]) > tolerance) {
                            count++;
                        }
                    }
                }
                return count;
            } finally {
                a.UnlockBits(da);
                b.UnlockBits(db);
            }
        }
    }
}
"@

function Capture-Region($x, $y, $w, $h, $path) {
    [Win32]::SetCursorPos(5, 5) | Out-Null
    Start-Sleep -Milliseconds 120
    $b = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $g.Dispose()
    $b.Save($path)
    $b.Dispose()
}

function Find-ByName($scope, $name) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    return $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

function Find-ById($scope, $automationId) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

function Find-ByClass($scope, $className) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ClassNameProperty, $className)
    return $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

# 同名要素の衝突回避(ツリー行 vs ドキュメントタブ): ControlType を指定して検索する
function Find-ByNameAndType($scope, $name, $controlType) {
    $c = New-Object System.Windows.Automation.AndCondition @(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $controlType)))
    return $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

function Select-Item($element) {
    $element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}

function Invoke-Button($element) {
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Toggle-Element($element) {
    $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
}

function Left-Click($x, $y) {
    [Win32]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 150
    [Win32]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [Win32]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
}

function Left-Drag($x0, $y0, $x1, $y1) {
    [Win32]::SetCursorPos($x0, $y0) | Out-Null
    Start-Sleep -Milliseconds 200
    [Win32]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 150
    for ($i = 1; $i -le 20; $i++) {
        $mx = [int]($x0 + ($x1 - $x0) * $i / 20)
        $my = [int]($y0 + ($y1 - $y0) * $i / 20)
        [Win32]::SetCursorPos($mx, $my) | Out-Null
        Start-Sleep -Milliseconds 40
    }
    Start-Sleep -Milliseconds 250
    [Win32]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 700
}

$script:failures = 0
function Assert-Equals($label, $actual, $expected) {
    if ($actual -eq $expected) {
        Write-Output ("PASS {0}" -f $label)
    } else {
        Write-Output ("FAIL {0}: '{1}' != '{2}'" -f $label, $actual, $expected)
        $script:failures++
    }
}

function Assert-Contains($label, $text, $substring) {
    if ($text -like "*$substring*") {
        Write-Output ("PASS {0}" -f $label)
    } else {
        Write-Output ("FAIL {0}: '{1}' not in '{2}'" -f $label, $substring, $text)
        $script:failures++
    }
}

function Assert-DiffAtLeast($label, $fileA, $fileB, $minPixels) {
    $diff = [PixelDiff]::Count($fileA, $fileB, 8)
    if ($diff -ge $minPixels) {
        Write-Output ("PASS {0}: diff={1} (>= {2})" -f $label, $diff, $minPixels)
    } else {
        Write-Output ("FAIL {0}: diff={1} (expected >= {2})" -f $label, $diff, $minPixels)
        $script:failures++
    }
}

function Wait-ForText($element, $substring, $timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $text = $element.Current.Name
        if ($text -like "*$substring*") {
            return $text
        }
        Start-Sleep -Milliseconds 400
    }
    return $element.Current.Name
}

# 日本語ラベル(エンコーディング事故を避けるため char 合成)
$LblBoss    = [string][char]0x5186 + [char]0x7B52 + [char]0x30DC + [char]0x30B9                              # 円筒ボス
$LblPlate   = [string][char]0x5186 + [char]0x5B54 + [char]0x4ED8 + [char]0x304D + [char]0x5E73 + [char]0x677F  # 円孔付き平板
$LblStatic  = [string][char]0x9759 + [char]0x89E3 + [char]0x6790                                             # 静解析
$LblTrans   = [string][char]0x904E + [char]0x6E21 + [char]0x5FDC + [char]0x7B54                              # 過渡応答
$LblBeam    = [string][char]0x7247 + [char]0x6301 + [char]0x3061 + [char]0x6881                              # 片持ち梁
$LblClear   = [string][char]0x9078 + [char]0x629E + [char]0x89E3 + [char]0x9664                              # 選択解除
$LblProbe   = [string][char]0x30D7 + [char]0x30ED + [char]0x30FC + [char]0x30D6                              # プローブ
$LblSel     = [string][char]0x9078 + [char]0x629E                                                            # 選択
$LblNone    = [string][char]0x306A + [char]0x3057                                                            # なし
$LblParts   = [string][char]0x30D1 + [char]0x30FC + [char]0x30C4                                             # パーツ
$LblFace    = [string][char]0x9762                                                                           # 面
$LblNode    = [string][char]0x7BC0 + [char]0x70B9                                                            # 節点
$LblHover   = [string][char]0x30DB + [char]0x30D0 + [char]0x30FC                                             # ホバー
$LblTri     = [string][char]0x4E09 + [char]0x89D2 + [char]0x5F62                                             # 三角形
$LblTheme   = [string][char]0x30E9 + [char]0x30A4 + [char]0x30C8 + [char]0x30C6 + [char]0x30FC + [char]0x30DE  # ライトテーマ
$Sigma      = [string][char]0x03C3                                                                           # σ
$LblStaticDoc = $LblStatic + ': ' + $LblPlate       # 静解析: 円孔付き平板
$LblTransDoc  = $LblTrans + ': ' + $LblBeam         # 過渡応答: 片持ち梁
$LblSelNone   = $LblSel + ': ' + $LblNone           # 選択: なし
$LblSelBoss   = $LblSel + ': ' + $LblParts + ' 1 / ' + $LblFace + ' 0 / ' + $LblNode + ' 0'

$rootDir = 'd:\home\Programs\CSharpProjects\WpfCustomUI'
$exe = Join-Path $rootDir 'WpfCustomUI.Gallery\bin\Debug\net10.0-windows\WpfCustomUI.Gallery.exe'
$outDir = Join-Path $rootDir '.dev\captures'

$p = Start-Process -FilePath $exe -ArgumentList '--dockshell' -PassThru
Start-Sleep -Seconds 7

# シェルウィンドウを名前で特定する。WPF のオーナー付きウィンドウは UIA では
# オーナー(ギャラリー本体)の子として現れるため、デスクトップ直下とオーナー配下の両方を探す
$desktop = [System.Windows.Automation.AutomationElement]::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$windowCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Window)
$root = $null
$deadline = (Get-Date).AddSeconds(15)
while ((Get-Date) -lt $deadline -and $null -eq $root) {
    foreach ($w in $desktop.FindAll([System.Windows.Automation.TreeScope]::Children, $pidCond)) {
        if ($w.Current.Name -like 'CAE Shell*') { $root = $w; break }
        foreach ($c in $w.FindAll([System.Windows.Automation.TreeScope]::Children, $windowCond)) {
            if ($c.Current.Name -like 'CAE Shell*') { $root = $c; break }
        }
        if ($null -ne $root) { break }
    }
    if ($null -eq $root) { Start-Sleep -Milliseconds 500 }
}
if ($null -eq $root) { Write-Output 'FAIL: shell window not found'; Stop-Process -Id $p.Id; exit 1 }

$h = [IntPtr]$root.Current.NativeWindowHandle
[Win32]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, 1280, 960, 0x0040) | Out-Null
[Win32]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 800
Write-Output ("shell window: " + $root.Current.Name)

$selStatus = Find-ById $root 'ShellSelectionStatus'
$hoverStatus = Find-ById $root 'ShellHoverStatus'
$statsStatus = Find-ById $root 'ShellStatsStatus'

# ---- 1. 初期状態: 非同期構築完了(静解析 4,800 三角形 = 平板 4,608 + ボス 192) ----
$stats = Wait-ForText $statsStatus '4,800' 30
Write-Output ("stats: " + $stats)
Assert-Contains 'initial stats show static doc' $stats $LblStatic
Assert-Contains 'initial stats triangle count' $stats ($LblTri + ' 4,800')

function Get-ActiveViewport($root) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'WcuViewport')
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $c)
    foreach ($v in $all) {
        $r = $v.Current.BoundingRectangle
        if (-not $v.Current.IsOffscreen -and $r.Width -gt 100 -and $r.Height -gt 100) { return $v }
    }
    return $null
}

$vp = Get-ActiveViewport $root
if ($null -eq $vp) { Write-Output 'FAIL: WcuViewport not found'; Stop-Process -Id $p.Id; exit 1 }
$r = $vp.Current.BoundingRectangle
$cx = [int]($r.X + $r.Width / 2); $cy = [int]($r.Y + $r.Height / 2)

$imgBase = Join-Path $outDir 'shell-static-base.png'
Capture-Region ([int]$r.X) ([int]$r.Y) ([int]$r.Width) ([int]$r.Height) $imgBase

# ツリー行(ListBoxItem)は複合テンプレートのため UIA 名が付かない。
# 行内の TextBlock を名前で見つけ、その座標クリックで選択+親 ListItem を辿って状態を読む
function Get-ContainingListItem($element) {
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $cur = $element
    while ($null -ne $cur) {
        if ($cur.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) { return $cur }
        $cur = $walker.GetParent($cur)
    }
    return $null
}

# ---- 2. ツリー選択 → 3D ハイライト(spec 6.25.3 片翼) ----
# 座標クリックはフォーカス状態に左右されるため、SelectionItemPattern で確実に選択する
$bossText = Find-ByNameAndType $root $LblBoss ([System.Windows.Automation.ControlType]::Text)
if ($null -eq $bossText) { Write-Output 'FAIL: boss tree row not found'; Stop-Process -Id $p.Id; exit 1 }
$bossItem = Get-ContainingListItem $bossText
Select-Item $bossItem
Start-Sleep -Milliseconds 700
Assert-Equals 'tree select -> selection status' $selStatus.Current.Name $LblSelBoss
$imgTreeSel = Join-Path $outDir 'shell-tree-select.png'
Capture-Region ([int]$r.X) ([int]$r.Y) ([int]$r.Width) ([int]$r.Height) $imgTreeSel
Assert-DiffAtLeast 'tree select -> 3D highlight' $imgBase $imgTreeSel 300

# ---- 3. 選択解除 → 3D パーツピック → ツリー選択(逆翼) ----
function Get-TreeItemSelected($item) {
    if ($null -eq $item) { return '(no listitem)' }
    return $item.GetCurrentPropertyValue(
        [System.Windows.Automation.SelectionItemPattern]::IsSelectedProperty)
}

Invoke-Button (Find-ByName $root $LblClear)
Start-Sleep -Milliseconds 500
Assert-Equals 'clear button -> none' $selStatus.Current.Name $LblSelNone
Assert-Equals 'clear button -> tree deselected' (Get-TreeItemSelected $bossItem) $false

Left-Click $cx $cy   # 中心 = 孔に通した円筒ボス
Start-Sleep -Milliseconds 500
Assert-Equals '3D pick -> selection status' $selStatus.Current.Name $LblSelBoss
Assert-Equals '3D pick -> tree selected' (Get-TreeItemSelected $bossItem) $true

# ---- 4. PropertyGrid 選択連動: 可視性チェック → 描画反映(spec 6.25.4) ----
# PropertyGrid 自体は UIA ピアを持たないため、行ラベル「表示」の Text から
# 親を辿って同じ行の CheckBox エディタを見つける
$LblVisible = [string][char]0x8868 + [char]0x793A   # 表示
$checkCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::CheckBox)

function Find-RowCheckBox($scope, $labelText) {
    $label = Find-ByNameAndType $scope $labelText ([System.Windows.Automation.ControlType]::Text)
    if ($null -eq $label) { return $null }
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $cur = $walker.GetParent($label)
    for ($i = 0; $i -lt 5 -and $null -ne $cur; $i++) {
        $cb = $cur.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $checkCond)
        if ($null -ne $cb) { return $cb }
        $cur = $walker.GetParent($cur)
    }
    return $null
}

$visibleCheck = Find-RowCheckBox $root $LblVisible
if ($null -eq $visibleCheck) {
    Write-Output 'FAIL part properties not shown (visible checkbox not found)'
    $script:failures++
} else {
    $imgSel = Join-Path $outDir 'shell-pick-select.png'
    Capture-Region ([int]$r.X) ([int]$r.Y) ([int]$r.Width) ([int]$r.Height) $imgSel

    Toggle-Element $visibleCheck   # 表示 OFF
    Start-Sleep -Milliseconds 700
    $imgHidden = Join-Path $outDir 'shell-part-hidden.png'
    Capture-Region ([int]$r.X) ([int]$r.Y) ([int]$r.Width) ([int]$r.Height) $imgHidden
    Assert-DiffAtLeast 'property visible=off hides part' $imgSel $imgHidden 300

    Toggle-Element $visibleCheck   # 表示 ON に戻す
    Start-Sleep -Milliseconds 700
}

# ---- 5. プローブ → LogConsole 記録(spec 6.25.5) ----
$probeRadio = Find-ByNameAndType $root $LblProbe ([System.Windows.Automation.ControlType]::RadioButton)
Select-Item $probeRadio
Start-Sleep -Milliseconds 400
$px = [int]($cx - $r.Width * 0.18)
Left-Click $px $cy   # 平板上(孔の左)
Start-Sleep -Milliseconds 700

# LogConsole 自体は UIA ピアを持たないため、シェル全体の Text 要素からログ行を探す
$textCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Text)
$texts = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)
$probeLogFound = $false
$probePattern = $Sigma + '_vM = [\d.]+ MPa \(N\d+\)'
foreach ($t in $texts) {
    if ($t.Current.Name -match $probePattern) { $probeLogFound = $true; break }
}
if ($probeLogFound) { Write-Output 'PASS probe result logged to console' }
else { Write-Output 'FAIL probe log line not found in LogConsole'; $script:failures++ }

# パーツ選択に戻す
Select-Item (Find-ByNameAndType $root $LblParts ([System.Windows.Automation.ControlType]::RadioButton))
Start-Sleep -Milliseconds 300

# ドキュメントタブの特定: モデルツリーのルート行が同名のため、TabItem がなければ
# ドキュメントペイン領域(x > 280、ツリーペインの右)にあるタブラベルの Text を使う
function Find-DocumentTab($scope, $name) {
    $tab = Find-ByNameAndType $scope $name ([System.Windows.Automation.ControlType]::TabItem)
    if ($null -ne $tab) { return $tab }
    $nameCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    foreach ($e in $scope.FindAll([System.Windows.Automation.TreeScope]::Descendants, $nameCond)) {
        if ($e.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) { continue }
        $er = $e.Current.BoundingRectangle
        # タブストリップはウィンドウ上部(y < 100)・ツリーペイン(x < 250)の右にある
        if ($er.X -gt 250 -and $er.Y -lt 100) { return $e }
    }
    return $null
}

function Select-Tab($tab) {
    try {
        Select-Item $tab
    } catch {
        # SelectionItemPattern 非対応のタブ(Text ラベル等)は座標クリックで切り替える
        $tr = $tab.Current.BoundingRectangle
        Left-Click ([int]($tr.X + $tr.Width / 2)) ([int]($tr.Y + $tr.Height / 2))
    }
}

# ---- 6. タブ切替: 過渡応答ドキュメント(ビューポート第2インスタンス+PlaybackBar) ----
$transTab = Find-DocumentTab $root $LblTransDoc
if ($null -eq $transTab) { Write-Output 'FAIL: transient document tab not found'; Stop-Process -Id $p.Id; exit 1 }
Write-Output ("transient tab type: " + $transTab.Current.ControlType.ProgrammaticName)
Select-Tab $transTab
Start-Sleep -Milliseconds 1500

$stats2 = Wait-ForText $statsStatus '2,000' 30
Write-Output ("stats(transient): " + $stats2)
Assert-Contains 'tab switch -> stats follow active doc' $stats2 $LblTrans
Assert-Contains 'transient triangle count' $stats2 ($LblTri + ' 2,000')

$vp2 = Get-ActiveViewport $root
$r2 = $vp2.Current.BoundingRectangle
$imgFrame0 = Join-Path $outDir 'shell-transient-f0.png'
Capture-Region ([int]$r2.X) ([int]$r2.Y) ([int]$r2.Width) ([int]$r2.Height) $imgFrame0

# PlaybackBar のスライダーでフレームを進める → Displacements 差し替え → 描画が変わる。
# PlaybackBar 自体は UIA ピアを持たないため、Maximum=89(フレーム数 90-1)のスライダーで特定する
$sliderCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Slider)
$slider = $null
foreach ($s in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $sliderCond)) {
    $range = $s.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
    if ($range.Current.Maximum -eq 89) { $slider = $s; break }
}
if ($null -eq $slider) { Write-Output 'FAIL: playback slider not found'; Stop-Process -Id $p.Id; exit 1 }
$slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(40)
Start-Sleep -Milliseconds 900
$imgFrame40 = Join-Path $outDir 'shell-transient-f40.png'
Capture-Region ([int]$r2.X) ([int]$r2.Y) ([int]$r2.Width) ([int]$r2.Height) $imgFrame40
Assert-DiffAtLeast 'playback frame changes deformation' $imgFrame0 $imgFrame40 300

# ---- 7. タブ復帰: 静解析ドキュメント(Unloaded → Loaded の再構築) ----
$staticTab = Find-DocumentTab $root $LblStaticDoc
Write-Output ("static tab type: " + $staticTab.Current.ControlType.ProgrammaticName +
    " rect: " + $staticTab.Current.BoundingRectangle.ToString())
for ($attempt = 0; $attempt -lt 3; $attempt++) {
    Select-Tab $staticTab
    Start-Sleep -Milliseconds 1200
    if ($statsStatus.Current.Name -like "*$LblStatic*") { break }
    # パターンで切り替わらない場合は座標クリックで再試行
    $tr = $staticTab.Current.BoundingRectangle
    Left-Click ([int]($tr.X + $tr.Width / 2)) ([int]($tr.Y + $tr.Height / 2))
    Start-Sleep -Milliseconds 1200
    if ($statsStatus.Current.Name -like "*$LblStatic*") { break }
}
$stats3 = Wait-ForText $statsStatus '4,800' 30
Assert-Contains 'tab return -> stats back to static doc' $stats3 $LblStatic

# ホバー(プローブ節点プレビューではなくパーツモード): ステータスバー連動
$vp = Get-ActiveViewport $root
$r = $vp.Current.BoundingRectangle
$cx = [int]($r.X + $r.Width / 2); $cy = [int]($r.Y + $r.Height / 2)
[Win32]::SetCursorPos([int]($cx - $r.Width * 0.18), $cy) | Out-Null
Start-Sleep -Milliseconds 200
[Win32]::SetCursorPos([int]($cx - $r.Width * 0.18) + 1, $cy) | Out-Null
Start-Sleep -Milliseconds 700
$hover = $hoverStatus.Current.Name
Assert-Contains 'hover status shows plate' $hover ($LblHover + ': ' + $LblPlate)

# ---- 8. ドキュメントのフローティング(D3DImage 再親付け) ----
$transTab = Find-DocumentTab $root $LblTransDoc
$tr = $transTab.Current.BoundingRectangle
Left-Drag ([int]($tr.X + $tr.Width / 2)) ([int]($tr.Y + $tr.Height / 2)) 660 990
Start-Sleep -Milliseconds 1500
$p.Refresh()
if ($p.HasExited) { Write-Output 'FAIL: process crashed on floating'; exit 1 }
Write-Output 'PASS floating document (process alive)'
Capture-Region 0 0 1280 1040 (Join-Path $outDir 'shell-floating.png')

# ---- 9. フローティング表示のままライトテーマ切替(複合パス) ----
# テーマトグルはメインギャラリーウィンドウ側にある
$desktop = [System.Windows.Automation.AutomationElement]::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$windows = $desktop.FindAll([System.Windows.Automation.TreeScope]::Children, $pidCond)
$themeToggle = $null
foreach ($w in $windows) {
    $themeToggle = Find-ByName $w $LblTheme
    if ($null -ne $themeToggle) { break }
}
if ($null -eq $themeToggle) {
    Write-Output 'FAIL: theme toggle not found'; $script:failures++
} else {
    Toggle-Element $themeToggle
    Start-Sleep -Milliseconds 2500
    $p.Refresh()
    if ($p.HasExited) { Write-Output 'FAIL: process crashed on theme switch'; exit 1 }
    Write-Output 'PASS theme switch with floating viewport (process alive)'
    [Win32]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 500
    Capture-Region 0 0 1280 1040 (Join-Path $outDir 'shell-light.png')

    # ライトテーマでもビューポート・同期が生きていることの最終確認
    $stats4 = $statsStatus.Current.Name
    Assert-Contains 'stats alive after theme switch' $stats4 $LblTri
}

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
if ($script:failures -gt 0) {
    Write-Output ("done with {0} FAILURE(S)" -f $script:failures)
    exit 1
}
Write-Output 'done: all assertions passed'
