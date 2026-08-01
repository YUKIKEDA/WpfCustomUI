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

function Capture-Region($x, $y, $w, $h, $path) {
    [Win32]::SetCursorPos(5, 5) | Out-Null
    Start-Sleep -Milliseconds 80
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

function Find-ById($scope, $automationId) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    return $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

function Find-ByClass($scope, $className) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ClassNameProperty, $className)
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

function Click-At($x, $y) {
    [Win32]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 120
    [Win32]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [Win32]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 300
}

$script:failures = 0
function Assert-Contains($label, $text, $substring) {
    if ($text -like "*$substring*") {
        Write-Output ("PASS {0}: '{1}' found" -f $label, $substring)
    } else {
        Write-Output ("FAIL {0}: '{1}' not in '{2}'" -f $label, $substring, $text)
        $script:failures++
    }
}

function Assert-NotContains($label, $text, $substring) {
    if ($text -like "*$substring*") {
        Write-Output ("FAIL {0}: '{1}' unexpectedly in '{2}'" -f $label, $substring, $text)
        $script:failures++
    } else {
        Write-Output ("PASS {0}: '{1}' absent" -f $label, $substring)
    }
}

function Assert-Same($label, $fileA, $fileB, $maxPixels) {
    $diff = [PixelDiff]::Count($fileA, $fileB, 8)
    if ($diff -le $maxPixels) {
        Write-Output ("PASS {0}: diff={1} (<= {2})" -f $label, $diff, $maxPixels)
    } else {
        Write-Output ("FAIL {0}: diff={1} (expected <= {2})" -f $label, $diff, $maxPixels)
        $script:failures++
    }
}

# 統計テキストが構築完了を示すまで待つ(タイマー更新 500ms)
function Wait-ForStats($statsElement, $expectedTriangles, $timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $text = $statsElement.Current.Name
        if ($text -like "*$expectedTriangles*") {
            return $text
        }
        Start-Sleep -Milliseconds 500
    }
    return $statsElement.Current.Name
}

# 日本語ラベル(エンコーディング事故を避けるため char 合成)
$LblTheme = [string][char]0x30E9 + [char]0x30A4 + [char]0x30C8 + [char]0x30C6 + [char]0x30FC + [char]0x30DE  # ライトテーマ
$LblChunk = [string][char]0x30C1 + [char]0x30E3 + [char]0x30F3 + [char]0x30AF  # チャンク
$LblEdgeSkip = [string][char]0x30A8 + [char]0x30C3 + [char]0x30B8 + [char]0x30B9 + [char]0x30AD + [char]0x30C3 + [char]0x30D7  # エッジスキップ
$LblSelected = [string][char]0x9078 + [char]0x629E + [char]0x9762  # 選択面
$Lbl10M = '1,000' + [string][char]0x4E07  # 1,000万
$LblLodTri = 'LOD ' + [string][char]0x4E09 + [char]0x89D2 + [char]0x5F62  # LOD 三角形
$LblLodActive = 'LOD' + [string][char]0x63CF + [char]0x753B + [char]0x4E2D  # LOD描画中
$Lbl5M = '500' + [string][char]0x4E07  # 500万
$LblBuilding = [string][char]0x69CB + [char]0x7BC9 + [char]0x4E2D  # 構築中
$LblBuildDone = [string][char]0x69CB + [char]0x7BC9 + [char]0x5B8C + [char]0x4E86  # 構築完了

$rootDir = 'd:\home\Programs\CSharpProjects\WpfCustomUI'
$exe = Join-Path $rootDir 'WpfCustomUI.Gallery\bin\Debug\net10.0-windows\WpfCustomUI.Gallery.exe'
$outDir = Join-Path $rootDir '.dev\captures'

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

$vp = Find-ByClass $root 'WcuViewport'
if ($null -eq $vp) { Write-Output 'FAIL: WcuViewport not found'; Stop-Process -Id $p.Id; exit 1 }
$r = $vp.Current.BoundingRectangle
$cx = [int]($r.X + $r.Width / 2); $cy = [int]($r.Y + $r.Height / 2)

$statsText = Find-ById $root 'StatsText'
$pickText = Find-ById $root 'PickText'
$sizeCombo = Find-ById $root 'SizeCombo'

# ---- 1. 既定 100万(side=707 → 999,698 三角形、1 チャンク、エッジ抽出あり) ----
$stats1M = Wait-ForStats $statsText '999,698' 60
Write-Output ("stats(1M): {0}" -f $stats1M)
Assert-Contains '1M triangles' $stats1M '999,698'
Assert-Contains '1M vertices' $stats1M '501,264'
Assert-Contains '1M single chunk' $stats1M ($LblChunk + ' 1 ')
Assert-Contains '1M edges extracted' $stats1M ($LblEdgeSkip + ' 0 ')

# ---- 2. ピック整合(100万、クリック → 選択面 1 件) ----
Click-At $cx $cy
Start-Sleep -Milliseconds 800
$pick1M = $pickText.Current.Name
Write-Output ("pick(1M): {0}" -f $pick1M)
Assert-Contains '1M pick selects one face' $pick1M ($LblSelected + ' 1 ')

