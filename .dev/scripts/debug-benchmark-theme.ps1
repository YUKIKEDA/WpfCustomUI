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
function Find-ByClass($scope, $className) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ClassNameProperty, $className)
    return $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}
$LblTheme = [string][char]0x30E9 + [char]0x30A4 + [char]0x30C8 + [char]0x30C6 + [char]0x30FC + [char]0x30DE

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
Start-Sleep -Milliseconds 4000

$vp = Find-ByClass $root 'WcuViewport'
$r = $vp.Current.BoundingRectangle

# 1,000万へ切り替えてから検証(verify スクリプト末尾と同じ状態を再現)
function Find-ById($scope, $automationId) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}
$sizeCombo = Find-ById $root 'SizeCombo'
$expand = $sizeCombo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
$expand.Expand()
Start-Sleep -Milliseconds 300
$Lbl10M = '1,000' + [string][char]0x4E07
(Find-ByName $sizeCombo $Lbl10M).GetCurrentPattern(
    [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 200
$expand.Collapse()
Start-Sleep -Seconds 20

Capture-Region ([int]$r.X) ([int]$r.Y) ([int]$r.Width) ([int]$r.Height) (Join-Path $outDir 'bench-theme-dark.png')

$statsText = Find-ById $root 'StatsText'
Write-Output ("stats before toggle: {0}" -f $statsText.Current.Name)

(Find-ByName $root $LblTheme).GetCurrentPattern(
    [System.Windows.Automation.TogglePattern]::Pattern).Toggle()
Start-Sleep -Milliseconds 2500
Capture-Region ([int]$r.X) ([int]$r.Y) ([int]$r.Width) ([int]$r.Height) (Join-Path $outDir 'bench-theme-light.png')
Write-Output ("stats after toggle:  {0}" -f $statsText.Current.Name)
Start-Sleep -Milliseconds 4000
Write-Output ("stats after 4s more: {0}" -f $statsText.Current.Name)
Capture-Region ([int]$r.X) ([int]$r.Y) ([int]$r.Width) ([int]$r.Height) (Join-Path $outDir 'bench-theme-light-late.png')

# Fit ボタンでカメラ変更 → 再描画を強制。これで直るなら「invalidate 漏れ」、
# 直らないなら「レンダラー破損(_renderBroken)」
(Find-ByName $root 'Fit').GetCurrentPattern(
    [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 2000
Capture-Region ([int]$r.X) ([int]$r.Y) ([int]$r.Width) ([int]$r.Height) (Join-Path $outDir 'bench-theme-light-after-fit.png')

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
Write-Output 'done'
