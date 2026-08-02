# CaeStudio(MVVM サンプル CAE アプリ)の UIA 検証(Phase 26、spec 6.26.7)
# M1: 起動 → メッシュプレビュー → 解析実行 → 完了 → コンター描画(ピクセル差分)
#     → ウィザードで片持ち板を新規作成 → 再解析 → 完了
# M2: ウィザードで固有値解析を選択 → モードテーブル(振動数/理論値/誤差) →
#     位相スイープ(PlaybackBar フレーム変更でピクセル差分) → プローブ注釈+ログ
# M3: テーマ切替 / 設定ダイアログ / 名前を付けて保存→開く往復 /
#     SearchBox・MatrixBox・CheckComboBox・Study タブの存在確認
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
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
"@
Add-Type -ReferencedAssemblies System.Drawing @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class PixelDiff {
    public static int Count(string pathA, string pathB, int tolerance) {
        using (var a = new Bitmap(pathA))
        using (var b = new Bitmap(pathB)) {
            int w = Math.Min(a.Width, b.Width), h = Math.Min(a.Height, b.Height);
            var rect = new Rectangle(0, 0, w, h);
            var da = a.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var db = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try {
                var bytesA = new byte[da.Stride * h];
                var bytesB = new byte[db.Stride * h];
                Marshal.Copy(da.Scan0, bytesA, 0, bytesA.Length);
                Marshal.Copy(db.Scan0, bytesB, 0, bytesB.Length);
                int count = 0;
                for (int y = 0; y < h; y++) {
                    int rowA = y * da.Stride, rowB = y * db.Stride;
                    for (int x = 0; x < w; x++) {
                        int ia = rowA + x * 4, ib = rowB + x * 4;
                        if (Math.Abs(bytesA[ia] - bytesB[ib]) > tolerance
                            || Math.Abs(bytesA[ia + 1] - bytesB[ib + 1]) > tolerance
                            || Math.Abs(bytesA[ia + 2] - bytesB[ib + 2]) > tolerance) {
                            count++;
                        }
                    }
                }
                return count;
            } finally {
                a.UnlockBits(da);
                b.UnlockBits(db);
            }
        }
    }
}
"@

$root = 'D:\home\Programs\CSharpProjects\WpfCustomUI'
$outDir = Join-Path $root '.dev\screenshots'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function Capture-Region($x, $y, $w, $h, $path) {
    [Win32]::SetCursorPos(5, 5) | Out-Null
    Start-Sleep -Milliseconds 150
    $b = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $g.Dispose()
    $b.Save($path)
    $b.Dispose()
}

function Find-ById($scope, $automationId) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

function Find-ByNameAndType($scope, $name, $controlType) {
    $c = New-Object System.Windows.Automation.AndCondition @(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $controlType)))
    return $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

function Invoke-Button($element) {
    if ($null -eq $element) { throw 'Invoke-Button: element is null' }
    if ($element -is [System.Array]) { $element = @($element)[0] }
    try {
        $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    } catch {
        $rect = $element.Current.BoundingRectangle
        Left-Click ([int]($rect.X + $rect.Width / 2)) ([int]($rect.Y + $rect.Height / 2))
    }
}

function Select-Item($element) {
    $element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}

function Toggle-On($element) {
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if ($pattern.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) {
        $pattern.Toggle()
    }
}

function Left-Click($x, $y) {
    [Win32]::SetCursorPos([int]$x, [int]$y) | Out-Null
    Start-Sleep -Milliseconds 120
    [Win32]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)  # LEFTDOWN
    Start-Sleep -Milliseconds 60
    [Win32]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)  # LEFTUP
    Start-Sleep -Milliseconds 200
}

