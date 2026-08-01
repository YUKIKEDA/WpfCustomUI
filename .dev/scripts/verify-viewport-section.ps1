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
    // 2 枚の PNG の相違ピクセル数(RGB いずれかの差が tolerance 超)を数える
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

function Set-RangeValue($element, $value) {
    $element.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue($value)
}

function Left-Drag($x0, $y0, $x1, $y1) {
    [Win32]::SetCursorPos($x0, $y0) | Out-Null
    Start-Sleep -Milliseconds 150
    [Win32]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    for ($i = 1; $i -le 10; $i++) {
        $mx = [int]($x0 + ($x1 - $x0) * $i / 10)
        $my = [int]($y0 + ($y1 - $y0) * $i / 10)
        [Win32]::SetCursorPos($mx, $my) | Out-Null
        Start-Sleep -Milliseconds 30
    }
    [Win32]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 500
}

# 差分アサート: 大きく変わるべき / 変わらないべき
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

(Find-ByName $root '3D Section').GetCurrentPattern(
    [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 2000

$vp = Find-ByClass $root 'WcuViewport'
if ($null -eq $vp) { Write-Output 'FAIL: WcuViewport not found'; Stop-Process -Id $p.Id; exit 1 }
$r = $vp.Current.BoundingRectangle
$vx = [int]$r.X; $vy = [int]$r.Y; $vw = [int]$r.Width; $vh = [int]$r.Height
function Capture-Viewport($name) {
    $path = Join-Path $outDir $name
    Capture-Region $vx $vy $vw $vh $path
    return $path
}

$clipToggle = Find-ById $root 'ClipToggle'
$flipToggle = Find-ById $root 'FlipToggle'
$indicatorToggle = Find-ById $root 'IndicatorToggle'
$sliceToggle = Find-ById $root 'SliceToggle'
$offsetSlider = Find-ById $root 'OffsetSlider'
$summary = Find-ById $root 'SelectionSummary'
function Get-Faces {
    if ($summary.Current.Name -match 'Faces: (\d+)') { return [int]$Matches[1] }
    return -1
}

# ---- 1. 断面カット ON(初期)vs OFF: 大きく変わる。ON へ戻すと元と一致 ----
$imgOn = Capture-Viewport 'section-on.png'
Capture-Region 0 0 1280 960 (Join-Path $outDir 'section-initial-full.png')
Toggle-Switch $clipToggle
Start-Sleep -Milliseconds 700
$imgOff = Capture-Viewport 'section-off.png'
Assert-Diff 'clip on -> off' $imgOn $imgOff 3000

Toggle-Switch $clipToggle
Start-Sleep -Milliseconds 700
$imgOn2 = Capture-Viewport 'section-on2.png'
Assert-Same 'clip re-enabled (deterministic)' $imgOn $imgOn2 150

# ---- 2. オフセット移動で断面位置が変わる ----
Set-RangeValue $offsetSlider 24.0
Start-Sleep -Milliseconds 700
$imgOffset = Capture-Viewport 'section-offset24.png'
Assert-Diff 'offset 0 -> 24' $imgOn2 $imgOffset 1500

# ---- 3. 反転で残る側が入れ替わる ----
Toggle-Switch $flipToggle
Start-Sleep -Milliseconds 700
$imgFlip = Capture-Viewport 'section-flip.png'
Assert-Diff 'flip (keep other side)' $imgOffset $imgFlip 3000
Capture-Region 0 0 1280 960 (Join-Path $outDir 'section-flip-full.png')
Toggle-Switch $flipToggle
Set-RangeValue $offsetSlider 0.0
Start-Sleep -Milliseconds 700
$imgBase = Capture-Viewport 'section-base.png'

# ---- 4. 平面インジケータの表示/非表示 ----
Toggle-Switch $indicatorToggle
Start-Sleep -Milliseconds 700
$imgNoInd = Capture-Viewport 'section-no-indicator.png'
Assert-Diff 'indicator off' $imgBase $imgNoInd 200
Toggle-Switch $indicatorToggle
Start-Sleep -Milliseconds 500

# ---- 5. 断面スライス(アプリ実装)の表示/非表示 ----
Toggle-Switch $sliceToggle
Start-Sleep -Milliseconds 700
$imgNoSlice = Capture-Viewport 'section-no-slice.png'
Assert-Diff 'app slice off' $imgBase $imgNoSlice 300

# ---- 6. ピック整合: クリップで隠れた面は矩形選択にも掛からない ----
# スライス OFF のまま、オフセット -40(既定法線 -X では x<-40 の薄いスライバのみ残る)で
# 矩形選択し、クリップ OFF の同じ矩形選択より面数が大幅に少ないことを確認する
Set-RangeValue $offsetSlider -40.0
Start-Sleep -Milliseconds 700
$dx0 = [int]($r.X + $r.Width * 0.06); $dy0 = [int]($r.Y + $r.Height * 0.15)
$dx1 = [int]($r.X + $r.Width * 0.70); $dy1 = [int]($r.Y + $r.Height * 0.92)
Left-Drag $dx0 $dy0 $dx1 $dy1
$facesClipped = Get-Faces
Write-Output ("rect select with clip (offset 40): Faces=" + $facesClipped)

Invoke-Button (Find-ById $root 'ClearSelectionButton')
Start-Sleep -Milliseconds 300
Toggle-Switch $clipToggle   # クリップ OFF
Start-Sleep -Milliseconds 700
Left-Drag $dx0 $dy0 $dx1 $dy1
$facesFull = Get-Faces
Write-Output ("rect select without clip: Faces=" + $facesFull)

if ($facesFull -gt $facesClipped -and $facesClipped -ge 0) {
    Write-Output ("PASS pick consistency: clipped {0} < full {1}" -f $facesClipped, $facesFull)
} else {
    Write-Output ("FAIL pick consistency: clipped {0}, full {1}" -f $facesClipped, $facesFull)
    $script:failures++
}

Invoke-Button (Find-ById $root 'ClearSelectionButton')
Toggle-Switch $clipToggle   # クリップ ON へ戻す
Toggle-Switch $sliceToggle  # スライス ON へ戻す
Set-RangeValue $offsetSlider 0.0
Start-Sleep -Milliseconds 700

# ---- 7. 面選択ハイライト+ライトテーマの見た目確認 ----
Left-Drag ([int]($r.X + $r.Width * 0.30)) ([int]($r.Y + $r.Height * 0.30)) `
          ([int]($r.X + $r.Width * 0.55)) ([int]($r.Y + $r.Height * 0.60))
Write-Output ("selection for theme check: " + $summary.Current.Name)
Capture-Region 0 0 1280 960 (Join-Path $outDir 'section-dark-full.png')

Toggle-Switch (Find-ByName $root $LblTheme)
Start-Sleep -Milliseconds 1500
Capture-Region 0 0 1280 960 (Join-Path $outDir 'section-light-full.png')
Write-Output 'light theme captured'

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
if ($script:failures -gt 0) {
    Write-Output ("done with {0} FAILURE(S)" -f $script:failures)
    exit 1
}
Write-Output 'done: all assertions passed'
