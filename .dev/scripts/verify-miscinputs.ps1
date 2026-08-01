$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
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

function Walk-UpToPattern($element, $pattern) {
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    while ($null -ne $element -and
           -not ($element.GetSupportedPatterns() -contains $pattern)) {
        $element = $walker.GetParent($element)
    }
    return $element
}

function Click-Element($element) {
    $r = $element.Current.BoundingRectangle
    $x = [int]($r.X + $r.Width / 2)
    $y = [int]($r.Y + $r.Height / 2)
    [Win32]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 100
    [Win32]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)  # down
    [Win32]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)  # up
}

$rootDir = 'd:\home\Programs\CSharpProjects\WpfCustomUI'
$exe = Join-Path $rootDir 'WpfCustomUI.Gallery\bin\Debug\net10.0-windows\WpfCustomUI.Gallery.exe'
$outDir = Join-Path $rootDir '.dev\captures'

$p = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 4
$p.Refresh()
$h = $p.MainWindowHandle
[Win32]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, 1280, 1030, 0x0040) | Out-Null
[Win32]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 500
$root = [System.Windows.Automation.AutomationElement]::FromHandle($h)

(Find-ByName $root 'Misc Inputs & Wizard').GetCurrentPattern(
    [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 1000
Capture-Screen 0 0 1280 1030 (Join-Path $outDir 'misc-top.png')

# ---- 1. CheckComboBox: 初期サマリと SelectedItems ----
$comboInfo = Find-TextLike $root 'SelectedItems:*'
Write-Output ("initial selection: " + $comboInfo.Current.Name)

# ---- 2. ドロップダウンを開いて Sy をチェック ----
# サマリ表示はテンプレート内 TextBlock のため UIA に出ない。
# CheckComboBox の透明トグル(TogglePattern 対応 Button)をツリー順で取得する。
$toggleCond = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)),
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::IsTogglePatternAvailableProperty, $true)))
$toggles = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $toggleCond)
Write-Output ("toggle buttons found: " + $toggles.Count)
$toggle = $toggles[0]  # 1つ目 = ComponentCombo
(Get-Toggle $toggle).Toggle()
Start-Sleep -Milliseconds 600
$syItem = Find-ByName $root 'Sy'
(Get-Toggle $syItem).Toggle()
Start-Sleep -Milliseconds 300
Write-Output ("after check Sy: " + $comboInfo.Current.Name)
Capture-Screen 0 0 1280 1030 (Join-Path $outDir 'misc-dropdown.png')

# ---- 3. すべて選択 ----
$selectAll = Find-ByName $root ('(' + [string][char]0x3059 + [char]0x3079 + [char]0x3066 + [char]0x9078 + [char]0x629E + ')')  # '(すべて選択)'
$selectAll = Walk-UpToPattern $selectAll ([System.Windows.Automation.InvokePattern]::Pattern)
if ($null -eq $selectAll) {
    # CheckBox は Invoke ではなく Toggle
    $selectAll = Find-ByName $root ('(' + [string][char]0x3059 + [char]0x3079 + [char]0x3066 + [char]0x9078 + [char]0x629E + ')')
    $selectAll = Walk-UpToPattern $selectAll ([System.Windows.Automation.TogglePattern]::Pattern)
}
(Get-Toggle $selectAll).Toggle()
Start-Sleep -Milliseconds 400
Write-Output ("after select-all: " + $comboInfo.Current.Name)
(Get-Toggle $toggle).Toggle()  # 閉じる
Start-Sleep -Milliseconds 300

