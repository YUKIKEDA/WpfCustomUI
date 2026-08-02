# Phase 27 M2: CaeStudio リボン UI のスモークキャプチャ。
# 起動直後(モデルタブ+空状態ガイド+最小パネル) → 解析タブ → 解析実行 →
# 完了後(結果タブへ自動切替+段階連動パネル表示) の 3 状態を撮影する。
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, IntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
}
"@

function Capture-Screen($x, $y, $w, $h, $path) {
    [Win32]::SetCursorPos(5, 5) | Out-Null
    Start-Sleep -Milliseconds 150
    $b = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $g.Dispose()
    $b.Save($path)
    $b.Dispose()
}

function Left-Click($x, $y) {
    [Win32]::SetCursorPos([int]$x, [int]$y) | Out-Null
    Start-Sleep -Milliseconds 120
    [Win32]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    [Win32]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 200
}

function Find-ById($scope, $automationId) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

$rootDir = 'd:\home\Programs\CSharpProjects\WpfCustomUI'
$exe = Join-Path $rootDir 'samples\CaeStudio.App\bin\Debug\net10.0-windows\CaeStudio.exe'
$outDir = Join-Path $rootDir '.dev\captures\phase27-m2'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$light = $args -contains '-light'
$themeSuffix = if ($light) { 'light' } else { 'dark' }

$p = Start-Process -FilePath $exe -PassThru
try {
Start-Sleep -Seconds 5
$p.Refresh()
$h = $p.MainWindowHandle
[Win32]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, 1400, 900, 0x0040) | Out-Null
[Win32]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 800
$root = [System.Windows.Automation.AutomationElement]::FromHandle($h)

# ライトテーマ指定時はリボン右端のテーマトグルを ON にする
if ($light) {
    $themeToggle = Find-ById $root 'ThemeToggle'
    if ($null -eq $themeToggle) {
        $lightLabel = [string][char]0x30E9 + [char]0x30A4 + [char]0x30C8  # 'ライト'
        $toggleCond = New-Object System.Windows.Automation.AndCondition @(
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::NameProperty, $lightLabel)),
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button)))
        $themeToggle = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $toggleCond)
    }
    if ($null -ne $themeToggle) {
        try {
            $pattern = $themeToggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
            if ($pattern.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { $pattern.Toggle() }
        } catch {
            $r = $themeToggle.Current.BoundingRectangle
            Left-Click ($r.X + $r.Width / 2) ($r.Y + $r.Height / 2)
        }
        Start-Sleep -Milliseconds 1000
    } else {
        Write-Output 'WARN: ThemeToggle not found'
    }
}

# 1. 起動直後(モデルタブ+空状態ガイド、下部はログのみ)
Capture-Screen 0 0 1400 900 (Join-Path $outDir "startup-$themeSuffix.png")

# 2. 解析タブへ切替(リボン領域 Y < 100 の TabItem)
$analysisName = [string][char]0x89E3 + [string][char]0x6790  # '解析'
$tabCond = New-Object System.Windows.Automation.AndCondition @(
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $analysisName)),
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::TabItem)))
$analysisTab = $null
foreach ($candidate in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tabCond)) {
    if ($candidate.Current.BoundingRectangle.Y -lt 100) { $analysisTab = $candidate; break }
}
if ($null -eq $analysisTab) { throw 'analysis ribbon tab not found' }
$analysisTab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 800
Capture-Screen 0 0 1400 900 (Join-Path $outDir "analysis-tab-$themeSuffix.png")

# 3. 解析実行 → 完了待ち(ステータス '解析完了')
$runButton = Find-ById $root 'RunButton'
if ($null -eq $runButton) { throw 'RunButton not found' }
$runButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

$doneText = [string][char]0x89E3 + [char]0x6790 + [char]0x5B8C + [char]0x4E86  # '解析完了'
$status = Find-ById $root 'StatusText'
if ($null -eq $status) { throw 'StatusText not found after run' }
$sw = [System.Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt 90) {
    $status = Find-ById $root 'StatusText'
    if ($null -eq $status) { Start-Sleep -Milliseconds 400; continue }
    $text = $status.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::NameProperty)
    if ($text -match $doneText) { break }
    Start-Sleep -Milliseconds 400
}
Write-Output ("status: " + $status.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::NameProperty))
Start-Sleep -Seconds 2

# 4. 完了後(結果タブへ自動切替+収束/パス/ヒストグラム/凡例パネル表示)
Capture-Screen 0 0 1400 900 (Join-Path $outDir "result-$themeSuffix.png")

Write-Output "done ($themeSuffix)"
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
}
