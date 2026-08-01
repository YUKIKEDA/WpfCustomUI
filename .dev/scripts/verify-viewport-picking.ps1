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

function Find-ByClass($scope, $className) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ClassNameProperty, $className)
    return $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

function Find-ById($scope, $automationId) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

function Select-Radio($element) {
    $element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}

function Invoke-Button($element) {
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Left-Click($x, $y) {
    [Win32]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 120
    [Win32]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)  # LEFTDOWN
    Start-Sleep -Milliseconds 60
    [Win32]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)  # LEFTUP
    Start-Sleep -Milliseconds 350
}

function Ctrl-Left-Click($x, $y) {
    [Win32]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)       # CTRL down
    Start-Sleep -Milliseconds 60
    Left-Click $x $y
    [Win32]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero)       # CTRL up
    Start-Sleep -Milliseconds 150
}

function Left-Drag($x0, $y0, $x1, $y1) {
    [Win32]::SetCursorPos($x0, $y0) | Out-Null
    Start-Sleep -Milliseconds 150
    [Win32]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    for ($i = 1; $i -le 10; $i++) {
        $mx = [int]($x0 + ($x1 - $x0) * $i / 10)
        $my = [int]($y0 + ($y1 - $y0) * $i / 10)
        [Win32]::SetCursorPos($mx, $my) | Out-Null
        Start-Sleep -Milliseconds 30
    }
    [Win32]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
}

# 日本語ラベル(エンコーディング事故を避けるため char 合成)
$LblFace   = [string][char]0x9762                                                                    # 面
$LblNode   = [string][char]0x7BC0 + [char]0x70B9                                                     # 節点
$LblPart   = [string][char]0x30D1 + [char]0x30FC + [char]0x30C4                                      # パーツ
$LblNone   = [string][char]0x306A + [char]0x3057                                                     # なし
$LblClear  = [string][char]0x9078 + [char]0x629E + [char]0x89E3 + [char]0x9664                       # 選択解除
$LblFront  = [string][char]0x6B63 + [char]0x9762                                                     # 正面
$LblTop    = [string][char]0x4E0A                                                                    # 上
$LblIso    = [string][char]0x7B49 + [char]0x89D2                                                     # 等角
$LblTheme  = [string][char]0x30E9 + [char]0x30A4 + [char]0x30C8 + [char]0x30C6 + [char]0x30FC + [char]0x30DE  # ライトテーマ

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

$vp = Find-ByClass $root 'WcuViewport'
if ($null -eq $vp) { Write-Output 'FAIL: WcuViewport not found'; Stop-Process -Id $p.Id; exit 1 }
$r = $vp.Current.BoundingRectangle
$cx = [int]($r.X + $r.Width / 2)
$cy = [int]($r.Y + $r.Height / 2)

$summary = Find-ById $root 'SelectionSummary'
function Get-Summary { $summary.Current.Name }
Write-Output ("initial: " + (Get-Summary))

# ---- 1. 面モード: クリック選択(中心から左 15% = 孔の左側の平板面) ----
Select-Radio (Find-ByName $root $LblFace)
Start-Sleep -Milliseconds 300
$px = [int]($cx - $r.Width * 0.15)
$py = $cy
Left-Click $px $py
Write-Output ("face click: " + (Get-Summary))
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'pick-face-click.png')

# ---- 2. Ctrl+クリックで追加 → 同じ場所を Ctrl+クリックでトグル解除 ----
$px2 = $px - 40
Ctrl-Left-Click $px2 $py
Write-Output ("face ctrl-add: " + (Get-Summary))
Ctrl-Left-Click $px2 $py
Write-Output ("face ctrl-toggle-off: " + (Get-Summary))

# ---- 3. 矩形選択(可視のみ) ----
Left-Drag ([int]($r.X + $r.Width * 0.22)) ([int]($r.Y + $r.Height * 0.25)) `
          ([int]($r.X + $r.Width * 0.45)) ([int]($r.Y + $r.Height * 0.60))
Write-Output ("face box: " + (Get-Summary))
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'pick-face-box.png')

# ---- 4. 節点モード ----
Select-Radio (Find-ByName $root $LblNode)
Start-Sleep -Milliseconds 300
Left-Click $px $py
Write-Output ("node click: " + (Get-Summary))
Left-Drag ([int]($r.X + $r.Width * 0.22)) ([int]($r.Y + $r.Height * 0.25)) `
          ([int]($r.X + $r.Width * 0.45)) ([int]($r.Y + $r.Height * 0.60))
Write-Output ("node box: " + (Get-Summary))
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'pick-node-box.png')

# ---- 5. パーツモード(中央=孔の中の円筒ボスを選択) ----
Select-Radio (Find-ByName $root $LblPart)
Start-Sleep -Milliseconds 300
Left-Click $cx $cy
Write-Output ("part click: " + (Get-Summary))
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'pick-part.png')

# ---- 6. 空クリックで全解除(ビューポート左上の背景) ----
Left-Click ([int]($r.X + 15)) ([int]($r.Y + $r.Height - 15))
Write-Output ("empty click: " + (Get-Summary))

# ---- 7. 選択解除ボタン(モデル操作の確認: 面を1つ選んでからクリア) ----
Select-Radio (Find-ByName $root $LblFace)
Start-Sleep -Milliseconds 300
Left-Click $px $py
Invoke-Button (Find-ByName $root $LblClear)
Start-Sleep -Milliseconds 300
Write-Output ("clear button: " + (Get-Summary))

# ---- 8. 標準視点ボタン(補間アニメーション込み) ----
Invoke-Button (Find-ByName $root $LblFront)
Start-Sleep -Milliseconds 700
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'view-front.png')
Invoke-Button (Find-ByName $root $LblTop)
Start-Sleep -Milliseconds 700
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'view-top.png')
Invoke-Button (Find-ByName $root $LblIso)
Start-Sleep -Milliseconds 700
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'view-iso.png')
Write-Output 'standard views done'

# ---- 9. ViewCube: 等角視点からキューブ中心の少し上(TOP 面)をクリック ----
$cubeCx = [int]($r.X + $r.Width - 58)
$cubeCy = [int]($r.Y + 58)
Left-Click $cubeCx ($cubeCy - 20)
Start-Sleep -Milliseconds 700
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'viewcube-top.png')
Write-Output ("viewcube click (selection unchanged): " + (Get-Summary))

# ---- 10. ライトテーマで選択ハイライト+ViewCube を確認 ----
Invoke-Button (Find-ByName $root $LblIso)
Start-Sleep -Milliseconds 500
Select-Radio (Find-ByName $root $LblFace)
Start-Sleep -Milliseconds 300
Left-Drag ([int]($r.X + $r.Width * 0.22)) ([int]($r.Y + $r.Height * 0.25)) `
          ([int]($r.X + $r.Width * 0.45)) ([int]($r.Y + $r.Height * 0.60))
$toggle = Find-ByName $root $LblTheme
$toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
Start-Sleep -Milliseconds 1500
Write-Output ("light theme: " + (Get-Summary))
Capture-Screen 0 0 1280 960 (Join-Path $outDir 'pick-light.png')

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
Write-Output 'done'
