# Release ビルドの手動ベンチ(spec 6.22.7 / 6.23.7)。100万〜2億の
# 構築時間・描画時間・回転アニメ FPS(LOD)・メモリを計測して出力する(結果は spec に記録)。
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
}
"@

function Find-ByName($scope, $name) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    return $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}
function Find-ById($scope, $automationId) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}
function Toggle-Switch($element) {
    $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
}
function Select-ComboItem($combo, $itemName) {
    $expand = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    Start-Sleep -Milliseconds 300
    $item = Find-ByName $combo $itemName
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 200
    $expand.Collapse()
}
function Wait-ForStats($statsElement, $expectedTriangles, $timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $text = $statsElement.Current.Name
        if ($text -like "*$expectedTriangles*") { return $text }
        Start-Sleep -Milliseconds 500
    }
    return $statsElement.Current.Name
}

$Lbl1M = '100' + [string][char]0x4E07
$Lbl10M = '1,000' + [string][char]0x4E07
$Lbl25M = '2,500' + [string][char]0x4E07
$Lbl50M = '5,000' + [string][char]0x4E07
$Lbl100M = '1' + [string][char]0x5104   # 1億
$Lbl200M = '2' + [string][char]0x5104   # 2億

$rootDir = 'd:\home\Programs\CSharpProjects\WpfCustomUI'
$exe = Join-Path $rootDir 'WpfCustomUI.Gallery\bin\Release\net10.0-windows\WpfCustomUI.Gallery.exe'

$p = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 4
$p.Refresh()
$h = $p.MainWindowHandle
[Win32]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, 1280, 960, 0x0040) | Out-Null
[Win32]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 500
$root = [System.Windows.Automation.AutomationElement]::FromHandle($h)

(Find-ByName $root '3D Benchmark').GetCurrentPattern(
    [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 3000

$statsText = Find-ById $root 'StatsText'
$fpsText = Find-ById $root 'FpsText'
$sizeCombo = Find-ById $root 'SizeCombo'
$rotateToggle = Find-ById $root 'RotateToggle'

$cases = @(
    @{ Label = $Lbl1M;  Expected = '999,698';    Timeout = 60;  Skip = $true },  # 既定選択済み
    @{ Label = $Lbl10M; Expected = '9,999,392';  Timeout = 120; Skip = $false },
    @{ Label = $Lbl25M; Expected = '25,006,592'; Timeout = 240; Skip = $false },
    @{ Label = $Lbl50M; Expected = '50,000,000'; Timeout = 480; Skip = $false },
    @{ Label = $Lbl100M; Expected = '99,998,082'; Timeout = 900; Skip = $false },
    @{ Label = $Lbl200M; Expected = '200,000,000'; Timeout = 1800; Skip = $false }
)

foreach ($case in $cases) {
    if (-not $case.Skip) {
        Select-ComboItem $sizeCombo $case.Label
    }

    $stats = Wait-ForStats $statsText $case.Expected $case.Timeout
    Write-Output ("=== {0} ===" -f $case.Expected)
    Write-Output ("stats: {0}" -f $stats)

    # 回転アニメを 6 秒回して実測 FPS を読む
    Toggle-Switch $rotateToggle
    Start-Sleep -Seconds 6
    Write-Output ("fps:   {0}" -f $fpsText.Current.Name)
    Toggle-Switch $rotateToggle
    Start-Sleep -Milliseconds 500

    # プロセスのメモリ使用量も参考記録
    $p.Refresh()
    Write-Output ("mem:   WorkingSet={0:N0} MB" -f ($p.WorkingSet64 / 1MB))
}

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
Write-Output 'bench done'