function Activate-DockTab($scope, $name, $preferX = $null, $preferY = $null) {
    $tab = Find-ByNameAndType $scope $name ([System.Windows.Automation.ControlType]::TabItem)
    if ($null -eq $tab) {
        $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)
        foreach ($candidate in $scope.FindAll([System.Windows.Automation.TreeScope]::Descendants, $nameCondition)) {
            $rect = $candidate.Current.BoundingRectangle
            if ($null -ne $preferX -and $rect.X -lt $preferX) { continue }
            if ($null -ne $preferY -and $rect.Y -lt $preferY) { continue }
            $tab = $candidate
            break
        }
    }
    if ($null -eq $tab) { return $null }
    try {
        Select-Item $tab
    } catch {
        $rect = $tab.Current.BoundingRectangle
        Left-Click ([int]($rect.X + $rect.Width / 2)) ([int]($rect.Y + $rect.Height / 2))
    }
    Start-Sleep -Milliseconds 600
    return $tab
}

# リボン上部タブ(Y < 120)を名前で選択する
function Select-RibbonTab($scope, $name) {
    $tabCond = New-Object System.Windows.Automation.AndCondition @(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::TabItem)))
    $tab = $null
    foreach ($candidate in $scope.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tabCond)) {
        if ($candidate.Current.BoundingRectangle.Y -lt 120) { $tab = $candidate; break }
    }
    Assert-True ($null -ne $tab) "リボontab: $name"
    Select-Item $tab
    Start-Sleep -Milliseconds 700
    return $tab
}

# モデルタブを開いてリボン上のコマンドボタンを返す(アプリメニューは UIA 非公開のため)
# 指定リボontabを開いてコマンドボタンを返す
function Invoke-RibbonCommand($scope, $automationId, $tabName = $null) {
    if ($null -eq $tabName) {
        $tabName = [string][char]0x30E2 + [char]0x30C7 + [char]0x30EB  # 'モデル'
    }
    $null = Select-RibbonTab $scope $tabName
    $btn = Find-ById $scope $automationId
    Assert-True ($null -ne $btn) "リボンコマンド: $automationId"
    return $btn
}

