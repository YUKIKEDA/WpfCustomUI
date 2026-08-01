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

function Toggle-Switch($element) {
    $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
}

function Set-SliderValue($element, $value) {
    $element.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue($value)
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

(Find-ByName $root '3D Glyphs').GetCurrentPattern(
    [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 2500

$vp = Find-ByClass $root 'WcuViewport'
if ($null -eq $vp) { Write-Output 'FAIL: WcuViewport not found'; Stop-Process -Id $p.Id; exit 1 }
$r = $vp.Current.BoundingRectangle
$vx = [int]$r.X; $vy = [int]$r.Y; $vw = [int]$r.Width; $vh = [int]$r.Height
function Capture-Viewport($name) {
    $path = Join-Path $outDir $name
    Capture-Region $vx $vy $vw $vh $path
    return $path
}

$glyphToggle = Find-ById $root 'GlyphToggle'
$clipToggle = Find-ById $root 'ClipToggle'
$scaleSlider = Find-ById $root 'ScaleSlider'
$strideCombo = Find-ById $root 'StrideCombo'

# ---- 1. グリフ ON(既定) vs OFF のピクセル差分 ----
$imgOn = Capture-Viewport 'glyph-on.png'
Toggle-Switch $glyphToggle       # OFF
Start-Sleep -Milliseconds 500
$imgOff = Capture-Viewport 'glyph-off.png'
Assert-Diff 'glyphs visible (ON vs OFF)' $imgOn $imgOff 3000

# ---- 2. 決定性: OFF → ON で元の絵に戻る ----
Toggle-Switch $glyphToggle       # ON
Start-Sleep -Milliseconds 500
$imgOnAgain = Capture-Viewport 'glyph-on-again.png'
Assert-Same 'deterministic re-enable (diff ~ 0)' $imgOn $imgOnAgain 50

# ---- 3. スケール変更で矢印が伸びる ----
Set-SliderValue $scaleSlider 2.5
Start-Sleep -Milliseconds 500
$imgScaled = Capture-Viewport 'glyph-scale.png'
Assert-Diff 'scale change grows arrows' $imgOn $imgScaled 2000
Set-SliderValue $scaleSlider 1.0
Start-Sleep -Milliseconds 500

# ---- 4. ストライド変更で本数が変わる ----
Select-ComboItem $strideCombo '16'
Start-Sleep -Milliseconds 500
$imgStride = Capture-Viewport 'glyph-stride.png'
Assert-Diff 'stride 4 -> 16 thins arrows' $imgOn $imgStride 2000
Select-ComboItem $strideCombo '4'
Start-Sleep -Milliseconds 500
$imgStrideBack = Capture-Viewport 'glyph-stride-back.png'
Assert-Same 'stride back to 4 (deterministic)' $imgOn $imgStrideBack 50

# ---- 5. 断面カット併用: クリップされた節点の矢印が消える ----
Toggle-Switch $clipToggle        # ON
Start-Sleep -Milliseconds 600
$imgClip = Capture-Viewport 'glyph-clip.png'
Assert-Diff 'section clip removes clipped arrows' $imgOn $imgClip 3000

# クリップ中にグリフだけ OFF → クリップ絵からさらに矢印が消える(クリップ側でも描かれていた証拠)
Toggle-Switch $glyphToggle       # OFF
Start-Sleep -Milliseconds 500
$imgClipNoGlyph = Capture-Viewport 'glyph-clip-noglyph.png'
Assert-Diff 'glyphs present in clipped view' $imgClip $imgClipNoGlyph 1500
Toggle-Switch $glyphToggle       # ON
Toggle-Switch $clipToggle        # OFF
Start-Sleep -Milliseconds 600

# ---- 6. 変形追従: 振動アニメで矢印基点も動く ----
$animToggle = Find-ById $root 'AnimToggle'
Toggle-Switch $animToggle
Start-Sleep -Milliseconds 600
$imgAnimA = Capture-Viewport 'glyph-anim-a.png'
Start-Sleep -Milliseconds 350
$imgAnimB = Capture-Viewport 'glyph-anim-b.png'
$diffA = [PixelDiff]::Count($imgOn, $imgAnimA, 8)
$diffB = [PixelDiff]::Count($imgOn, $imgAnimB, 8)
$animDiff = [Math]::Max($diffA, $diffB)
if ($animDiff -ge 1000) {
    Write-Output ("PASS deformation animation moves glyphs: maxDiff={0}" -f $animDiff)
} else {
    Write-Output ("FAIL deformation animation moves glyphs: maxDiff={0}" -f $animDiff)
    $script:failures++
}
Toggle-Switch $animToggle        # 停止 → scale 0
Start-Sleep -Milliseconds 700
$imgStopped = Capture-Viewport 'glyph-stopped.png'
Assert-Same 'anim stop returns to base (deterministic)' $imgOn $imgStopped 50

# ---- 7. 両テーマのスクリーンショット(目視用) ----
Capture-Region 0 0 1280 960 (Join-Path $outDir 'glyph-dark-full.png')

Toggle-Switch (Find-ByName $root $LblTheme)
Start-Sleep -Milliseconds 1500
Capture-Region 0 0 1280 960 (Join-Path $outDir 'glyph-light-full.png')
Write-Output 'light theme captured'

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
if ($script:failures -gt 0) {
    Write-Output ("done with {0} FAILURE(S)" -f $script:failures)
    exit 1
}
Write-Output 'done: all assertions passed'
