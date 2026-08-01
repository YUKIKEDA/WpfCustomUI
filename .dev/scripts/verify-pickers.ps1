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
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
}
"@

$MOUSEEVENTF_LEFTDOWN = 0x0002
$MOUSEEVENTF_LEFTUP = 0x0004

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

function Find-ById($scope, $id) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
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

function Invoke-Element($element) {
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Click-At($x, $y) {
    [Win32]::SetCursorPos([int]$x, [int]$y) | Out-Null
    Start-Sleep -Milliseconds 120
    [Win32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [Win32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 250
}

function Find-ProcessMenu($processId) {
    $pidCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    foreach ($w in [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children, $pidCond)) {
        $menuCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Menu)
        $menu = $w.FindFirst([System.Windows.Automation.TreeScope]::Subtree, $menuCond)
        if ($null -ne $menu) { return $menu }
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
[Win32]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, 1280, 940, 0x0040) | Out-Null
[Win32]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 500
$root = [System.Windows.Automation.AutomationElement]::FromHandle($h)

(Find-ByName $root 'Pickers & Range').GetCurrentPattern(
    [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 1000
Capture-Screen 0 0 1280 940 (Join-Path $outDir 'pickers-top.png')

$dropResult = Find-ById $root 'DropDownResult'

# ---- 1. DropDownButton: メニューを開いて項目を選ぶ ----
Invoke-Element (Find-ByName $root ([char]0x30D3 + [char]0x30E5 + [char]0x30FC))  # 'ビュー'
Start-Sleep -Milliseconds 600
$menu = Find-ProcessMenu $p.Id
if ($null -eq $menu) { Write-Output 'FAIL: dropdown menu not found'; Stop-Process -Id $p.Id; exit 1 }
Capture-Screen 0 0 1280 940 (Join-Path $outDir 'pickers-dropdown-open.png')
$iso = $null
foreach ($mi in $menu.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::MenuItem)))) {
    if ($mi.Current.Name -like '*Isometric*') { $iso = $mi; break }
}
Invoke-Element $iso
Start-Sleep -Milliseconds 500
Write-Output ("dropdown result: " + $dropResult.Current.Name)

# ---- 2. SplitButton: 本体クリック → Command、矢印 → メニュー ----
$solve = Find-ById $root 'SolveButton'
Invoke-Element $solve
Start-Sleep -Milliseconds 500
Write-Output ("split main result: " + $dropResult.Current.Name)

$btnCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Button)
$arrow = $solve.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
if ($null -eq $arrow) { Write-Output 'FAIL: split arrow not found' }
else {
    Invoke-Element $arrow
    Start-Sleep -Milliseconds 600
    $menu = Find-ProcessMenu $p.Id
    if ($null -eq $menu) { Write-Output 'FAIL: split menu not open' }
    else {
        Capture-Screen 0 0 1280 940 (Join-Path $outDir 'pickers-split-open.png')
        $batch = $null
        foreach ($mi in $menu.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::MenuItem)))) {
            if ($mi.Current.Name -like '*...*') { $batch = $mi; break }
        }
        Invoke-Element $batch
        Start-Sleep -Milliseconds 500
        Write-Output ("split menu result: " + $dropResult.Current.Name)
    }
}

# ---- 3. RangeSlider: 中央バーをドラッグして両端が同時に動くこと ----
$before = Find-TextLike $root 'Min = *'
Write-Output ("range before: " + $before.Current.Name)
$rect = $before.Current.BoundingRectangle
$sliderY = $rect.Top - 18
$grabX = $rect.Left + 0.45 * 280
[Win32]::SetCursorPos([int]$grabX, [int]$sliderY) | Out-Null
Start-Sleep -Milliseconds 150
[Win32]::mouse_event($MOUSEEVENTF_LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 150
for ($i = 1; $i -le 10; $i++) {
    [Win32]::SetCursorPos([int]($grabX + $i * 5), [int]$sliderY) | Out-Null
    Start-Sleep -Milliseconds 30
}
[Win32]::mouse_event($MOUSEEVENTF_LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 400
$after = Find-TextLike $root 'Min = *'
Write-Output ("range after drag: " + $after.Current.Name)
Capture-Screen 0 0 1280 940 (Join-Path $outDir 'pickers-range-dragged.png')

# ---- 4. ColorPicker: 下までスクロールしてポップアップ+パレット適用 ----
$scrollCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty, $true)
foreach ($s in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $scrollCond)) {
    $sp = $s.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    if ($sp.Current.VerticallyScrollable) {
        $sp.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 100)
        break
    }
}
Start-Sleep -Milliseconds 600
Capture-Screen 0 0 1280 940 (Join-Path $outDir 'pickers-bottom.png')

# ドロップダウン型ピッカー(「ドロップダウン型」ラベルの直下)をクリック
$label = Find-TextLike $root ([regex]::Unescape('\u30C9\u30ED\u30C3\u30D7\u30C0\u30A6\u30F3\u578B*'))
$lrect = $label.Current.BoundingRectangle
Click-At ($lrect.Left + 30) ($lrect.Bottom + 14)
Start-Sleep -Milliseconds 600
Capture-Screen 0 0 1280 940 (Join-Path $outDir 'pickers-colorpicker-open.png')

# ポップアップ内の Hex ボックス(#CC0078D7)を探す(WPF の Popup はウィンドウのサブツリーに載る)
$editCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Edit)
$popupEdit = $null
foreach ($edit in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCond)) {
    try {
        $v = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
        if ($v -eq '#CC0078D7') { $popupEdit = $edit; break }
    } catch { }
}
if ($null -eq $popupEdit) { Write-Output 'FAIL: colorpicker popup hex not found' }
else {
    Write-Output 'popup hex: #CC0078D7'
    $editRect = $popupEdit.Current.BoundingRectangle

    # ポップアップ内の白スウォッチ(Name=#FFFFFFFF、Hex ボックスの下にある)をクリック
    $white = $null
    foreach ($b in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)) {
        if ($b.Current.Name -eq '#FFFFFFFF') {
            $brect = $b.Current.BoundingRectangle
            if ([Math]::Abs($brect.Left - $editRect.Left) -lt 250 -and
                $brect.Top -gt $editRect.Top -and ($brect.Top - $editRect.Bottom) -lt 80) {
                $white = $b; break
            }
        }
    }
    if ($null -eq $white) { Write-Output 'FAIL: white swatch not found' }
    else {
        Invoke-Element $white
        Start-Sleep -Milliseconds 500
        $hex2 = $popupEdit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
        Write-Output ("hex after palette: " + $hex2)
        $sel = Find-TextLike $root 'SelectedColor = *'
        Write-Output ("bound color: " + $sel.Current.Name)
        Capture-Screen 0 0 1280 940 (Join-Path $outDir 'pickers-colorpicker-palette.png')
    }
}

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
Write-Output 'done'
