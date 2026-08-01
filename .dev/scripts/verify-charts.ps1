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

function Capture-Screen($x, $y, $w, $h, $path) {
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

function Find-TextLike($scope, $pattern) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text)
    foreach ($t in $scope.FindAll([System.Windows.Automation.TreeScope]::Descendants, $c)) {
        if ($t.Current.Name -like $pattern) { return $t }
    }
    return $null
}

function Invoke-Element($element) {
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Get-Toggle($element) {
    return $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
}

function Get-Scroller($scope) {
    $all = $scope.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($el in $all) {
        if ($el.GetSupportedPatterns() -contains [System.Windows.Automation.ScrollPattern]::Pattern) {
            $sp = $el.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
            if ($sp.Current.VerticallyScrollable) { return $sp }
        }
    }
    return $null
}

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

(Find-ByName $root 'Charts').GetCurrentPattern(
    [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 1500
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'charts-top.png')

# ---- 1. ConvergenceMonitor: 解析開始 → ストリーミング → 収束 ----
$solve = Find-ByName $root ([string][char]0x89E3 + [char]0x6790 + [char]0x958B + [char]0x59CB)  # '解析開始'
Invoke-Element $solve
Start-Sleep -Milliseconds 2500
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'charts-solving.png')
$running = Find-TextLike $root ([string][char]0x89E3 + [char]0x6790 + [char]0x4E2D + '*')  # '解析中...'
Write-Output ("solver running: " + ($null -ne $running))

# 収束を待つ(最長 12 秒)
$converged = $false
for ($i = 0; $i -lt 24; $i++) {
    Start-Sleep -Milliseconds 500
    $done = Find-TextLike $root ([string][char]0x53CE + [char]0x675F + '*')  # '収束しました'
    if ($null -ne $done) { $converged = $true; break }
}
Write-Output ("solver converged: " + $converged)
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'charts-converged.png')

# ---- 2. HistoryChart: 十字カーソル(マウスホバー) ----
$scroller = Get-Scroller $root
$scroller.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 25)
Start-Sleep -Milliseconds 800
# チャート中央へマウスを移動(実イベントで MouseMove を発生させる)
[Win32]::SetCursorPos(700, 500) | Out-Null
[Win32]::mouse_event(0x0001, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 300
[Win32]::SetCursorPos(720, 480) | Out-Null
[Win32]::mouse_event(0x0001, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 600
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'charts-history-crosshair.png')
Write-Output 'history crosshair captured'

# ---- 3. FrequencyResponsePlot: 位相 OFF / dB OFF ----
[Win32]::SetCursorPos(30, 900) | Out-Null   # チャートからマウスを外す
$scroller.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 55)
Start-Sleep -Milliseconds 800
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'charts-frf.png')

$phase = Find-ByName $root ([string][char]0x4F4D + [char]0x76F8 + [char]0x3092 + [char]0x8868 + [char]0x793A)  # '位相を表示'
(Get-Toggle $phase).Toggle()
Start-Sleep -Milliseconds 800
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'charts-frf-nophase.png')
Write-Output 'phase panel hidden'
(Get-Toggle $phase).Toggle()
Start-Sleep -Milliseconds 500

# ---- 4. HistogramChart: ビン数変更 + 正規化 ----
$scroller.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 80)
Start-Sleep -Milliseconds 800
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'charts-histogram.png')

$sliderCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Slider)
$binSlider = $null
foreach ($s in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $sliderCond)) {
    $rv = $s.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
    if ($rv.Current.Maximum -eq 60) { $binSlider = $rv; break }
}
if ($null -eq $binSlider) { Write-Output 'FAIL: bin slider not found' }
else {
    $binSlider.SetValue(10)
    Start-Sleep -Milliseconds 600
    Capture-Screen 0 0 1280 960 (Join-Path $outDir 'charts-histogram-bins10.png')
    Write-Output 'bin count changed to 10'
}

$normalize = Find-ByName $root ([string][char]0x5BC6 + [char]0x5EA6 + [char]0x306B + [char]0x6B63 + [char]0x898F + [char]0x5316)  # '密度に正規化'
(Get-Toggle $normalize).Toggle()
Start-Sleep -Milliseconds 600
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'charts-histogram-density.png')
Write-Output 'normalized to density'

# ---- 5. 素の WcuPlot + テーマ追従(アクセント変更) ----
$scroller.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 100)
Start-Sleep -Milliseconds 800
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'charts-freeplot.png')
Write-Output 'free plot captured'

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
Write-Output 'done'
