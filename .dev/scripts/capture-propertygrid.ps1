$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
"@

$rootDir = 'd:\home\Programs\CSharpProjects\WpfCustomUI'
$exe = Join-Path $rootDir 'WpfCustomUI.Gallery\bin\Debug\net10.0-windows\WpfCustomUI.Gallery.exe'

$p = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 4
$p.Refresh()
[Win32]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, 0, 0, 1280, 940, 0x0040) | Out-Null
$root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)

$c = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, 'PropertyGrid')
($root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)).GetCurrentPattern(
    [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 1200

$b = New-Object System.Drawing.Bitmap(1280, 940)
$g = [System.Drawing.Graphics]::FromImage($b)
$g.CopyFromScreen(0, 0, 0, 0, (New-Object System.Drawing.Size(1280, 940)))
$b.Save((Join-Path $rootDir '.dev\captures\pickers-propertygrid.png'))
Stop-Process -Id $p.Id
Write-Output 'done'
