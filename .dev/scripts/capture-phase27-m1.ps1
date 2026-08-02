# Phase 27 M1 の新規ページ(Icons / Ribbon / Inputs & Buttons)のスクリーンショットを撮る。
# Ribbon はタブ切替と SplitButton メニューも撮影する。
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
}

$rootDir = 'd:\home\Programs\CSharpProjects\WpfCustomUI'
$exe = Join-Path $rootDir 'WpfCustomUI.Gallery\bin\Debug\net10.0-windows\WpfCustomUI.Gallery.exe'
$outDir = Join-Path $rootDir '.dev\captures\phase27-m1'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$light = $args -contains '-light'
$themeSuffix = if ($light) { 'light' } else { 'dark' }
$procArgs = if ($light) { @('--light') } else { @() }

$p = if ($procArgs.Count -gt 0) { Start-Process -FilePath $exe -ArgumentList $procArgs -PassThru } else { Start-Process -FilePath $exe -PassThru }
Start-Sleep -Seconds 4
$p.Refresh()
$h = $p.MainWindowHandle
[Win32]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, 1280, 960, 0x0040) | Out-Null
[Win32]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 500
$root = [System.Windows.Automation.AutomationElement]::FromHandle($h)

function Select-Page($name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $item = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($null -eq $item) { throw "page not found: $name" }
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 900
}

# 1. Icons ページ
Select-Page 'Icons'
Capture-Screen 0 0 1280 960 (Join-Path $outDir "icons-$themeSuffix.png")

# 2. Inputs & Buttons(4 階層+アイコン付き)
Select-Page 'Inputs & Buttons'
Capture-Screen 0 0 1280 960 (Join-Path $outDir "buttons-$themeSuffix.png")

# 3. Ribbon: モデルタブ
Select-Page 'Ribbon'
Capture-Screen 0 0 1280 960 (Join-Path $outDir "ribbon-model-$themeSuffix.png")

# 4. Ribbon: 解析タブ(アクセント SplitButton)
$analysisName = [string][char]0x89E3 + [string][char]0x6790  # '解析'
$tabCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, $analysisName)
$tab = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tabCond)
if ($null -ne $tab) {
    $r = $tab.Current.BoundingRectangle
    Left-Click ($r.X + $r.Width / 2) ($r.Y + $r.Height / 2)
    Start-Sleep -Milliseconds 600
    Capture-Screen 0 0 1280 960 (Join-Path $outDir "ribbon-analysis-$themeSuffix.png")
} else {
    Write-Output 'WARN: analysis tab not found'
}

# 5. Ribbon: 結果タブ(トグル)
$resultsName = [string][char]0x7D50 + [string][char]0x679C  # '結果'
$tabCond2 = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, $resultsName)
$tab2 = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tabCond2)
if ($null -ne $tab2) {
    $r2 = $tab2.Current.BoundingRectangle
    Left-Click ($r2.X + $r2.Width / 2) ($r2.Y + $r2.Height / 2)
    Start-Sleep -Milliseconds 600
    Capture-Screen 0 0 1280 960 (Join-Path $outDir "ribbon-results-$themeSuffix.png")
}

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
Write-Output "done ($themeSuffix)"
