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
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, int data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
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

$scroller = Get-Scroller $root
$scroller.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 25)
Start-Sleep -Milliseconds 800
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'wheel-before.png')

# ---- 1. チャート上で素のホイール → ページがスクロールし、ズームしないこと ----
[Win32]::SetCursorPos(700, 480) | Out-Null   # HistoryChart 上
[Win32]::mouse_event(0x0001, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 300
$pctBefore = $scroller.Current.VerticalScrollPercent
for ($i = 0; $i -lt 3; $i++) {
    [Win32]::mouse_event(0x0800, 0, 0, -120, [UIntPtr]::Zero)  # ホイール下
    Start-Sleep -Milliseconds 150
}
Start-Sleep -Milliseconds 500
$pctAfter = $scroller.Current.VerticalScrollPercent
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'wheel-plain-scrolls.png')
Write-Output ("plain wheel over chart scrolls page: " + ($pctAfter -gt $pctBefore) + " (before=$pctBefore after=$pctAfter)")

# ---- 2. チャート上で Ctrl+ホイール → ズームし、ページはスクロールしないこと ----
$scroller.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 25)
Start-Sleep -Milliseconds 800
[Win32]::SetCursorPos(700, 480) | Out-Null
[Win32]::mouse_event(0x0001, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 300
$pctBefore2 = $scroller.Current.VerticalScrollPercent

[Win32]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)   # Ctrl down
Start-Sleep -Milliseconds 150
for ($i = 0; $i -lt 4; $i++) {
    [Win32]::mouse_event(0x0800, 0, 0, 120, [UIntPtr]::Zero)  # ホイール上(ズームイン)
    Start-Sleep -Milliseconds 150
}
[Win32]::keybd_event(0x11, 0, 0x2, [UIntPtr]::Zero) # Ctrl up
Start-Sleep -Milliseconds 500

$pctAfter2 = $scroller.Current.VerticalScrollPercent
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'wheel-ctrl-zooms.png')
Write-Output ("page did NOT scroll during Ctrl+wheel: " + ([math]::Abs($pctAfter2 - $pctBefore2) -lt 0.5) + " (before=$pctBefore2 after=$pctAfter2)")
Write-Output 'compare wheel-before.png vs wheel-ctrl-zooms.png for zoom / vs wheel-plain-scrolls.png for no-zoom'

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
Write-Output 'done'