Capture-Region 0 0 1280 960 (Join-Path $outDir 'benchmark-1m-dark.png')

# ---- 2.5 非同期構築(Phase 24、spec 6.24.2): 完了メッセージ+構築中の旧シーン維持+連打競合 ----
$buildText = Find-ById $root 'BuildText'
Assert-Contains '1M async build completed message' $buildText.Current.Name $LblBuildDone

# 連打競合: 500万を選んだ直後に 1,000万へ切替(第1世代は世代管理で破棄される)。
# 構築中は BuildText が進捗を示し、統計(=GPU 上の旧シーン)は 100万のまま維持される
Select-ComboItem $sizeCombo $Lbl5M
Start-Sleep -Milliseconds 200
Select-ComboItem $sizeCombo $Lbl10M

$sawBuilding = $false
$oldSceneDuringBuild = $false
$pollDeadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $pollDeadline) {
    $bt = $buildText.Current.Name
    if ($bt -like ('*' + $LblBuilding + '*')) {
        $sawBuilding = $true
        if ($statsText.Current.Name -like '*999,698*') {
            $oldSceneDuringBuild = $true
        }
    }
    if ($statsText.Current.Name -like '*9,999,392*') { break }
    Start-Sleep -Milliseconds 100
}
if ($sawBuilding) { Write-Output 'PASS async build progress shown during build' }
else { Write-Output 'FAIL async build progress never shown'; $script:failures++ }
if ($oldSceneDuringBuild) { Write-Output 'PASS old scene stats kept while building' }
else { Write-Output 'FAIL old scene stats not observed during build'; $script:failures++ }

# ---- 3. 1,000万(side=2236 → 9,999,392 三角形、節点 5,004,169 > 400万 → 複数チャンク、
#         三角形数 > EdgeExtractionLimit(500万) → エッジスキップ 1) ----
# 連打競合の最終結果が最後の選択(1,000万)になっていること(spec 6.24.2 世代管理)
$stats10M = Wait-ForStats $statsText '9,999,392' 180
Assert-Contains '10M async build completed message' $buildText.Current.Name $LblBuildDone
Write-Output ("stats(10M): {0}" -f $stats10M)
Assert-Contains '10M triangles' $stats10M '9,999,392'
Assert-Contains '10M vertices' $stats10M '5,004,169'
Assert-Contains '10M edge skipped' $stats10M ($LblEdgeSkip + ' 1 ')

# チャンク数 >= 2 をアサート(数値を抜き出して比較)
if ($stats10M -match ($LblChunk + ' (\d+)')) {
    $chunks = [int]$Matches[1]
    if ($chunks -ge 2) {
        Write-Output ("PASS 10M multi-chunk: chunks={0}" -f $chunks)
    } else {
        Write-Output ("FAIL 10M multi-chunk: chunks={0} (expected >= 2)" -f $chunks)
        $script:failures++
    }
} else {
    Write-Output ("FAIL 10M multi-chunk: chunk count not found in '{0}'" -f $stats10M)
    $script:failures++
}

