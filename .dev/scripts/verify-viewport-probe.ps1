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
Add-Type -ReferencedAssemblies 'System.Drawing' @"
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

function Invoke-Button($element) {
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Toggle-Switch($element) {
    $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
}

function Left-Click($x, $y) {
    [Win32]::SetCursorPos([int]$x, [int]$y) | Out-Null
    Start-Sleep -Milliseconds 150
    [Win32]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [Win32]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
}

$script:failures = 0
function Assert-Diff($label, $fileA, $fileB, $minPixels) {
    $diff = [PixelDiff]::Count($fileA, $fileB, 8)
    if ($diff -ge $minPixels) {
        Write-Output ("PASS {0}: diff={1} (>= {2})" -f $label, $diff, $minPixels)
    } else {
        Write-Output ("FAIL {0}: diff={1} (expected >= {2})" -f $label, $diff, $minPixels)
        $script:failures++
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
function Assert-True($label, $condition, $detail) {
    if ($condition) {
        Write-Output ("PASS {0}: {1}" -f $label, $detail)
    } else {
        Write-Output ("FAIL {0}: {1}" -f $label, $detail)
        $script:failures++
    }
}

# 日本語ラベル(エンコーディング事故を避けるため char 合成)
$LblTheme = [string][char]0x30E9 + [char]0x30A4 + [char]0x30C8 + [char]0x30C6 + [char]0x30FC + [char]0x30DE  # ライトテーマ

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

(Find-ByName $root '3D Probe').GetCurrentPattern(
    [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 2000

$vp = Find-ByClass $root 'WcuViewport'
if ($null -eq $vp) { Write-Output 'FAIL: WcuViewport not found'; Stop-Process -Id $p.Id; exit 1 }
$r = $vp.Current.BoundingRectangle
$vx = [int]$r.X; $vy = [int]$r.Y; $vw = [int]$r.Width; $vh = [int]$r.Height
$cx = [int]($r.X + $r.Width / 2.0); $cy = [int]($r.Y + $r.Height / 2.0)
function Capture-Viewport($name) {
    $path = Join-Path $outDir $name
    Capture-Region $vx $vy $vw $vh $path
    return $path
}

# ピクセル/mm 換算: 上面視点+FitToBounds(margin 1.1, FOV 45°)で平板(z=0)面は
# 半画面高 = 境界球半径 72.11 * 1.1 / cos(22.5°) = 85.87 mm に対応する
$pxPerMm = ($vh / 2.0) / 85.87
$offNear = [int](11.0 * $pxPerMm)   # 孔縁(a=10mm)のすぐ外側
$offFar = [int](40.0 * $pxPerMm)    # 遠方場(x 軸に沿って r=40mm)
Write-Output ("viewport {0}x{1}, pxPerMm={2:0.00}, offNear={3}px, offFar={4}px" -f $vw, $vh, $pxPerMm, $offNear, $offFar)

$annotationList = Find-ById $root 'AnnotationList'
$summary = Find-ById $root 'AnnotationSummary'
function Get-AnnotationTexts {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'ListBoxItem')
    $items = $annotationList.FindAll([System.Windows.Automation.TreeScope]::Children, $c)
    $texts = @()
    foreach ($item in $items) { $texts += $item.Current.Name }
    return ,$texts
}
function Parse-Mpa($text) {
    if ($text -match '=\s*([\-0-9.]+)\s*MPa') { return [double]$Matches[1] }
    return [double]::NaN
}

# ---- 1. プローブクリックで注釈チップが立つ(ピクセル差分) ----
$imgBase = Capture-Viewport 'probe-base.png'
Left-Click ($cx + $offFar) $cy          # 遠方場(θ=0, r=40mm)
$imgOne = Capture-Viewport 'probe-one.png'
Assert-Diff 'annotation chip appears' $imgBase $imgOne 200

# ---- 2. 孔縁 4 点(右/上/左/下)をプローブ ----
Left-Click ($cx + $offNear) $cy
Left-Click $cx ($cy - $offNear)
Left-Click ($cx - $offNear) $cy
Left-Click $cx ($cy + $offNear)
Start-Sleep -Milliseconds 300

# ---- 3. ラベル文字列を直接アサート(Kirsch 厳密解との照合) ----
$texts = Get-AnnotationTexts
Write-Output ("annotations: " + ($texts -join ' | '))
Assert-True 'annotation count after 5 probes' ($texts.Count -eq 5) ("count=" + $texts.Count)

$farValue = Parse-Mpa $texts[0]
$nearValues = @()
for ($i = 1; $i -lt $texts.Count; $i++) { $nearValues += Parse-Mpa $texts[$i] }
$nearMax = ($nearValues | Measure-Object -Maximum).Maximum
$nearMin = ($nearValues | Measure-Object -Minimum).Minimum

# 遠方場(θ=0, r≈40mm): σ_vM ≈ 0.84S ≈ 84 MPa
Assert-True 'far-field value ~ 0.84S' ($farValue -ge 60.0 -and $farValue -le 110.0) ("value=" + $farValue)
# 孔縁 θ=±90°: 応力集中で σ_vM → 3S = 300 MPa(r≈1.1a で ≈ 230)
Assert-True 'hole-edge max ~ 3S concentration' ($nearMax -ge 190.0 -and $nearMax -le 320.0) ("max=" + $nearMax)
# 孔縁 θ=0°/180°: σ_vM は低い(≈ 0.6S)→ Kirsch の異方性を確認
Assert-True 'hole-edge min (anisotropy)' ($nearMin -ge 30.0 -and $nearMin -le 110.0) ("min=" + $nearMin)

# ---- 4. 空クリック(背景)では注釈が増えない ----
Left-Click ($vx + 15) ($vy + 15)
$texts = Get-AnnotationTexts
Assert-True 'empty click adds nothing' ($texts.Count -eq 5) ("count=" + $texts.Count)

# ---- 5. プローブモード OFF ではクリックしても注釈が増えない ----
$probeToggle = Find-ById $root 'ProbeToggle'
Toggle-Switch $probeToggle
Start-Sleep -Milliseconds 300
Left-Click $cx ($cy - $offNear)
$texts = Get-AnnotationTexts
Assert-True 'probe off adds nothing' ($texts.Count -eq 5) ("count=" + $texts.Count)
Toggle-Switch $probeToggle
Start-Sleep -Milliseconds 300

# ---- 6. 削除/全削除(アプリ側参照実装) ----
$c = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'ListBoxItem')
$firstItem = $annotationList.FindFirst([System.Windows.Automation.TreeScope]::Children, $c)
$firstItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Invoke-Button (Find-ById $root 'RemoveAnnotationButton')
Start-Sleep -Milliseconds 300
$texts = Get-AnnotationTexts
Assert-True 'remove selected annotation' ($texts.Count -eq 4) ("count=" + $texts.Count)

Invoke-Button (Find-ById $root 'ClearAnnotationsButton')
Start-Sleep -Milliseconds 500
$texts = Get-AnnotationTexts
Assert-True 'clear all annotations' ($texts.Count -eq 0) ("count=" + $texts.Count + ", summary=" + $summary.Current.Name)
$imgCleared = Capture-Viewport 'probe-cleared.png'
Assert-Same 'cleared viewport matches base' $imgBase $imgCleared 150

# ---- 7. 変形追従: 振動アニメ中は注釈チップも動き、停止で決定的に戻る ----
Left-Click $cx ($cy - $offNear)   # 孔上縁に注釈 1 件
Start-Sleep -Milliseconds 300
$imgAnchor = Capture-Viewport 'probe-anchor.png'

$animToggle = Find-ById $root 'AnimToggle'
Toggle-Switch $animToggle
Start-Sleep -Milliseconds 600
$imgAnimA = Capture-Viewport 'probe-anim-a.png'
Start-Sleep -Milliseconds 350
$imgAnimB = Capture-Viewport 'probe-anim-b.png'
$diffA = [PixelDiff]::Count($imgAnchor, $imgAnimA, 8)
$diffB = [PixelDiff]::Count($imgAnchor, $imgAnimB, 8)
$animDiff = [Math]::Max($diffA, $diffB)
Assert-True 'deformation animation moves scene+chip' ($animDiff -ge 1000) ("maxDiff=" + $animDiff)

Toggle-Switch $animToggle   # 停止 → scale 0 に戻る
Start-Sleep -Milliseconds 700
$imgStopped = Capture-Viewport 'probe-stopped.png'
Assert-Same 'anim stop returns to base (deterministic)' $imgAnchor $imgStopped 150

# ---- 8. 両テーマのスクリーンショット(目視用) ----
Left-Click ($cx + $offFar) $cy   # もう 1 件追加して見栄えを整える
Start-Sleep -Milliseconds 300
Capture-Region 0 0 1280 960 (Join-Path $outDir 'probe-dark-full.png')

Toggle-Switch (Find-ByName $root $LblTheme)
Start-Sleep -Milliseconds 1500
Capture-Region 0 0 1280 960 (Join-Path $outDir 'probe-light-full.png')
Write-Output 'light theme captured'

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
if ($script:failures -gt 0) {
    Write-Output ("done with {0} FAILURE(S)" -f $script:failures)
    exit 1
}
Write-Output 'done: all assertions passed'
