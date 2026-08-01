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

function Capture-Region($x, $y, $w, $h, $path) {
    [Win32]::SetCursorPos(5, 5) | Out-Null
    Start-Sleep -Milliseconds 80
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

function Toggle-Switch($element) {
    $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
}

function Select-ComboItem($combo, $itemName) {
    $expand = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    Start-Sleep -Milliseconds 300
    $item = Find-ByName $combo $itemName
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 200
    $expand.Collapse()
}

function Click-At($x, $y) {
    [Win32]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 120
    [Win32]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [Win32]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 300
}

$script:failures = 0
function Assert-Contains($label, $text, $substring) {
    if ($text -like "*$substring*") {
        Write-Output ("PASS {0}: '{1}' found" -f $label, $substring)
    } else {
        Write-Output ("FAIL {0}: '{1}' not in '{2}'" -f $label, $substring, $text)
        $script:failures++
    }
}

# 統計テキストが構築完了を示すまで待つ(タイマー更新 500ms)
function Wait-ForStats($statsElement, $expectedTriangles, $timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $text = $statsElement.Current.Name
        if ($text -like "*$expectedTriangles*") {
            return $text
        }
        Start-Sleep -Milliseconds 500
    }
    return $statsElement.Current.Name
}

# 日本語ラベル(エンコーディング事故を避けるため char 合成)
$LblTheme = [string][char]0x30E9 + [char]0x30A4 + [char]0x30C8 + [char]0x30C6 + [char]0x30FC + [char]0x30DE  # ライトテーマ
$LblChunk = [string][char]0x30C1 + [char]0x30E3 + [char]0x30F3 + [char]0x30AF  # チャンク
$LblEdgeSkip = [string][char]0x30A8 + [char]0x30C3 + [char]0x30B8 + [char]0x30B9 + [char]0x30AD + [char]0x30C3 + [char]0x30D7  # エッジスキップ
$LblSelected = [string][char]0x9078 + [char]0x629E + [char]0x9762  # 選択面
$Lbl10M = '1,000' + [string][char]0x4E07  # 1,000万

$rootDir = 'd:\home\Programs\CSharpProjects\WpfCustomUI'
$exe = Join-Path $rootDir 'WpfCustomUI.Gallery\bin\Debug\net10.0-windows\WpfCustomUI.Gallery.exe'
$outDir = Join-Path $rootDir '.dev\captures'

$p = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 4
$p.Refresh()
$h = $p.MainWindowHandle
[Win32]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, 1280, 960, 0x0040) | Out-Null
[Win32]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 500
$root = [System.Windows.Automation.AutomationElement]::FromHandle($h)

