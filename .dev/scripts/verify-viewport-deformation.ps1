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
$LblMode2 = '2' + [char]0x6B21 + [char]0x66F2 + [char]0x3052                                          # 2次曲げ
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

(Find-ByName $root '3D Deformation').GetCurrentPattern(
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

# ---- 1. 初期状態(モード1、スケール5)vs スケール0(平坦) ----
$imgInitial = Capture-Viewport 'deform-initial.png'
$slider = Find-ById $root 'DeformScaleSlider'
Set-RangeValue $slider 0.0
Start-Sleep -Milliseconds 600
$imgFlat = Capture-Viewport 'deform-flat.png'
Assert-Diff 'scale 5 -> 0 (deformation visible)' $imgInitial $imgFlat 3000

# ---- 2. 自動スケール適用で再び変形 ----
Invoke-Button (Find-ById $root 'AutoScaleButton')
Start-Sleep -Milliseconds 600
$imgAuto = Capture-Viewport 'deform-auto.png'
Assert-Diff 'auto scale (deformed again)' $imgFlat $imgAuto 3000
$scaleText = (Find-ById $root 'ScaleText').Current.Name
Write-Output ("auto scale value: " + $scaleText)

# ---- 3. 非変形ワイヤフレーム重畳 ----
Toggle-Switch (Find-ById $root 'UndeformedToggle')
Start-Sleep -Milliseconds 600
$imgGhost = Capture-Viewport 'deform-ghost.png'
Assert-Diff 'undeformed wireframe overlay' $imgAuto $imgGhost 500
Capture-Region 0 0 1280 960 (Join-Path $outDir 'deform-ghost-full.png')
Toggle-Switch (Find-ById $root 'UndeformedToggle')
Start-Sleep -Milliseconds 400

# ---- 4. モード切替(1次 -> 2次) ----
$combo = Find-ById $root 'ModeCombo'
$combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
Start-Sleep -Milliseconds 400
$item2 = Find-ByName $combo $LblMode2
$item2.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 700
$imgMode2 = Capture-Viewport 'deform-mode2.png'
Assert-Diff 'mode 1 -> mode 2' $imgAuto $imgMode2 3000
Capture-Region 0 0 1280 960 (Join-Path $outDir 'deform-mode2-full.png')

# ---- 5. 振動アニメーション: ON で連続 2 枚が異なり、OFF で一致する ----
Toggle-Switch (Find-ById $root 'AnimToggle')
Start-Sleep -Milliseconds 500
$imgAnimA = Capture-Viewport 'deform-anim-a.png'
Start-Sleep -Milliseconds 300
$imgAnimB = Capture-Viewport 'deform-anim-b.png'
Assert-Diff 'animation running (frames differ)' $imgAnimA $imgAnimB 1000

Toggle-Switch (Find-ById $root 'AnimToggle')
Start-Sleep -Milliseconds 600
$imgStillA = Capture-Viewport 'deform-still-a.png'
Start-Sleep -Milliseconds 300
$imgStillB = Capture-Viewport 'deform-still-b.png'
Assert-Same 'animation stopped (frames identical)' $imgStillA $imgStillB 100

# ---- 6. PlaybackBar 過渡再生: フレーム 0 と中間フレームで表示が変わる ----
# PlaybackBar 本体は UIA ピアを持たないため、テンプレート内の FrameSlider を直接探す
$frameSlider = Find-ById $root 'FrameSlider'
if ($null -eq $frameSlider) { Write-Output 'FAIL: PlaybackBar slider not found'; $script:failures++ }
else {
    Set-RangeValue $frameSlider 0
    Start-Sleep -Milliseconds 500
    $imgFrame0 = Capture-Viewport 'deform-frame0.png'
    Set-RangeValue $frameSlider 14
    Start-Sleep -Milliseconds 500
    $imgFrame14 = Capture-Viewport 'deform-frame14.png'
    Assert-Diff 'transient playback (frame 0 -> 14)' $imgFrame0 $imgFrame14 1000
    Capture-Region 0 0 1280 960 (Join-Path $outDir 'deform-playback-full.png')
}

# ---- 7. ライトテーマ(変形+非変形重畳の見た目確認) ----
Toggle-Switch (Find-ById $root 'UndeformedToggle')
Start-Sleep -Milliseconds 300
$toggle = Find-ByName $root $LblTheme
Toggle-Switch $toggle
Start-Sleep -Milliseconds 1500
Capture-Region 0 0 1280 960 (Join-Path $outDir 'deform-light-full.png')
Write-Output 'light theme captured'

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
if ($script:failures -gt 0) {
    Write-Output ("done with {0} FAILURE(S)" -f $script:failures)
    exit 1
}
Write-Output 'done: all assertions passed'
