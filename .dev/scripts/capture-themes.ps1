# 全ページ x 両テーマ(Dark / Light)のスクリーンショットを撮る(spec 6.15.4)。
# 同一プロセス内で Dark 全ページ -> テーマトグルで Light へ切替 -> Light 全ページの順に
# 撮影するため、実行時テーマ切替(DynamicResource / ThemeChanged 追従)の検証を兼ねる。
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

$rootDir = 'd:\home\Programs\CSharpProjects\WpfCustomUI'
$exe = Join-Path $rootDir 'WpfCustomUI.Gallery\bin\Debug\net10.0-windows\WpfCustomUI.Gallery.exe'
$outDir = Join-Path $rootDir '.dev\captures\themes'
New-Item -ItemType Directory -Force -Path (Join-Path $outDir 'dark') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $outDir 'light') | Out-Null

$p = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 4
$p.Refresh()
$h = $p.MainWindowHandle
[Win32]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, 1280, 960, 0x0040) | Out-Null
[Win32]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 500
$root = [System.Windows.Automation.AutomationElement]::FromHandle($h)

# ナビの ListBox 項目(ページ一覧)を列挙する
$listItemCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::ListItem)
$navItems = @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $listItemCond))
Write-Output ("nav items: " + $navItems.Count)

function Capture-AllPages($themeName) {
    $index = 0
    foreach ($item in $navItems) {
        $name = $item.Current.Name
        $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        # Charts など初期描画が重いページのために少し長めに待つ
        Start-Sleep -Milliseconds 1500
        $safe = ($name -replace '[^A-Za-z0-9]+', '-').Trim('-').ToLower()
        if ($safe -eq '') { $safe = 'page' }
        $file = Join-Path $outDir ("{0}\{1:D2}-{2}.png" -f $themeName, $index, $safe)
        Capture-Screen 0 0 1280 960 $file
        $index++
    }
    Write-Output ("captured " + $index + " pages (" + $themeName + ")")
}

# ---- 1. ダークテーマで全ページ ----
Capture-AllPages 'dark'

# ---- 2. 実行時切替: ナビ下部のトグルでライトテーマへ ----
$toggleName = [string][char]0x30E9 + [char]0x30A4 + [char]0x30C8 + [char]0x30C6 + [char]0x30FC + [char]0x30DE  # 'ライトテーマ'
$toggleCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, $toggleName)
$toggle = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $toggleCond)
if ($null -eq $toggle) { Write-Output 'FAIL: theme toggle not found'; Stop-Process -Id $p.Id; exit 1 }
$toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
Start-Sleep -Milliseconds 1500
Write-Output 'theme switched to light at runtime'

# ---- 3. ライトテーマで全ページ ----
Capture-AllPages 'light'

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
Write-Output 'done'
