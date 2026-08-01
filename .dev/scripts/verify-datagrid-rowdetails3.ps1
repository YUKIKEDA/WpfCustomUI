$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
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

function Toggle($element) {
    $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
}

$rootDir = 'd:\home\Programs\CSharpProjects\WpfCustomUI'
$exe = Join-Path $rootDir 'WpfCustomUI.Gallery\bin\Debug\net10.0-windows\WpfCustomUI.Gallery.exe'
$outDir = Join-Path $rootDir '.dev\captures'

$p = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 4
$p.Refresh()
$h = $p.MainWindowHandle
$root = [System.Windows.Automation.AutomationElement]::FromHandle($h)

(Find-ByName $root 'DataGrid').GetCurrentPattern(
    [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 1000

# 1行目の詳細を開く
$dgCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::DataGrid)
$dg = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $dgCond)
$grid = $dg.GetCurrentPattern([System.Windows.Automation.GridPattern]::Pattern)
$cell = $grid.GetItem(0, 0)
$btnCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Button)
Toggle ($cell.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond))
Start-Sleep -Milliseconds 400
Capture $h (Join-Path $outDir 'datagrid-details-on.png')
Write-Output 'details opened'

# RowDetails チェックを OFF
$check = Find-ByName $root ('RowDetails' + [char]0xFF08 + [char]0x884C + [char]0x5148 + [char]0x982D + [char]0x306E + ' ' + [char]0x25B6 + ' ' + [char]0x3067 + [char]0x958B + [char]0x9589 + [char]0xFF09)
if ($null -eq $check) {
    # 名前一致に失敗した場合は CheckBox を列挙して RowDetails を含むものを探す
    $cbCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::CheckBox)
    foreach ($cb in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cbCond)) {
        if ($cb.Current.Name -like '*RowDetails*') { $check = $cb; break }
    }
}
if ($null -eq $check) { Write-Output 'checkbox not found'; Stop-Process -Id $p.Id; exit 1 }
Toggle $check
Start-Sleep -Milliseconds 500
Capture $h (Join-Path $outDir 'datagrid-details-off.png')
Write-Output 'details disabled'

# 再度 ON に戻す
Toggle $check
Start-Sleep -Milliseconds 500
Capture $h (Join-Path $outDir 'datagrid-details-reon.png')
Write-Output 'details re-enabled'

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
Write-Output 'done'
