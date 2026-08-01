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

function Find-ByClass($scope, $className) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ClassNameProperty, $className)
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

function Get-Toggle($element) {
    return $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
}

# 日本語ラベル(エンコーディング事故を避けるため char 合成)
$LblOrtho    = [string][char]0x5E73 + [char]0x884C + [char]0x6295 + [char]0x5F71                     # 平行投影
$LblContour  = [string][char]0x30B3 + [char]0x30F3 + [char]0x30BF + [char]0x30FC                     # コンター
$LblEdge     = [string][char]0x30A8 + [char]0x30C3 + [char]0x30B8                                    # エッジ
$LblDiscrete = [string][char]0x96E2 + [char]0x6563 + ' 10 ' + [char]0x5206 + [char]0x5272           # 離散 10 分割
$LblTheme    = [string][char]0x30E9 + [char]0x30A4 + [char]0x30C8 + [char]0x30C6 + [char]0x30FC + [char]0x30DE  # ライトテーマ
$PatRenderer = [string][char]0x30EC + [char]0x30F3 + [char]0x30C0 + [char]0x30EA + [char]0x30F3 + [char]0x30B0 + '*'  # レンダリング*

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

(Find-ByName $root '3D Viewport').GetCurrentPattern(
    [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 2000
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'viewport-initial.png')

# レンダリング経路の表示を確認
$info = Find-TextLike $root $PatRenderer
if ($null -ne $info) { Write-Output ("renderer: " + $info.Current.Name) }
else { Write-Output 'FAIL: renderer info not found' }

# ビューポートの画面座標
$vp = Find-ByClass $root 'WcuViewport'
if ($null -eq $vp) { Write-Output 'FAIL: WcuViewport not found'; Stop-Process -Id $p.Id; exit 1 }
$r = $vp.Current.BoundingRectangle
$cx = [int]($r.X + $r.Width / 2)
$cy = [int]($r.Y + $r.Height / 2)
Write-Output ("viewport rect: " + $r.X + "," + $r.Y + " " + $r.Width + "x" + $r.Height)

# ---- 1. 中ボタンドラッグで回転 ----
[Win32]::SetCursorPos($cx, $cy) | Out-Null
Start-Sleep -Milliseconds 200
[Win32]::mouse_event(0x0020, 0, 0, 0, [UIntPtr]::Zero)   # MIDDLEDOWN
for ($i = 1; $i -le 10; $i++) {
    [Win32]::SetCursorPos($cx + $i * 12, $cy - $i * 6) | Out-Null
    Start-Sleep -Milliseconds 30
}
[Win32]::mouse_event(0x0040, 0, 0, 0, [UIntPtr]::Zero)   # MIDDLEUP
Start-Sleep -Milliseconds 800
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'viewport-orbit.png')
Write-Output 'orbit done'

# ---- 2. ホイールでカーソル位置ズームイン ----
[Win32]::SetCursorPos([int]($r.X + $r.Width * 0.6), [int]($r.Y + $r.Height * 0.4)) | Out-Null
Start-Sleep -Milliseconds 200
for ($i = 0; $i -lt 4; $i++) {
    [Win32]::mouse_event(0x0800, 0, 0, 120, [UIntPtr]::Zero)  # WHEEL +
    Start-Sleep -Milliseconds 150
}
Start-Sleep -Milliseconds 800
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'viewport-zoom.png')
Write-Output 'zoom done'

# ---- 3. 中ボタンダブルクリックで Fit ----
[Win32]::SetCursorPos($cx, $cy) | Out-Null
Start-Sleep -Milliseconds 200
[Win32]::mouse_event(0x0020, 0, 0, 0, [UIntPtr]::Zero)
[Win32]::mouse_event(0x0040, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 80
[Win32]::mouse_event(0x0020, 0, 0, 0, [UIntPtr]::Zero)
[Win32]::mouse_event(0x0040, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 800
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'viewport-fit.png')
Write-Output 'fit done'

# ---- 4. 平行投影 ----
(Get-Toggle (Find-ByName $root $LblOrtho)).Toggle()
Start-Sleep -Milliseconds 800
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'viewport-ortho.png')
Write-Output 'ortho done'

# ---- 5. コンター OFF(単色表示) ----
(Get-Toggle (Find-ByName $root $LblContour)).Toggle()
Start-Sleep -Milliseconds 800
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'viewport-nocontour.png')
(Get-Toggle (Find-ByName $root $LblContour)).Toggle()
Start-Sleep -Milliseconds 400
Write-Output 'contour toggle done'

# ---- 6. エッジ OFF ----
(Get-Toggle (Find-ByName $root $LblEdge)).Toggle()
Start-Sleep -Milliseconds 800
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'viewport-noedge.png')
(Get-Toggle (Find-ByName $root $LblEdge)).Toggle()
Start-Sleep -Milliseconds 400
Write-Output 'edge toggle done'

# ---- 7. 離散 10 分割 ----
(Get-Toggle (Find-ByName $root $LblDiscrete)).Toggle()
Start-Sleep -Milliseconds 800
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'viewport-discrete.png')
Write-Output 'discrete done'

# ---- 8. ライトテーマ ----
(Get-Toggle (Find-ByName $root $LblTheme)).Toggle()
Start-Sleep -Milliseconds 1500
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'viewport-light.png')
Write-Output 'light theme done'

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
Write-Output 'done'