(Find-ByName $root '3D Benchmark').GetCurrentPattern(
    [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 3000

$vp = Find-ByClass $root 'WcuViewport'
if ($null -eq $vp) { Write-Output 'FAIL: WcuViewport not found'; Stop-Process -Id $p.Id; exit 1 }
$r = $vp.Current.BoundingRectangle
$cx = [int]($r.X + $r.Width / 2); $cy = [int]($r.Y + $r.Height / 2)

$statsText = Find-ById $root 'StatsText'
$pickText = Find-ById $root 'PickText'
$sizeCombo = Find-ById $root 'SizeCombo'

# ---- 1. 既定 100万(side=707 → 999,698 三角形、1 チャンク、エッジ抽出あり) ----
$stats1M = Wait-ForStats $statsText '999,698' 60
Write-Output ("stats(1M): {0}" -f $stats1M)
Assert-Contains '1M triangles' $stats1M '999,698'
Assert-Contains '1M vertices' $stats1M '501,264'
Assert-Contains '1M single chunk' $stats1M ($LblChunk + ' 1 ')
Assert-Contains '1M edges extracted' $stats1M ($LblEdgeSkip + ' 0 ')

# ---- 2. ピック整合(100万、クリック → 選択面 1 件) ----
Click-At $cx $cy
Start-Sleep -Milliseconds 800
$pick1M = $pickText.Current.Name
Write-Output ("pick(1M): {0}" -f $pick1M)
Assert-Contains '1M pick selects one face' $pick1M ($LblSelected + ' 1 ')

Capture-Region 0 0 1280 960 (Join-Path $outDir 'benchmark-1m-dark.png')

# ---- 3. 1,000万(side=2236 → 9,999,392 三角形、節点 5,004,169 > 400万 → 複数チャンク、
#         三角形数 > EdgeExtractionLimit(500万) → エッジスキップ 1) ----
Select-ComboItem $sizeCombo $Lbl10M
$stats10M = Wait-ForStats $statsText '9,999,392' 180
Write-Output ("stats(10M): {0}" -f $stats10M)
Assert-Contains '10M triangles' $stats10M '9,999,392'
Assert-Contains '10M vertices' $stats10M '5,004,169'
Assert-Contains '10M edge skipped' $stats10M ($LblEdgeSkip + ' 1 ')

# チャンク数 >= 2 をアサート(数値を抜き出して比較)
if ($stats10M -match ($LblChunk + ' (\d+)')) {
    $chunks = [int]$Matches[1]
    if ($chunks -ge 2) {
        Write-Output ("PASS 10M multi-chunk: chunks={0}" -f $chunks)
    } else {
        Write-Output ("FAIL 10M multi-chunk: chunks={0} (expected >= 2)" -f $chunks)
        $script:failures++
    }
} else {
    Write-Output ("FAIL 10M multi-chunk: chunk count not found in '{0}'" -f $stats10M)
    $script:failures++
}

# ---- 4. ピック整合(1,000万、チャンク基点オフセットの検証) ----
# 真上視点にすると波面板が画面いっぱいに写り、三角形 ID(x 行順)が画面方向に単調に
# 並ぶ。3×3 の格子でクリックスキャンし、チャンク 1(ID < 250万)とチャンク 2
# (ID > 750万、チャンク境界 ≈ 799万)の両方がピックできることを検証する
(Find-ById $root 'TopViewButton').GetCurrentPattern(
    [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 500
(Find-ByName $root 'Fit').GetCurrentPattern(
    [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 800

# フィット後の板の半幅は最小辺の約 30%(バウンディング球フィットのため板対角/
# 球直径 ≈ 0.7 を掛けた値)。±22% × 最小辺 のオフセットなら確実に板の上に乗り、
# 三角形 ID は中心±75% の行に届く(チャンク境界 ≈ 799万を跨ぐ)
$off = [int]([Math]::Min($r.Width, $r.Height) * 0.22)
Capture-Region ([int]$r.X) ([int]$r.Y) ([int]$r.Width) ([int]$r.Height) (Join-Path $outDir 'benchmark-10m-topview.png')
$pickedIds = @()
foreach ($dx in @(-$off, 0, $off)) {
    foreach ($dy in @(-$off, 0, $off)) {
        Click-At ($cx + $dx) ($cy + $dy)
        Start-Sleep -Milliseconds 700
        $text = $pickText.Current.Name
        Write-Output ("  scan({0},{1}): {2}" -f $dx, $dy, $text)
        if ($text -match 'ID \[(\d+)') {
            $pickedIds += [long]$Matches[1]
        }
    }
}

Write-Output ("pick(10M) scanned ids: {0}" -f ($pickedIds -join ', '))
if ($pickedIds.Count -ge 5) {
    Write-Output ("PASS 10M pick works at scale: {0}/9 hits" -f $pickedIds.Count)
} else {
    Write-Output ("FAIL 10M pick works at scale: only {0}/9 hits" -f $pickedIds.Count)
    $script:failures++
}

$minId = ($pickedIds | Measure-Object -Minimum).Minimum
$maxId = ($pickedIds | Measure-Object -Maximum).Maximum
if ($minId -lt 2500000 -and $maxId -gt 7500000 -and $maxId -lt 9999392) {
    Write-Output ("PASS 10M pick spans both chunks: min={0} max={1}" -f $minId, $maxId)
} else {
    Write-Output ("FAIL 10M pick spans both chunks: min={0} max={1} (expected min<2.5M, 7.5M<max<9,999,392)" -f $minId, $maxId)
    $script:failures++
}

Capture-Region 0 0 1280 960 (Join-Path $outDir 'benchmark-10m-dark.png')

# ---- 5. ライトテーマのスクリーンショット(目視用) ----
Toggle-Switch (Find-ByName $root $LblTheme)
Start-Sleep -Milliseconds 2000
Capture-Region 0 0 1280 960 (Join-Path $outDir 'benchmark-10m-light.png')
Write-Output 'light theme captured'

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
if ($script:failures -gt 0) {
    Write-Output ("done with {0} FAILURE(S)" -f $script:failures)
    exit 1
}
Write-Output 'done: all assertions passed'