# ---- 4. ピック整合(1,000万、チャンク基点オフセットの検証) ----
# 真上視点にすると波面板が画面いっぱいに写り、三角形 ID(x 行順)が画面方向に単調に
# 並ぶ。3×3 の格子でクリックスキャンし、チャンク 1(ID < 250万)とチャンク 2
# (ID > 750万、チャンク境界 ≈ 799万)の両方がピックできることを検証する
(Find-ById $root 'TopViewButton').GetCurrentPattern(
    [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 500
(Find-ByName $root 'Fit').GetCurrentPattern(
    [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 800

# フィット後の板の半幅は最小辺の約 30%(バウンディング球フィットのため板対角/
# 球直径 ≈ 0.7 を掛けた値)。±22% × 最小辺 のオフセットなら確実に板の上に乗り、
# 三角形 ID は中心±75% の行に届く(チャンク境界 ≈ 799万を跨ぐ)
$off = [int]([Math]::Min($r.Width, $r.Height) * 0.22)
Capture-Region ([int]$r.X) ([int]$r.Y) ([int]$r.Width) ([int]$r.Height) (Join-Path $outDir 'benchmark-10m-topview.png')
$pickedIds = @()
foreach ($dx in @(-$off, 0, $off)) {
    foreach ($dy in @(-$off, 0, $off)) {
        Click-At ($cx + $dx) ($cy + $dy)
        Start-Sleep -Milliseconds 700
        $text = $pickText.Current.Name
        Write-Output ("  scan({0},{1}): {2}" -f $dx, $dy, $text)
        if ($text -match 'ID \[(\d+)') {
            $pickedIds += [long]$Matches[1]
        }
    }
}

Write-Output ("pick(10M) scanned ids: {0}" -f ($pickedIds -join ', '))
if ($pickedIds.Count -ge 5) {
    Write-Output ("PASS 10M pick works at scale: {0}/9 hits" -f $pickedIds.Count)
} else {
    Write-Output ("FAIL 10M pick works at scale: only {0}/9 hits" -f $pickedIds.Count)
    $script:failures++
}

$minId = ($pickedIds | Measure-Object -Minimum).Minimum
$maxId = ($pickedIds | Measure-Object -Maximum).Maximum
if ($minId -lt 2500000 -and $maxId -gt 7500000 -and $maxId -lt 9999392) {
    Write-Output ("PASS 10M pick spans both chunks: min={0} max={1}" -f $minId, $maxId)
} else {
    Write-Output ("FAIL 10M pick spans both chunks: min={0} max={1} (expected min<2.5M, 7.5M<max<9,999,392)" -f $minId, $maxId)
    $script:failures++
}

Capture-Region 0 0 1280 960 (Join-Path $outDir 'benchmark-10m-dark.png')

# ---- 5. 操作中 LOD(spec 6.23.3): 1,000万 > 閾値 500万 → LOD 構築済み ----
$statsNow = $statsText.Current.Name
Write-Output ("stats(10M with LOD): {0}" -f $statsNow)
if ($statsNow -match ($LblLodTri + ' ([\d,]+)')) {
    $lodTris = [long]($Matches[1] -replace ',', '')
    if ($lodTris -ge 200000 -and $lodTris -le 1500000) {
        Write-Output ("PASS 10M LOD built: lodTriangles={0} (~1/20 of 10M)" -f $lodTris)
    } else {
        Write-Output ("FAIL 10M LOD built: lodTriangles={0} (expected 200k..1.5M)" -f $lodTris)
        $script:failures++
    }
} else {
    Write-Output ("FAIL 10M LOD built: '{0}' not found in stats" -f $LblLodTri)
    $script:failures++
}

# 静止時は LOD 描画中でない(ピック直後 = 操作から 700ms 以上経過)
Assert-NotContains '10M idle renders full detail' $statsNow $LblLodActive

# 回転アニメ中は LOD 描画になる
Toggle-Switch (Find-ById $root 'RotateToggle')   # ON
Start-Sleep -Milliseconds 2500
$statsRotating = $statsText.Current.Name
Write-Output ("stats(rotating): {0}" -f $statsRotating)
Assert-Contains '10M rotating uses LOD' $statsRotating $LblLodActive
Toggle-Switch (Find-ById $root 'RotateToggle')   # OFF

# 停止から 300ms(復帰遅延)+ 統計更新 500ms 後にはフル描画へ戻る
Start-Sleep -Milliseconds 2000
$statsIdle = $statsText.Current.Name
Write-Output ("stats(idle after rotate): {0}" -f $statsIdle)
Assert-NotContains '10M restores full detail after idle' $statsIdle $LblLodActive

# ---- 6. 静止時フル描画の決定性: LOD 有効(静止)と LOD 無効で同一ピクセル ----
(Find-ById $root 'TopViewButton').GetCurrentPattern(
    [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 400
(Find-ByName $root 'Fit').GetCurrentPattern(
    [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 1500   # LOD 復帰遅延(300ms)より十分長く待つ
$imgLodIdle = Join-Path $outDir 'benchmark-10m-lod-idle.png'
Capture-Region ([int]$r.X) ([int]$r.Y) ([int]$r.Width) ([int]$r.Height) $imgLodIdle

# LOD 無効化(閾値 int.MaxValue → ジオメトリ再構築、LOD 三角形 0 になるまで待つ)
Toggle-Switch (Find-ById $root 'LodToggle')      # OFF
$statsNoLod = Wait-ForStats $statsText ($LblLodTri + ' 0') 180
Write-Output ("stats(LOD disabled): {0}" -f $statsNoLod)
Assert-Contains 'LOD disabled has no LOD mesh' $statsNoLod ($LblLodTri + ' 0')

(Find-ById $root 'TopViewButton').GetCurrentPattern(
    [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 400
(Find-ByName $root 'Fit').GetCurrentPattern(
    [System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 1500
$imgNoLod = Join-Path $outDir 'benchmark-10m-nolod.png'
Capture-Region ([int]$r.X) ([int]$r.Y) ([int]$r.Width) ([int]$r.Height) $imgNoLod

Assert-Same 'static full render identical (LOD on idle vs LOD off)' $imgLodIdle $imgNoLod 50

# LOD を既定(有効)へ戻す(LOD 三角形が 0 でなくなる = 再構築完了を待つ)
Toggle-Switch (Find-ById $root 'LodToggle')      # ON
$deadline = (Get-Date).AddSeconds(180)
while ((Get-Date) -lt $deadline -and $statsText.Current.Name -like ('*' + $LblLodTri + ' 0*')) {
    Start-Sleep -Milliseconds 500
}

# ---- 7. ライトテーマのスクリーンショット(目視用) ----
Toggle-Switch (Find-ByName $root $LblTheme)
Start-Sleep -Milliseconds 2000
Capture-Region 0 0 1280 960 (Join-Path $outDir 'benchmark-10m-light.png')
Write-Output 'light theme captured'

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
if ($script:failures -gt 0) {
    Write-Output ("done with {0} FAILURE(S)" -f $script:failures)
    exit 1
}
Write-Output 'done: all assertions passed'
