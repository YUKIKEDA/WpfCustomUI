# Phase 25: 既存 UIA スクリプト全回帰ランナー
$scripts = @(
    'verify-docking',
    'verify-viewport',
    'verify-viewport-picking',
    'verify-viewport-deformation',
    'verify-viewport-section',
    'verify-viewport-probe',
    'verify-viewport-glyphs',
    'verify-viewport-benchmark',
    'verify-charts',
    'verify-charts-accent',
    'verify-charts-wheel',
    'verify-postprocessing',
    'verify-pickers',
    'verify-miscinputs',
    'verify-morecontrols',
    'verify-datagrid-rowdetails3'
)
$rootDir = 'd:\home\Programs\CSharpProjects\WpfCustomUI'
$log = Join-Path $rootDir '.dev\captures\regression-p25.log'
Set-Content -Path $log -Value ('start ' + (Get-Date))
foreach ($s in $scripts) {
    Add-Content -Path $log -Value ('===== ' + $s + ' =====')
    $out = powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $rootDir ('.dev\scripts\' + $s + '.ps1')) 2>&1 | Out-String
    Add-Content -Path $log -Value $out
    Add-Content -Path $log -Value ('exit: ' + $LASTEXITCODE)
}
Add-Content -Path $log -Value ('end ' + (Get-Date))
Write-Output 'REGRESSION-COMPLETE'
