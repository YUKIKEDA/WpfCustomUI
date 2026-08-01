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

(Find-ByName $root 'More Controls').GetCurrentPattern(
    [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 1000
Capture-Screen 0 0 1280 940 (Join-Path $outDir 'more-top.png')

# ---- 1. InfoBar: アクション実行 → 閉じる → 再表示 ----
Invoke-Element (Find-ByName $root ([char]0x518D + [char]0x8A66 + [char]0x884C))  # '再試行'
Start-Sleep -Milliseconds 300
$status = Find-ById $root 'InfoBarStatus'
Write-Output ("action result: " + $status.Current.Name)

# エラー InfoBar の閉じるボタン = 「再試行」ボタンと同じ行にある Close ボタン
$retry = Find-ByName $root ([char]0x518D + [char]0x8A66 + [char]0x884C)
$retryTop = $retry.Current.BoundingRectangle.Top
$close = $null
foreach ($b in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, 'Close')))) {
    if ([Math]::Abs($b.Current.BoundingRectangle.Top - $retryTop) -lt 40) { $close = $b; break }
}
# InfoBar の表示状態は ActionContent の「再試行」ボタン(実コンテンツで UIA に出る)で判定する
# (テンプレート内の TextBlock は UIA コントロールビューに公開されない)
$retryName = [char]0x518D + [char]0x8A66 + [char]0x884C
if ($null -eq $close) { Write-Output 'FAIL: close button not found' }
else {
    Invoke-Element $close
    Start-Sleep -Milliseconds 400
    Write-Output ("closed event: " + $status.Current.Name)
    $r1 = Find-ByName $root $retryName
    Write-Output ("error bar visible after close: " + ($null -ne $r1 -and -not $r1.Current.IsOffscreen))
    Capture-Screen 0 0 1280 940 (Join-Path $outDir 'more-infobar-closed.png')

    Invoke-Element (Find-ById $root 'ReopenButton')
    Start-Sleep -Milliseconds 600
    $r2 = Find-ByName $root $retryName
    Write-Output ("error bar visible after reopen: " + ($null -ne $r2 -and -not $r2.Current.IsOffscreen))
    Capture-Screen 0 0 1280 940 (Join-Path $outDir 'more-infobar-reopened.png')
}

# ---- 2. ToggleSwitch: トグルして状態表示が追従すること ----
$sw = Find-ById $root 'AutoSaveSwitch'
$tp = $sw.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
Write-Output ("toggle before: " + $tp.Current.ToggleState)
$tp.Toggle()
Start-Sleep -Milliseconds 300
Write-Output ("toggle after: " + $tp.Current.ToggleState)
$hint = Find-TextLike $root '*IsChecked = False*'
Write-Output ("toggle hint found: " + ($null -ne $hint))
Capture-Screen 0 0 1280 940 (Join-Path $outDir 'more-toggled.png')

# ---- 3. TreeView: 折りたたみノードを展開して子が現れること ----
$shaft = Find-TextLike $root 'Part:*'
$tree = $null
foreach ($ti in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::TreeItem)))) {
    if ($ti.Current.Name -like '*Part:*' + [char]0x30B7 + '*') { $tree = $ti; break }
}
if ($null -eq $tree) { Write-Output 'FAIL: shaft tree item not found' }
else {
    $ec = $tree.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    Write-Output ("tree state before: " + $ec.Current.ExpandCollapseState)
    $ec.Expand()
    Start-Sleep -Milliseconds 400
    Write-Output ("tree state after: " + $ec.Current.ExpandCollapseState)
    $mesh = Find-TextLike $tree '*8,102*'
    Write-Output ("expanded child found: " + ($null -ne $mesh))
    Capture-Screen 0 0 1280 940 (Join-Path $outDir 'more-tree-expanded.png')
}

# ---- 4. 下半分へスクロール(ListView / PasswordBox / RichTextBox / Hyperlink) ----
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
Capture-Screen 0 0 1280 940 (Join-Path $outDir 'more-bottom.png')

# ---- 5. ListView の行選択(セルのテキストから親の ListItem を辿る) ----
$peek = Find-TextLike $root 'PEEK'
if ($null -eq $peek) { Write-Output 'FAIL: listview cell not found' }
else {
    # GridView 表示の行は UIA では DataItem として公開される
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $row = $walker.GetParent($peek)
    while ($null -ne $row -and
           $row.Current.ControlType -ne [System.Windows.Automation.ControlType]::ListItem -and
           $row.Current.ControlType -ne [System.Windows.Automation.ControlType]::DataItem) {
        $row = $walker.GetParent($row)
    }
    if ($null -eq $row) { Write-Output 'FAIL: listview row not found' }
    else {
        $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 300
        $sel = $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        Write-Output ("listview row selected: " + $sel.Current.IsSelected)
    }
}

# ---- 6. Hyperlink クリック ----
$link = $null
foreach ($hl in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Hyperlink)))) {
    if ($hl.Current.Name -like '*' + [char]0x30D8 + [char]0x30EB + [char]0x30D7 + '*') { $link = $hl; break }
}
if ($null -eq $link) { Write-Output 'FAIL: hyperlink not found' }
else {
    Invoke-Element $link
    Start-Sleep -Milliseconds 300
    $ls = Find-ById $root 'LinkStatus'
    Write-Output ("hyperlink result: " + $ls.Current.Name)
}
Capture-Screen 0 0 1280 940 (Join-Path $outDir 'more-bottom-after.png')

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
Write-Output 'done'
