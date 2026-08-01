$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
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

function Get-Toggle($element) {
    return $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
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

(Find-ByName $root 'Post-processing').GetCurrentPattern(
    [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 1000
Capture-Screen 0 0 1280 940 (Join-Path $outDir 'post-top.png')

# ---- 1. PlaybackBar: 再生でスライダーが進むこと ----
# PlaybackBar 本体はカスタム Control で UIA ツリーに出ないため、
# スライダー(Maximum=59 のもの)を直接探し、ボタンは Name で最初の有効なものを使う
$sliderCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Slider)
$slider = $null
foreach ($s in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $sliderCond)) {
    $rv = $s.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
    if ($rv.Current.Maximum -eq 59) { $slider = $s; break }
}
$range = $slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
Write-Output ("slider max: " + $range.Current.Maximum)
$playbackBar = $root

$play = Find-ByName $playbackBar 'Play'
(Get-Toggle $play).Toggle()   # 再生開始
Start-Sleep -Milliseconds 800
$v1 = $range.Current.Value
Start-Sleep -Milliseconds 800
$v2 = $range.Current.Value
Write-Output ("playing advances: " + ($v2 -ne $v1) + " (v1=$v1 v2=$v2)")
Capture-Screen 0 0 1280 940 (Join-Path $outDir 'post-playing.png')

$pause = Find-ByName $playbackBar 'Pause'
(Get-Toggle $pause).Toggle()  # 一時停止
Start-Sleep -Milliseconds 400
$v3 = $range.Current.Value
Start-Sleep -Milliseconds 500
Write-Output ("paused holds: " + ($range.Current.Value -eq $v3))

# ---- 2. ステップ送り/戻し ----
$before = $range.Current.Value
Invoke-Element (Find-ByName $playbackBar 'Step forward')
Start-Sleep -Milliseconds 300
Write-Output ("step forward: " + $before + " -> " + $range.Current.Value)
Invoke-Element (Find-ByName $playbackBar 'Step back')
Start-Sleep -Milliseconds 300
Write-Output ("step back: " + $range.Current.Value)

# ---- 3. ループ OFF で末尾到達 → 自動停止 ----
$loop = Find-ByName $playbackBar 'Loop'
(Get-Toggle $loop).Toggle()   # ループ解除
$range.SetValue(55)           # 末尾近くへ
Start-Sleep -Milliseconds 200
$play = Find-ByName $playbackBar 'Play'
(Get-Toggle $play).Toggle()   # 再生
Start-Sleep -Milliseconds 1500
$playState = (Get-Toggle (Find-ByName $playbackBar 'Play')).Current.ToggleState
Write-Output ("auto-stop at end: state=$playState value=" + $range.Current.Value)

# ---- 4. ColorScaleEditor: カラーマップ変更 → 凡例追従(目視) ----
# エディタ本体もカスタム Control で UIA に出ないため、SpeedCombo 以外の ComboBox を使う
$editor = $root
$comboCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::ComboBox)
$combo = $null
foreach ($cb in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $comboCond)) {
    if ($cb.Current.AutomationId -ne 'SpeedCombo') { $combo = $cb; break }
}
$combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
Start-Sleep -Milliseconds 500
$viridis = Find-ByName $root 'Viridis'
# TextBlock がヒットすることがあるため、SelectionItemPattern を持つ親まで遡る
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
while ($null -ne $viridis -and
       -not ($viridis.GetSupportedPatterns() -contains [System.Windows.Automation.SelectionItemPattern]::Pattern)) {
    $viridis = $walker.GetParent($viridis)
}
if ($null -eq $viridis) { Write-Output 'FAIL: Viridis item not found' }
else {
    $viridis.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 500
    Write-Output 'colormap changed to Viridis'
}
Capture-Screen 0 0 1280 940 (Join-Path $outDir 'post-viridis.png')

# ---- 5. 離散レベルを OFF → 連続グラデーション(目視) ----
$discrete = Find-ByName $editor ([char]0x96E2 + [char]0x6563 + [char]0x30EC + [char]0x30D9 + [char]0x30EB)  # '離散レベル'
(Get-Toggle $discrete).Toggle()
Start-Sleep -Milliseconds 500
Capture-Screen 0 0 1280 940 (Join-Path $outDir 'post-continuous.png')
Write-Output 'discrete toggled off'

# ---- 6. 最大値の編集 → 凡例の目盛が変わること ----
$editCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Edit)
$maxBox = $null
foreach ($ed in $editor.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCond)) {
    try {
        $v = $ed.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
        if ($v -eq '350') { $maxBox = $ed; break }
    } catch { }
}
if ($null -eq $maxBox) { Write-Output 'FAIL: max numericbox not found' }
else {
    $maxBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('500')
    # フォーカスを移して確定させる
    $combo.SetFocus()
    Start-Sleep -Milliseconds 600
    $tick = Find-TextLike $root '500'
    Write-Output ("legend tick shows 500: " + ($null -ne $tick))
    Capture-Screen 0 0 1280 940 (Join-Path $outDir 'post-max500.png')
}

# ---- 7. 詳細 Expander: クランプ解除で ColorPicker が有効になる ----
$adv = Find-ByName $root ([string][char]0x8A73 + [char]0x7D30)  # '詳細'
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
while ($null -ne $adv -and
       -not ($adv.GetSupportedPatterns() -contains [System.Windows.Automation.ExpandCollapsePattern]::Pattern)) {
    $adv = $walker.GetParent($adv)
}
if ($null -eq $adv) { Write-Output 'FAIL: advanced expander not found' }
else {
    $adv.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 500
    # 2つ目の「クランプ」(上限超過)を解除 → 上限色ピッカーが有効化される
    $clampCond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            ([string][char]0x30AF + [char]0x30E9 + [char]0x30F3 + [char]0x30D7))),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::CheckBox)))
    $clamps = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $clampCond)
    Write-Output ("clamp checkboxes: " + $clamps.Count)
    (Get-Toggle $clamps[1]).Toggle()
    Start-Sleep -Milliseconds 500
    Capture-Screen 0 0 1280 940 (Join-Path $outDir 'post-advanced.png')
    Write-Output 'clamp above unchecked'
}

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
Write-Output 'done'