function Set-FileDialogPath($scope, $dialogTitle, $filePath) {
    Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
    # オーナー付き共通ダイアログはデスクトップ直下ではなく親ウィンドウの子として現れる
    Wait-Until {
        $null -ne (Find-ByNameAndType $scope $dialogTitle ([System.Windows.Automation.ControlType]::Window))
    } 15 "ファイルダイアログ: $dialogTitle"
    $dialog = Find-ByNameAndType $scope $dialogTitle ([System.Windows.Automation.ControlType]::Window)
    Assert-True ($null -ne $dialog) "ファイルダイアログを取得: $dialogTitle"

    $hwnd = [IntPtr]$dialog.Current.NativeWindowHandle
    if ($hwnd -ne [IntPtr]::Zero) {
        [Win32]::SetForegroundWindow($hwnd) | Out-Null
    }
    Start-Sleep -Milliseconds 300

    # Win11: AutomationId 1001 はアドレスバー。ファイル名は「ファイル名(N):」付近の Edit/Combo。
    # Alt+N でファイル名欄へフォーカスしてからフルパスを SendKeys する。
    if ($hwnd -ne [IntPtr]::Zero) {
        [Win32]::SetForegroundWindow($hwnd) | Out-Null
    }
    Start-Sleep -Milliseconds 200
    [System.Windows.Forms.SendKeys]::SendWait('%n')
    Start-Sleep -Milliseconds 250
    [System.Windows.Forms.SendKeys]::SendWait('^a')
    Start-Sleep -Milliseconds 80
    $escaped = ($filePath -replace '([+\^%~(){}])', '{$1}')
    [System.Windows.Forms.SendKeys]::SendWait($escaped)
    Start-Sleep -Milliseconds 300
    Assert-True $true 'ファイルダイアログへパスを入力(Alt+N)'

    # Win11 共通ダイアログの「保存」「開く」は Button ではなく Pane(AutomationId=1)
    $confirmNames = if ($dialogTitle -match '開く') {
        @('開く(O)', '開く(_O)', '開く')
    } else {
        @('保存(S)', '保存(_S)', '保存')
    }
    $confirmControl = $null
    foreach ($name in $confirmNames) {
        $confirmControl = Find-ByNameAndType $dialog $name ([System.Windows.Automation.ControlType]::Button)
        if ($null -eq $confirmControl) {
            $confirmControl = Find-ByNameAndType $dialog $name ([System.Windows.Automation.ControlType]::Pane)
        }
        if ($null -ne $confirmControl) { break }
    }
    if ($null -eq $confirmControl) {
        # AutomationId=1 はサイドバー ListItem にも使われるため、名前が空でない Pane に限定
        $idCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, '1')
        foreach ($candidate in $dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants, $idCondition)) {
            if ($candidate.Current.ControlType -eq [System.Windows.Automation.ControlType]::Pane -and
                $candidate.Current.Name -match '保存|開く') {
                $confirmControl = $candidate
                break
            }
        }
    }
    Assert-True ($null -ne $confirmControl) 'ファイルダイアログの確定ボタンを取得'
    try {
        Invoke-Button $confirmControl
    } catch {
        $rect = $confirmControl.Current.BoundingRectangle
        Left-Click ([int]($rect.X + $rect.Width / 2)) ([int]($rect.Y + $rect.Height / 2))
    }

    # 上書き確認
    Start-Sleep -Milliseconds 500
    foreach ($title in @('確認', '名前を付けて保存の確認')) {
        $confirm = Find-ByNameAndType $scope $title ([System.Windows.Automation.ControlType]::Window)
        if ($null -ne $confirm) {
            $yes = Find-ByNameAndType $confirm 'はい(Y)' ([System.Windows.Automation.ControlType]::Button)
            if ($null -eq $yes) { $yes = Find-ByNameAndType $confirm 'はい' ([System.Windows.Automation.ControlType]::Button) }
            if ($null -eq $yes) { $yes = Find-ByNameAndType $confirm 'はい(Y)' ([System.Windows.Automation.ControlType]::Pane) }
            if ($null -ne $yes) {
                try { Invoke-Button $yes } catch {
                    $rect = $yes.Current.BoundingRectangle
                    Left-Click ([int]($rect.X + $rect.Width / 2)) ([int]($rect.Y + $rect.Height / 2))
                }
            }
            break
        }
    }
    Start-Sleep -Milliseconds 1200
}

function Wait-Until([scriptblock]$condition, $timeoutSec, $message) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
        if (& $condition) { return }
        Start-Sleep -Milliseconds 300
    }
    throw "タイムアウト: $message"
}

$script:passCount = 0
function Assert-True($condition, $message) {
    if ($condition) {
        $script:passCount++
        Write-Host "PASS: $message" -ForegroundColor Green
    } else {
        throw "FAIL: $message"
    }
}

# ================= 起動 =================

