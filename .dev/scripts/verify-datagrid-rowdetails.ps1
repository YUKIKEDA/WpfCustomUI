$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

function Capture($h, $path) {
    $rect = New-Object Win32+RECT
    [Win32]::GetWindowRect($h, [ref]$rect) | Out-Null
    $b = New-Object System.Drawing.Bitmap(($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top))
    $g = [System.Drawing.Graphics]::FromImage($b)
    $hdc = $g.GetHdc()
    [Win32]::PrintWindow($h, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc)
    $b.Save($path)
}

function Find-ByName($scope, $name) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    return $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

$rootDir = 'd:\home\Programs\CSharpProjects\WpfCustomUI'
$exe = Join-Path $rootDir 'WpfCustomUI.Gallery\bin\Debug\net10.0-windows\WpfCustomUI.Gallery.exe'
$outDir = Join-Path $rootDir '.dev\captures'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$p = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 4
$p.Refresh()
$h = $p.MainWindowHandle
$root = [System.Windows.Automation.AutomationElement]::FromHandle($h)

[Win32]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0013) | Out-Null
[Win32]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 300

(Find-ByName $root 'DataGrid').GetCurrentPattern(
    [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 1000

# 1行目と3行目の ▶ トグルを物理クリックで開く
$dgCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::DataGrid)
$dg = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $dgCond)
$grid = $dg.GetCurrentPattern([System.Windows.Automation.GridPattern]::Pattern)

foreach ($row in @(0, 2)) {
    $cell = $grid.GetItem($row, 0)
    $cr = $cell.Current.BoundingRectangle
    $cx = [int]($cr.X + $cr.Width / 2)
    $cy = [int]($cr.Y + $cr.Height / 2)
    [Win32]::SetCursorPos($cx, $cy) | Out-Null
    Start-Sleep -Milliseconds 200
    [Win32]::mouse_event(2,0,0,0,[UIntPtr]::Zero); [Win32]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
    Write-Output ("toggled row " + $row)
}

Capture $h (Join-Path $outDir 'datagrid-rowdetails-open.png')

# 1行目を閉じる
$cell = $grid.GetItem(0, 0)
$cr = $cell.Current.BoundingRectangle
[Win32]::SetCursorPos([int]($cr.X + $cr.Width / 2), [int]($cr.Y + $cr.Height / 2)) | Out-Null
Start-Sleep -Milliseconds 200
[Win32]::mouse_event(2,0,0,0,[UIntPtr]::Zero); [Win32]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Milliseconds 400
Capture $h (Join-Path $outDir 'datagrid-rowdetails-closed.png')

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
Write-Output 'done'