# ---- 4. MatrixBox: (0,1) を編集 → 対称ミラー確認 ----
$editCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Edit)
$cell01 = $null
foreach ($ed in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCond)) {
    try {
        $v = $ed.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
        if ($v -eq '80') { $cell01 = $ed; break }  # 行優先で最初の 80 = (0,1)
    } catch { }
}
$cell01.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('95')
$comboInfoParent = Walk-UpToPattern $comboInfo ([System.Windows.Automation.SelectionItemPattern]::Pattern)  # フォーカス移動用ダミー
$cell01.SetFocus() | Out-Null
Start-Sleep -Milliseconds 200
[System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
Start-Sleep -Milliseconds 500
$matrixInfo = Find-TextLike $root 'Values =*'
$mirrorCount = ([regex]::Matches($matrixInfo.Current.Name, '95')).Count
Write-Output ("matrix text: " + ($matrixInfo.Current.Name -replace "`r`n", ' / '))
Write-Output ("symmetric mirror (95 x2): " + ($mirrorCount -ge 2))

# ---- 5. KeyGestureBox: クリックしてフォーカス → Ctrl+Shift+F6 ----
# 表示テキストはテンプレート内のため、実コントロールである Clear ボタンの左側をクリックする
$clearForPos = Find-ByName $root 'Clear'
$r = $clearForPos.Current.BoundingRectangle
[Win32]::SetCursorPos([int]($r.X - 60), [int]($r.Y + $r.Height / 2)) | Out-Null
Start-Sleep -Milliseconds 100
[Win32]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
[Win32]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait('^+{F6}')
Start-Sleep -Milliseconds 400
$gestureInfo = Find-TextLike $root 'Gesture:*'
Write-Output ("after capture: " + $gestureInfo.Current.Name)
$clearBtn = Find-ByName $root 'Clear'
$clearBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 300
Write-Output ("after clear: " + $gestureInfo.Current.Name)

# ---- 6. Wizard: 次へ → CanGoNext 無効 → チェック → 次へ → 完了 ----
function Find-Button($scope, $name) {
    $c = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)))
    return $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

$nextName = [string][char]0x6B21 + [char]0x3078          # '次へ'
$backName = [string][char]0x623B + [char]0x308B          # '戻る'
$finishName = [string][char]0x5B8C + [char]0x4E86        # '完了'

$backBtn = Find-Button $root $backName
$nextBtn = Find-Button $root $nextName
Write-Output ("step1: back enabled=" + $backBtn.Current.IsEnabled + " next enabled=" + $nextBtn.Current.IsEnabled)
$nextBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 500
$nextBtn = Find-Button $root $nextName
Write-Output ("step2: next enabled (expect False)=" + $nextBtn.Current.IsEnabled)
Capture-Screen 0 0 1280 1030 (Join-Path $outDir 'misc-wizard-step2.png')

$condition = Find-TextLike $root ([string][char]0x5883 + [char]0x754C + '*')  # '境界条件...'
$conditionBox = Walk-UpToPattern $condition ([System.Windows.Automation.TogglePattern]::Pattern)
(Get-Toggle $conditionBox).Toggle()
Start-Sleep -Milliseconds 400
$nextBtn = Find-Button $root $nextName
Write-Output ("step2 after check: next enabled=" + $nextBtn.Current.IsEnabled)
$nextBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 500
Capture-Screen 0 0 1280 1030 (Join-Path $outDir 'misc-wizard-step3.png')

$finishBtn = Find-Button $root $finishName
$finishBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 400
$wizardInfo = Find-TextLike $root 'Finished*'
Write-Output ("finished event: " + ($null -ne $wizardInfo))

# ---- 7. ModelTree: F2 インライン名前変更 ----
(Find-ByName $root 'ModelTree').GetCurrentPattern(
    [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 1000

$row = Find-ByName $root 'Bracket-01'
$row = Walk-UpToPattern $row ([System.Windows.Automation.SelectionItemPattern]::Pattern)
$row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Click-Element $row   # フォーカスを行に移す
Start-Sleep -Milliseconds 400
[System.Windows.Forms.SendKeys]::SendWait('{F2}')
Start-Sleep -Milliseconds 500
Capture-Screen 0 0 1280 1030 (Join-Path $outDir 'misc-rename-editing.png')
[System.Windows.Forms.SendKeys]::SendWait('Bracket-99{ENTER}')
Start-Sleep -Milliseconds 500
$renamed = Find-ByName $root 'Bracket-99'
Write-Output ("renamed to Bracket-99: " + ($null -ne $renamed))
Capture-Screen 0 0 1280 1030 (Join-Path $outDir 'misc-renamed.png')

# Esc キャンセルの確認
[System.Windows.Forms.SendKeys]::SendWait('{F2}')
Start-Sleep -Milliseconds 400
[System.Windows.Forms.SendKeys]::SendWait('ZZZ{ESC}')
Start-Sleep -Milliseconds 400
$stillOld = Find-ByName $root 'Bracket-99'
$zzz = Find-ByName $root 'ZZZ'
Write-Output ("esc cancels (still Bracket-99): " + (($null -ne $stillOld) -and ($null -eq $zzz)))

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
Write-Output 'done'
