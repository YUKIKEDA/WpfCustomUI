$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class Win32 {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lparam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    delegate bool EnumProc(IntPtr h, IntPtr l);

    // 指定プロセスの可視トップレベルウィンドウの hwnd を、タイトル接頭辞で検索する
    public static IntPtr FindWindowOfProcess(uint targetPid, string titlePrefix) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid == targetPid && IsWindowVisible(h)) {
                var sb = new StringBuilder(256);
                GetWindowText(h, sb, 256);
                if (sb.ToString().StartsWith(titlePrefix)) { found = h; return false; }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static int CountVisibleWindows(uint targetPid) {
        int count = 0;
        EnumWindows((h, l) => {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid == targetPid && IsWindowVisible(h)) count++;
            return true;
        }, IntPtr.Zero);
        return count;
    }
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

function Find-NameLike($scope, $pattern, $controlType) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $controlType)
    foreach ($el in $scope.FindAll([System.Windows.Automation.TreeScope]::Descendants, $c)) {
        if ($el.Current.Name -like $pattern) { return $el }
    }
    return $null
}

# ---- 日本語文字列([char] 連結でエンコーディング問題を回避) ----
$sLayout  = [string][char]0x30EC + [char]0x30A4 + [char]0x30A2 + [char]0x30A6 + [char]0x30C8   # レイアウト
$sSave    = $sLayout + [char]0x3092 + [char]0x4FDD + [char]0x5B58                              # レイアウトを保存
$sRestore = $sLayout + [char]0x3092 + [char]0x5FA9 + [char]0x5143                              # レイアウトを復元
$sSaved   = $sSave + [char]0x3057 + [char]0x307E + [char]0x3057 + [char]0x305F                 # レイアウトを保存しました
$sRestored= $sRestore + [char]0x3057 + [char]0x307E + [char]0x3057 + [char]0x305F              # レイアウトを復元しました
$sProps   = [string][char]0x30D7 + [char]0x30ED + [char]0x30D1 + [char]0x30C6 + [char]0x30A3   # プロパティ

$rootDir = 'd:\home\Programs\CSharpProjects\WpfCustomUI'
$exe = Join-Path $rootDir 'WpfCustomUI.Gallery\bin\Debug\net10.0-windows\WpfCustomUI.Gallery.exe'
$outDir = Join-Path $rootDir '.dev\captures'

$p = Start-Process -FilePath $exe -ArgumentList '--dockshell' -PassThru
Start-Sleep -Seconds 6

# ---- シェルウィンドウ(CAE Shell ...)を Win32 列挙で探す(UIA のデスクトップ列挙は取りこぼすことがある) ----
$desktop = [System.Windows.Automation.AutomationElement]::RootElement
$h = [Win32]::FindWindowOfProcess([uint32]$p.Id, 'CAE Shell')
if ($h -eq [IntPtr]::Zero) { Stop-Process -Id $p.Id -ErrorAction SilentlyContinue; throw 'shell window not found' }
$shell = [System.Windows.Automation.AutomationElement]::FromHandle($h)
Write-Output ("shell window: " + $shell.Current.Name)
[Win32]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, 1280, 850, 0x0040) | Out-Null
[Win32]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 800

Capture-Screen 0 0 1280 850 (Join-Path $outDir 'dock-initial.png')

# ---- 1. レイアウトを保存 ----
$menuLayout = Find-NameLike $shell ($sLayout + '*') ([System.Windows.Automation.ControlType]::MenuItem)
$menuLayout.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
Start-Sleep -Milliseconds 600
$itemSave = Find-NameLike $desktop $sSave ([System.Windows.Automation.ControlType]::MenuItem)
$itemSave.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 600
$status = Find-NameLike $shell $sSaved ([System.Windows.Automation.ControlType]::Text)
Write-Output ("layout saved status: " + ($null -ne $status))

# ---- 2. プロパティのキャプション(ペイン上部)をドラッグしてフローティング化 ----
# 同名要素が複数ある(キャプション/下部タブ)ため、最も上にあるもの = キャプションを選ぶ
$propsCaption = $null
foreach ($el in $shell.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $sProps)))) {
    if ($null -eq $propsCaption -or
        $el.Current.BoundingRectangle.Y -lt $propsCaption.Current.BoundingRectangle.Y) {
        $propsCaption = $el
    }
}
if ($null -eq $propsCaption) { Stop-Process -Id $p.Id -ErrorAction SilentlyContinue; throw 'props caption not found' }
$r = $propsCaption.Current.BoundingRectangle
$sx = [int]($r.X + $r.Width / 2); $sy = [int]($r.Y + $r.Height / 2)
Write-Output ("drag start: $sx,$sy (type=" + $propsCaption.Current.ControlType.ProgrammaticName + ")")

[Win32]::SetCursorPos($sx, $sy) | Out-Null
Start-Sleep -Milliseconds 300
[Win32]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)  # down
Start-Sleep -Milliseconds 300
# 少しずつ中央へ移動(ドラッグ検知とガイド表示のため)
$tx = 560; $ty = 420
for ($i = 1; $i -le 30; $i++) {
    $mx = [int]($sx + ($tx - $sx) * $i / 30)
    $my = [int]($sy + ($ty - $sy) * $i / 30)
    [Win32]::SetCursorPos($mx, $my) | Out-Null
    [Win32]::mouse_event(0x0001, 0, 0, 0, [UIntPtr]::Zero)  # MOVE(相対0)でイベントを確実に発生させる
    Start-Sleep -Milliseconds 40
}
Start-Sleep -Milliseconds 800
Capture-Screen 0 0 1280 850 (Join-Path $outDir 'dock-guides.png')  # ドッキングガイド表示中
[Win32]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)  # up → ガイド外なのでフローティング
Start-Sleep -Milliseconds 1200
Capture-Screen 0 0 1280 850 (Join-Path $outDir 'dock-floating.png')

# フローティングウィンドウ(同プロセスの別トップレベルウィンドウ)が生えたか確認
$floatCount = [Win32]::CountVisibleWindows([uint32]$p.Id)
Write-Output ("visible top-level windows of process (expect >= 3): " + $floatCount)

# ---- 3. レイアウトを復元(プロパティがドックに戻るはず) ----
[Win32]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 300
$menuLayout = Find-NameLike $shell ($sLayout + '*') ([System.Windows.Automation.ControlType]::MenuItem)
$menuLayout.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
Start-Sleep -Milliseconds 600
$itemRestore = Find-NameLike $desktop $sRestore ([System.Windows.Automation.ControlType]::MenuItem)
$itemRestore.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 1000
$status = Find-NameLike $shell $sRestored ([System.Windows.Automation.ControlType]::Text)
Write-Output ("layout restored status: " + ($null -ne $status))
Capture-Screen 0 0 1280 850 (Join-Path $outDir 'dock-restored.png')

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
Write-Output 'done'
