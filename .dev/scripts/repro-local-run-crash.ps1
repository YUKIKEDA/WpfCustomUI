# Reproduce RunCommand crash (geometry rebuild / ComPtr dispose)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

function Find-ById($scope, $id) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

function Select-RibbonTab($scope, $name) {
    $nameCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $typeCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::TabItem)
    $and = New-Object System.Windows.Automation.AndCondition($nameCond, $typeCond)
    foreach ($c in $scope.FindAll([System.Windows.Automation.TreeScope]::Descendants, $and)) {
        $r = $c.Current.BoundingRectangle
        if ($r.Y -lt 140 -and $r.Width -gt 0) {
            $c.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            return $true
        }
    }
    return $false
}

function Invoke-Button($el) {
    try {
        $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    } catch {
        $r = $el.Current.BoundingRectangle
        Add-Type @"
using System; using System.Runtime.InteropServices;
public static class M {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
}
"@
        [M]::SetCursorPos([int]($r.X + $r.Width/2), [int]($r.Y + $r.Height/3)) | Out-Null
        [M]::mouse_event(0x2,0,0,0,[IntPtr]::Zero); [M]::mouse_event(0x4,0,0,0,[IntPtr]::Zero)
    }
}

$rootDir = 'd:\home\Programs\CSharpProjects\WpfCustomUI'
$exe = Join-Path $rootDir 'samples\CaeStudio.App\bin\Debug\net10.0-windows\CaeStudio.exe'
$p = Start-Process -FilePath $exe -PassThru
try {
    $deadline = (Get-Date).AddSeconds(40)
    $main = $null
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 400
        $p.Refresh()
        if ($p.HasExited) { throw "exited during startup code=$($p.ExitCode)" }
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) {
            $main = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
            if ($null -ne $main) { break }
        }
    }
    if ($null -eq $main) { throw 'main window not found' }
    Start-Sleep -Seconds 3

    $analysisName = [string]([char]0x89E3) + [string]([char]0x6790)
    if (-not (Select-RibbonTab $main $analysisName)) {
        Write-Output 'WARN: analysis tab select failed; trying RunButton anyway'
    }
    Start-Sleep -Milliseconds 800

    $run = Find-ById $main 'RunButton'
    if ($null -eq $run) { throw 'RunButton not found' }
    Write-Output 'invoking RunButton'
    Invoke-Button $run

    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 1
        $p.Refresh()
        if ($p.HasExited) {
            Write-Output ("CRASH at t=${i}s exit=" + $p.ExitCode)
            # dump latest AV from event log
            Get-WinEvent -FilterHashtable @{LogName='Application'; Id=1026; StartTime=(Get-Date).AddMinutes(-2)} -MaxEvents 1 -ErrorAction SilentlyContinue |
              ForEach-Object { $_.Message.Substring(0, [Math]::Min(800, $_.Message.Length)) }
            exit 2
        }
        $status = Find-ById $main 'StatusText'
        if ($null -ne $status) {
            $t = [string]$status.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::NameProperty)
            if ($t -match [char]0x5B8C + [char]0x4E86) { # 完了
                Write-Output "OK status=$t at t=${i}s"
                # run again to stress rebuild/dispose
                Write-Output 'invoking RunButton again (stress)'
                Invoke-Button $run
                Start-Sleep -Seconds 8
                $p.Refresh()
                if ($p.HasExited) {
                    Write-Output ("CRASH on 2nd run exit=" + $p.ExitCode)
                    exit 2
                }
                Write-Output 'still alive after 2nd run'
                exit 0
            }
        }
    }
    Write-Output 'still alive after 30s (no completion status seen)'
    exit 0
}
finally {
    if (-not $p.HasExited) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }
}
