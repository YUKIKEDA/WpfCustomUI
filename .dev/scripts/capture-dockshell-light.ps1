# ライトテーマでドッキングシェルデモを撮影する(Phase 15 検証用)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
"@

$rootDir = 'd:\home\Programs\CSharpProjects\WpfCustomUI'
$exe = Join-Path $rootDir 'WpfCustomUI.Gallery\bin\Debug\net10.0-windows\WpfCustomUI.Gallery.exe'

$p = Start-Process -FilePath $exe -ArgumentList '--light', '--dockshell' -PassThru
Start-Sleep -Seconds 6
$p.Refresh()
# --dockshell はメインの上にシェルウィンドウを開くので、前面のシェルをそのまま撮る
[Win32]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, 0, 0, 1280, 960, 0x0040) | Out-Null
Start-Sleep -Milliseconds 1500

$b = New-Object System.Drawing.Bitmap(1280, 960)
$g = [System.Drawing.Graphics]::FromImage($b)
$g.CopyFromScreen(0, 0, 0, 0, (New-Object System.Drawing.Size(1280, 960)))
$g.Dispose()
$b.Save((Join-Path $rootDir '.dev\captures\themes\light\dockshell.png'))
$b.Dispose()

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
Write-Output 'captured'
