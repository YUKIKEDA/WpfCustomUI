# Investigate: Jobs panel missing + analysis after new project
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

function Find-ById($s, $id) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $s.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

function Select-RibbonTab($s, $name) {
    $and = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::TabItem)))
    foreach ($c in $s.FindAll([System.Windows.Automation.TreeScope]::Descendants, $and)) {
        if ($c.Current.BoundingRectangle.Y -lt 140) {
            $c.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            return $true
        }
    }
    return $false
}

$exe = 'd:\home\Programs\CSharpProjects\WpfCustomUI\samples\CaeStudio.App\bin\Debug\net10.0-windows\CaeStudio.exe'
$p = Start-Process $exe -PassThru
try {
    $deadline = (Get-Date).AddSeconds(40)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 300
        $p.Refresh()
        if ($p.HasExited) { throw "startup crash $($p.ExitCode)" }
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) { break }
    }
    Start-Sleep 3
    $main = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)

    $jobTable = Find-ById $main 'JobTable'
    Write-Output "JobTable at startup: present=$($null -ne $jobTable)"

    $analysis = [string][char]0x89E3 + [char]0x6790
    Select-RibbonTab $main $analysis | Out-Null
    Start-Sleep -Milliseconds 600
    $jobsBtn = Find-ById $main 'JobsPanelButton'
    if ($null -eq $jobsBtn) { throw 'JobsPanelButton missing' }
    $jobsBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep 1
    $jobTable2 = Find-ById $main 'JobTable'
    Write-Output "JobTable after ShowJobs: present=$($null -ne $jobTable2) offscreen=$(if($jobTable2){$jobTable2.Current.IsOffscreen}else{'n/a'})"

    # Run on default project first
    $run = Find-ById $main 'RunButton'
    Write-Output "RunButton enabled=$($run.Current.IsEnabled)"
    $status = Find-ById $main 'StatusText'
    $before = [string]$status.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::NameProperty)
    Write-Output "status before run: $before"
    $run.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep 3
    $p.Refresh()
    if ($p.HasExited) { Write-Output "CRASH after run exit=$($p.ExitCode)"; exit 2 }
    $after = [string]$status.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::NameProperty)
    Write-Output "status after run: $after"

    $results = [string][char]0x7D50 + [char]0x679C
    $and = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $results)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::TabItem)))
    foreach ($c in $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $and)) {
        if ($c.Current.BoundingRectangle.Y -lt 140) {
            $sel = $c.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            Write-Output "Results tab enabled=$($c.Current.IsEnabled) selected=$($sel.Current.IsSelected)"
        }
    }
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
}