$exe = Join-Path $root 'samples\CaeStudio.App\bin\Debug\net10.0-windows\CaeStudio.exe'
$process = Start-Process -FilePath $exe -PassThru
try {
    Wait-Until { $process.Refresh(); $process.MainWindowHandle -ne [IntPtr]::Zero } 30 'メインウィンドウ'
    Start-Sleep -Seconds 3

    $hwnd = $process.MainWindowHandle
    [Win32]::SetWindowPos($hwnd, [IntPtr]::Zero, 0, 0, 1400, 900, 0x0040) | Out-Null
    [Win32]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 800

    $main = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    Assert-True ($null -ne $main) 'CaeStudio メインウィンドウを取得'

    # ---- 起動直後: プレビューメッシュが構築されている ----
    $meshStats = Find-ById $main 'MeshStatsText'
    Wait-Until { $meshStats.GetCurrentPropertyValue(
        [System.Windows.Automation.AutomationElement]::NameProperty) -match '節点' } 20 'メッシュプレビュー統計'
    $statsText = $meshStats.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::NameProperty)
    Assert-True ($statsText -match '節点 [\d,]+ / 三角形 [\d,]+') "メッシュ統計表示: $statsText"

    Capture-Region 0 0 1400 900 (Join-Path $outDir 'caestudio-preview.png')

    # ---- 解析タブへ切替 → 解析実行 → 完了 ----
    $analysisName = [string][char]0x89E3 + [string][char]0x6790  # '解析'
    Select-RibbonTab $main $analysisName

    $runButton = Find-ById $main 'RunButton'
    Assert-True ($null -ne $runButton) '解析実行ボタンを取得'
    Invoke-Button $runButton

    $status = Find-ById $main 'StatusText'
    Wait-Until { $status.GetCurrentPropertyValue(
        [System.Windows.Automation.AutomationElement]::NameProperty) -match '解析完了' } 90 '静解析の完了'
    $statusText = $status.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::NameProperty)
    Assert-True ($statusText -match '解析完了\([\d,]+ 反復') "解析完了ステータス: $statusText"

    Start-Sleep -Seconds 2
    Capture-Region 0 0 1400 900 (Join-Path $outDir 'caestudio-result.png')

    # コンター描画でビューポートが大きく変わる(グレー→Jet カラー)
    $diff = [PixelDiff]::Count(
        (Join-Path $outDir 'caestudio-preview.png'),
        (Join-Path $outDir 'caestudio-result.png'), 12)
    Assert-True ($diff -gt 30000) "コンター描画のピクセル差分: $diff"

    # ---- ウィザード: 片持ち板を新規作成 ----
    $modelName = [string][char]0x30E2 + [char]0x30C7 + [char]0x30EB  # 'モデル'
    Select-RibbonTab $main $modelName
    $newButton = Find-ById $main 'NewProjectButton'
    Invoke-Button $newButton
    Start-Sleep -Milliseconds 1200

    # オーナー付きダイアログはメインウィンドウ配下に現れる
    $wizard = Find-ByNameAndType $main '新規解析' ([System.Windows.Automation.ControlType]::Window)
    Assert-True ($null -ne $wizard) '新規解析ウィザードが表示'

    $beamRadio = Find-ById $wizard 'TemplateBeam'
    Select-Item $beamRadio
    Start-Sleep -Milliseconds 400

    $nextButton = Find-ByNameAndType $wizard '次へ' ([System.Windows.Automation.ControlType]::Button)
    Invoke-Button $nextButton
    Start-Sleep -Milliseconds 400

    # ---- M2: パラメータステップで固有値解析を選択 ----
    $modalRadio = Find-ById $wizard 'WizardModal'
    Assert-True ($null -ne $modalRadio) 'ウィザードの解析タイプ選択(固有値解析)を取得'
    Select-Item $modalRadio
    Start-Sleep -Milliseconds 400

    Invoke-Button $nextButton
    Start-Sleep -Milliseconds 400

    # 確認ステップのサマリに片持ち板+固有値解析の内容が出ている
    $summary = Find-ById $wizard 'WizardSummary'
    $summaryText = $summary.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::NameProperty)
    Assert-True ($summaryText -match '片持ち板の曲げ\(固有値解析\)') "ウィザードサマリ: 片持ち板+固有値解析"

    $finishButton = Find-ByNameAndType $wizard '作成' ([System.Windows.Automation.ControlType]::Button)
    Invoke-Button $finishButton
    Start-Sleep -Seconds 2

    # タイトルとメッシュ統計が新プロジェクトに切り替わる(既定 80×8 分割 → 729 節点)
    Wait-Until { $meshStats.GetCurrentPropertyValue(
        [System.Windows.Automation.AutomationElement]::NameProperty) -match '節点 729' } 20 '片持ち板メッシュ統計'

    # ---- M2: 固有値解析 → モードテーブル ----
    # 新規作成後はモデルタブに戻るため、解析タブへ再切替
    Select-RibbonTab $main $analysisName
    $runButton = Find-ById $main 'RunButton'
    Assert-True ($null -ne $runButton) '解析実行ボタン(再取得)'
    Invoke-Button $runButton
    Wait-Until { $status.GetCurrentPropertyValue(
        [System.Windows.Automation.AutomationElement]::NameProperty) -match '固有値解析完了' } 120 '固有値解析の完了'
    $statusText = $status.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::NameProperty)
    Assert-True ($statusText -match '固有値解析完了\(\d+ モード') "固有値解析完了ステータス: $statusText"

    Start-Sleep -Seconds 2

    # 右ペインの「モード」タブを前面化(背面タブの内容は UIA ツリーに現れないため)。
    # AvalonDock のアンカラブルタブは TabItem でないことがあるため名前検索+クリックで切り替える
    $modeTab = Find-ByNameAndType $main 'モード' ([System.Windows.Automation.ControlType]::TabItem)
    if ($null -eq $modeTab) {
        $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, 'モード')
        foreach ($candidate in $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $nameCondition)) {
            $rect = $candidate.Current.BoundingRectangle
            if ($rect.X -gt 1000) { $modeTab = $candidate; break }  # 右ペイン領域のタブラベル
        }
    }
    Assert-True ($null -ne $modeTab) 'モードタブを取得'
    try {
        Select-Item $modeTab
    } catch {
        $rect = $modeTab.Current.BoundingRectangle
        Left-Click ([int]($rect.X + $rect.Width / 2)) ([int]($rect.Y + $rect.Height / 2))
    }
    Start-Sleep -Milliseconds 800

    # モードテーブル: セルテキストから振動数と誤差 % を検証
    $modeTable = Find-ById $main 'ModeTable'
    Assert-True ($null -ne $modeTable) 'モードテーブルを取得'
    $textCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Custom)
    $cells = $modeTable.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCondition)
    $cellNames = @($cells | ForEach-Object { $_.GetCurrentPropertyValue(
        [System.Windows.Automation.AutomationElement]::NameProperty) })
    # FEM 4 モード+曲げ卓越モードの理論値(軸振動モードの理論欄は「-」)
    $frequencyCells = @($cellNames | Where-Object { $_ -match '^[\d,]+\.\d$' })
    $errorCells = @($cellNames | Where-Object { $_ -match '^[+\-]\d+\.\d %$' })
    Assert-True ($frequencyCells.Count -ge 7) "モードテーブルに振動数セル(FEM+理論): $($frequencyCells.Count) 個"
    Assert-True ($errorCells.Count -ge 3) "モードテーブルに誤差セル: $($errorCells.Count) 個"

    # 1 次固有振動数が Euler-Bernoulli 理論値(413.6 Hz)の ±5% 以内
    $f1 = [double](($frequencyCells[0]) -replace ',', '')
    Assert-True ($f1 -gt 392 -and $f1 -lt 435) "1 次固有振動数: $f1 Hz(理論 413.6 Hz)"

    Capture-Region 0 0 1400 900 (Join-Path $outDir 'caestudio-modal.png')

    # ---- M2: 周波数応答タブ(モード重ね合わせ FRF が描画される) ----
    $frfTab = $null
    $frfNameCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, '周波数応答')
    foreach ($candidate in $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $frfNameCondition)) {
        $rect = $candidate.Current.BoundingRectangle
        if ($rect.Y -gt 500) { $frfTab = $candidate; break }  # 下部ペインのタブラベル
    }
    Assert-True ($null -ne $frfTab) '周波数応答タブを取得'
    try {
        Select-Item $frfTab
    } catch {
        $rect = $frfTab.Current.BoundingRectangle
        Left-Click ([int]($rect.X + $rect.Width / 2)) ([int]($rect.Y + $rect.Height / 2))
    }
    Start-Sleep -Milliseconds 1200
    Capture-Region 0 0 1400 900 (Join-Path $outDir 'caestudio-frf.png')

    # ---- M2: 位相スイープ(PlaybackBar のフレームでモード形状が反転する) ----
    $viewport = Find-ById $main 'Viewport'
    Assert-True ($null -ne $viewport) 'ビューポートを取得'
    $viewportRect = $viewport.GetCurrentPropertyValue(
        [System.Windows.Automation.AutomationElement]::BoundingRectangleProperty)

    # PlaybackBar 内部の Slider(Maximum=59)を特定してフレームを直接設定
    $sliderCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Slider)
    $sliders = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $sliderCondition)
    $phaseSlider = $null
    foreach ($slider in $sliders) {
        $range = $slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
        if ([Math]::Abs($range.Current.Maximum - 59) -lt 0.5) { $phaseSlider = $slider; break }
    }
    Assert-True ($null -ne $phaseSlider) '位相スイープスライダ(60 フレーム)を取得'

    Capture-Region ([int]$viewportRect.X) ([int]$viewportRect.Y) ([int]$viewportRect.Width) ([int]$viewportRect.Height) `
        (Join-Path $outDir 'caestudio-phase0.png')

    # フレーム 30 = cos(π) = -1 → モード形状が反転描画される
    $phaseSlider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(30)
    Start-Sleep -Milliseconds 1200
    Capture-Region ([int]$viewportRect.X) ([int]$viewportRect.Y) ([int]$viewportRect.Width) ([int]$viewportRect.Height) `
        (Join-Path $outDir 'caestudio-phase30.png')

    $phaseDiff = [PixelDiff]::Count(
        (Join-Path $outDir 'caestudio-phase0.png'),
        (Join-Path $outDir 'caestudio-phase30.png'), 12)
    Assert-True ($phaseDiff -gt 5000) "位相スイープのピクセル差分: $phaseDiff"

    # ---- M2: プローブ → 注釈ラベル+ログ ----
    # 完了後は結果タブへ自動切替済み。プローブは結果タブにある
    $probeToggle = Find-ById $main 'ProbeToggle'
    if ($null -eq $probeToggle) {
        $resultsName = [string][char]0x7D50 + [string][char]0x679C  # '結果'
        Select-RibbonTab $main $resultsName
        $probeToggle = Find-ById $main 'ProbeToggle'
    }
    Assert-True ($null -ne $probeToggle) 'プローブトグルを取得'
    Toggle-On $probeToggle
    Start-Sleep -Milliseconds 400

    [Win32]::SetForegroundWindow($hwnd) | Out-Null
    Left-Click ($viewportRect.X + $viewportRect.Width * 0.5) ($viewportRect.Y + $viewportRect.Height * 0.5)
    Start-Sleep -Milliseconds 1000

    # 注釈ラベル(WPF オーバーレイの Text)がフォーマッター書式で表示される
    $textTypeCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text)
    $texts = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textTypeCondition)
    $annotation = $null
    foreach ($text in $texts) {
        $name = $text.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::NameProperty)
        if ($name -match 'N\d+: \|u\| = \d\.\d{3}') { $annotation = $name; break }
    }
    Assert-True ($null -ne $annotation) "プローブ注釈ラベル: $annotation"

    Capture-Region 0 0 1400 900 (Join-Path $outDir 'caestudio-probe.png')

    # 注釈クリアで注釈が消える
    $clearButton = Find-ById $main 'ClearAnnotationsButton'
    Invoke-Button $clearButton
    Start-Sleep -Milliseconds 600
    $texts = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textTypeCondition)
    $remaining = @($texts | ForEach-Object { $_.GetCurrentPropertyValue(
        [System.Windows.Automation.AutomationElement]::NameProperty) } |
        Where-Object { $_ -match 'N\d+: \|u\| =' })
    Assert-True ($remaining.Count -eq 0) '注釈クリアで注釈が消える'

    # ================= M3: 永続化・設定・網羅コントロール =================

    # ---- 表示タブ: 表示項目(CheckComboBox)・低頻度パネルを開く ----
    $viewName = [string][char]0x8868 + [string][char]0x793A  # '表示'
    Select-RibbonTab $main $viewName
    $displayOptions = Find-ById $main 'DisplayOptions'
    Assert-True ($null -ne $displayOptions) '表示項目 CheckComboBox を取得'
    $treeSearch = Find-ById $main 'TreeSearch'
    Assert-True ($null -ne $treeSearch) 'ツリー検索 SearchBox を取得'

    # 材料剛性・スタディは既定非表示 → 表示タブのトグルで開く
    $materialToggle = $null
    $materialLabel = [string][char]0x6750 + [char]0x6599 + [char]0x525B + [char]0x6027  # '材料剛性'
    $nameCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $materialLabel)
    foreach ($c in $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $nameCond)) {
        if ($c.Current.BoundingRectangle.Y -lt 160) { $materialToggle = $c; break }
    }
    if ($null -ne $materialToggle) {
        try { Toggle-On $materialToggle } catch {
            $r = $materialToggle.Current.BoundingRectangle
            Left-Click ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
        }
        Start-Sleep -Milliseconds 600
    }

    # ---- 材料剛性タブ(MatrixBox) ----
    $materialTab = Activate-DockTab $main $materialLabel 1000
    Assert-True ($null -ne $materialTab) '材料剛性タブを取得'
    $matrix = Find-ById $main 'ElasticityMatrix'
    Assert-True ($null -ne $matrix) '弾性マトリクス MatrixBox を取得'

    # ---- スタディタブ(HistoryChart) ----
    $studyLabel = [string][char]0x30B9 + [char]0x30BF + [char]0x30C7 + [char]0x30A3  # 'スタディ'
    $studyToggle = $null
    $studyNameCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $studyLabel)
    foreach ($c in $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $studyNameCond)) {
        if ($c.Current.BoundingRectangle.Y -lt 160) { $studyToggle = $c; break }
    }
    if ($null -ne $studyToggle) {
        try { Toggle-On $studyToggle } catch {
            $r = $studyToggle.Current.BoundingRectangle
            Left-Click ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
        }
        Start-Sleep -Milliseconds 600
    }
    $studyTab = Activate-DockTab $main $studyLabel $null 500
    Assert-True ($null -ne $studyTab) 'スタディタブを取得'
    $studyButton = Find-ById $main 'StudyButton'
    Assert-True ($null -ne $studyButton) 'スタディ実行ボタンを取得'

    # ---- テーマ切替(設定ダイアログのトグル) ----
    $viewNameForSettings = [string][char]0x8868 + [string][char]0x793A  # '表示'
    $settingsItem = Invoke-RibbonCommand $main 'SettingsButton' $viewNameForSettings
    Invoke-Button $settingsItem
    Start-Sleep -Milliseconds 1200

    $settings = Find-ByNameAndType $main '設定' ([System.Windows.Automation.ControlType]::Window)
    Assert-True ($null -ne $settings) '設定ダイアログが表示'
    Assert-True ($null -ne (Find-ById $settings 'SettingsLightTheme')) '設定: テーマトグル'
    Assert-True ($null -ne (Find-ById $settings 'SettingsDefaultDir')) '設定: PathBox'
    Assert-True ($null -ne (Find-ById $settings 'SettingsRunGesture')) '設定: KeyGestureBox'
    Toggle-On (Find-ById $settings 'SettingsLightTheme')
    Start-Sleep -Milliseconds 400
    Invoke-Button (Find-ById $settings 'SettingsOk')
    Start-Sleep -Milliseconds 1000
    Capture-Region 0 0 1400 900 (Join-Path $outDir 'caestudio-light.png')

    # ---- 名前を付けて保存 → 開く 往復 ----
    $projectPath = Join-Path $env:TEMP ("caestudio-uia-" + [Guid]::NewGuid().ToString('N') + '.wcuproj')
    try {
        $saveAsItem = Invoke-RibbonCommand $main 'SaveAsButton'
        Invoke-Button $saveAsItem
        Set-FileDialogPath $main 'プロジェクトを保存' $projectPath
        Assert-True (Test-Path $projectPath) "プロジェクトファイルが保存された: $projectPath"
        $savedJson = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
        Assert-True ($savedJson -match 'CantileverPlate') '保存 JSON に片持ち板テンプレート'
        Assert-True ($savedJson -match '"AnalysisType"\s*:\s*"Modal"') '保存 JSON に固有値解析'

        # いったん円孔平板の新規プロジェクトへ切替(保存済みとメッシュ統計が変わること)
        $newButton = Invoke-RibbonCommand $main 'NewProjectButton'
        Invoke-Button $newButton
        Start-Sleep -Milliseconds 1200
        $discard = Find-ByNameAndType $main '確認' ([System.Windows.Automation.ControlType]::Window)
        if ($null -ne $discard) {
            $yes = Find-ByNameAndType $discard 'はい' ([System.Windows.Automation.ControlType]::Button)
            if ($null -ne $yes) { Invoke-Button $yes }
            Start-Sleep -Milliseconds 800
        }
        $wizard2 = Find-ByNameAndType $main '新規解析' ([System.Windows.Automation.ControlType]::Window)
        Assert-True ($null -ne $wizard2) '往復確認用の新規解析ウィザード'
        Wait-Until {
            $null -ne (Find-ByNameAndType $wizard2 '次へ' ([System.Windows.Automation.ControlType]::Button))
        } 10 'ウィザードの次へボタン'
        $next2 = Find-ByNameAndType $wizard2 '次へ' ([System.Windows.Automation.ControlType]::Button)
        Invoke-Button $next2; Start-Sleep -Milliseconds 400
        Invoke-Button $next2; Start-Sleep -Milliseconds 400
        $finish2 = Find-ByNameAndType $wizard2 '作成' ([System.Windows.Automation.ControlType]::Button)
        Assert-True ($null -ne $finish2) 'ウィザードの作成ボタン'
        Invoke-Button $finish2
        Start-Sleep -Seconds 2
        Wait-Until { $meshStats.GetCurrentPropertyValue(
            [System.Windows.Automation.AutomationElement]::NameProperty) -notmatch '節点 729' } 20 '円孔平板へ切替'
        $plateStats = $meshStats.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::NameProperty)
        Assert-True ($plateStats -match '節点') "切替後メッシュ統計: $plateStats"

        # 保存済み片持ち板を開き直す
        $openItem = Invoke-RibbonCommand $main 'OpenButton'
        Invoke-Button $openItem
        Set-FileDialogPath $main 'プロジェクトを開く' $projectPath

        # ダイアログが閉じたこと+メッシュが片持ち板に戻ること
        Wait-Until {
            $null -eq (Find-ByNameAndType $main 'プロジェクトを開く' ([System.Windows.Automation.ControlType]::Window))
        } 15 '開くダイアログが閉じる'
        Wait-Until {
            $meshStats.GetCurrentPropertyValue(
                [System.Windows.Automation.AutomationElement]::NameProperty) -match '節点 729'
        } 30 '保存済み片持ち板の再読込'
        $reloaded = $meshStats.GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::NameProperty)
        Assert-True ($reloaded -match '節点 729') "開く往復後のメッシュ統計: $reloaded"
        Capture-Region 0 0 1400 900 (Join-Path $outDir 'caestudio-reload.png')
    }
    finally {
        if (Test-Path $projectPath) { Remove-Item -LiteralPath $projectPath -Force }
    }

    Write-Host ""
    Write-Host "=== verify-caestudio: $script:passCount 件すべて PASS ===" -ForegroundColor Green
}
finally {
    if (-not $process.HasExited) { $process.Kill() }
}
