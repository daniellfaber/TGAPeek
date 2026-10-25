<#
.SYNOPSIS
  End-to-end check of the real viewer: launches it on generated samples, checks titles and error
  dialogs, and captures the window to confirm the test pattern appears with exact colours in the
  right place (orientation, channel order and unscaled drawing).

.EXAMPLE
  dotnet run --project tools/TGAPeek.SampleGenerator -- samples/generated
  powershell -ExecutionPolicy Bypass -File tools/SmokeTest.ps1 -Exe artifacts\publish\win-x64-framework-dependent\TGAPeek.exe

  The viewer's settings file is backed up first and restored afterwards.
  Viewer windows open and close on the desktop while it runs. Keyboard checks send keys only while the
  viewer is the foreground window; if Windows keeps another program in front (for example because the
  keyboard or mouse is in use), those checks are skipped rather than typed into that program.
  Run it while nobody is using the PC: viewer windows open under the mouse pointer, and scrolling or
  typing can reach them and change what is on screen. Add -CaptureFolder <folder> to save the window
  capture behind any failed visual check.
#>
param(
    [string]$Exe = (Join-Path $PSScriptRoot "..\artifacts\publish\win-x64-framework-dependent\TGAPeek.exe"),
    [string]$Samples = (Join-Path $PSScriptRoot "..\samples\generated"),
    # When set, the window capture behind each failed visual check is saved here as a PNG.
    [string]$CaptureFolder = ""
)

$ErrorActionPreference = "Stop"
$Exe = (Resolve-Path $Exe).Path
$Samples = (Resolve-Path $Samples).Path

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

public static class WindowProbe
{
    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);

    /// <summary>Asks Windows to bring this window to the front; Windows may refuse.</summary>
    public static void Activate(IntPtr hwnd) { SetForegroundWindow(hwnd); }

    /// <summary>The process that owns the window receiving keyboard input.</summary>
    public static int ForegroundProcessId()
    {
        uint pid;
        GetWindowThreadProcessId(GetForegroundWindow(), out pid);
        return (int)pid;
    }
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    public static string[] WindowTitles(int processId)
    {
        var titles = new List<string>();
        EnumWindows(delegate (IntPtr hwnd, IntPtr lParam)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == processId && IsWindowVisible(hwnd))
            {
                var text = new StringBuilder(512);
                GetWindowText(hwnd, text, text.Capacity);
                titles.Add(text.ToString());
            }
            return true;
        }, IntPtr.Zero);
        return titles.ToArray();
    }

    /// <summary>The window's pixels, or null if the window no longer exists or has no area.</summary>
    public static Bitmap Capture(IntPtr hwnd)
    {
        SetThreadDpiAwarenessContext(new IntPtr(-4)); // per-monitor aware: real pixel sizes
        Rect r;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out r) || r.Right <= r.Left || r.Bottom <= r.Top) return null;
        var bitmap = new Bitmap(r.Right - r.Left, r.Bottom - r.Top, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            IntPtr hdc = g.GetHdc();
            PrintWindow(hwnd, hdc, 2); // PW_RENDERFULLCONTENT
            g.ReleaseHdc(hdc);
        }
        return bitmap;
    }

    /// <summary>Bounding box of pixels exactly equal to rgb, as "left,top,right,bottom,count" (empty if none).</summary>
    public static int[] Find(Bitmap b, int rgb, int left, int top, int right, int bottom)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1, count = 0;
        for (int y = Math.Max(0, top); y <= Math.Min(b.Height - 1, bottom); y++)
            for (int x = Math.Max(0, left); x <= Math.Min(b.Width - 1, right); x++)
                if ((b.GetPixel(x, y).ToArgb() & 0xFFFFFF) == rgb)
                {
                    count++;
                    if (x < x0) x0 = x; if (y < y0) y0 = y; if (x > x1) x1 = x; if (y > y1) y1 = y;
                }
        return count == 0 ? new int[0] : new[] { x0, y0, x1, y1, count };
    }
}
"@

$failures = New-Object System.Collections.Generic.List[string]
function Sample([string]$prefix) {
    $file = Get-ChildItem -LiteralPath $Samples -Filter "$prefix *" | Select-Object -First 1
    if ($null -eq $file) { throw "No sample starting with '$prefix ' in $Samples" }
    return $file.Name
}
function Check([bool]$condition, [string]$message) {
    if ($condition) { Write-Host "  PASS  $message" } else { Write-Host "  FAIL  $message" -ForegroundColor Red; $failures.Add($message) }
}
$skipped = New-Object System.Collections.Generic.List[string]
function Skip([string]$message) {
    Write-Host "  SKIP  $message (Windows kept '$script:inFront' in front, so no keys were sent)" -ForegroundColor Yellow
    $skipped.Add($message)
}
function Save-Capture($bitmap, [string]$label) {
    if ($CaptureFolder -eq "" -or $null -eq $bitmap) { return }
    New-Item -ItemType Directory -Force $CaptureFolder | Out-Null
    $file = Join-Path $CaptureFolder ((($label -replace '[^A-Za-z0-9]+', '-').Trim('-')) + ".png")
    $bitmap.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "        capture saved: $file"
}
# Retries a check for up to 3 seconds, so a slow first paint is not mistaken for a wrong one.
function WaitFor([scriptblock]$condition) {
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 300
    }
    return $false
}

$settings = Join-Path $env:LOCALAPPDATA "TGAPeek\settings.json"
$settingsFolder = Split-Path $settings
$hadSettingsFolder = Test-Path $settingsFolder
$backup = $null
if (Test-Path $settings) { $backup = Get-Content $settings -Raw -Encoding UTF8 }
function Read-Settings { if (Test-Path $settings) { Get-Content $settings -Raw | ConvertFrom-Json } }

function Start-Viewer([string[]]$paths) {
    $quoted = $paths | ForEach-Object { '"' + $_ + '"' }
    $p = Start-Process -FilePath $Exe -ArgumentList $quoted -PassThru
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        $p.Refresh()
        $titles = [WindowProbe]::WindowTitles($p.Id)
        if ($titles.Count -gt 0 -and ($titles -match "TGAPeek").Count -gt 0) { break }
    }
    Start-Sleep -Milliseconds 1200 # let decoding and the first paint finish
    return $p
}

function Stop-Viewer($p) {
    if (-not $p.HasExited) { [void]$p.CloseMainWindow(); if (-not $p.WaitForExit(5000)) { Stop-Process -Id $p.Id -Force } }
}

try {
    Write-Host "Viewer: $Exe"

    foreach ($name in ("01", "02", "03", "04", "12", "16", "19", "20" | ForEach-Object { Sample $_ })) {
        Write-Host "`n$name"
        $p = Start-Viewer @((Join-Path $Samples $name))
        try {
            $titles = [WindowProbe]::WindowTitles($p.Id)
            Check (($titles -like "$name*").Count -gt 0) "window title shows the file name"
            Check (($titles -contains "Cannot open image") -eq $false) "no error dialog"
            $bitmap = $null
            $found = WaitFor {
                if ($null -ne $bitmap) { $bitmap.Dispose() }
                $p.Refresh()
                $script:bitmap = [WindowProbe]::Capture($p.MainWindowHandle)
                if ($null -eq $bitmap) { $script:red = @(); return $false }
                $w = $bitmap.Width; $h = $bitmap.Height
                $script:red = [WindowProbe]::Find($bitmap, 0xFF0000, 0, 0, $w, $h)
                $script:green = [WindowProbe]::Find($bitmap, 0x00FF00, 0, 0, $w, $h)
                $script:blue = [WindowProbe]::Find($bitmap, 0x0000FF, 0, 0, $w, $h)
                $red.Count -gt 0 -and $green.Count -gt 0 -and $blue.Count -gt 0
            }
            $note = if ($null -eq $bitmap) { " (the viewer window could not be captured; still running: $(-not $p.HasExited))" } else { "" }
            Check $found "exact red, green and blue pixels are on screen$note"
            if (-not $found) { Save-Capture $bitmap "$name colours" }
            if ($red.Count -gt 0 -and $green.Count -gt 0 -and $blue.Count -gt 0) {
                Check ($green[0] -gt $red[2]) "green quadrant is to the right of red"
                Check ($blue[1] -gt $red[3]) "blue quadrant is below red"
                $white = [WindowProbe]::Find($bitmap, 0xFFFFFF, $green[0], $blue[1], $green[2], $blue[3])
                $area = ($green[2] - $green[0] + 1) * ($blue[3] - $blue[1] + 1)
                Check ($white.Count -gt 0 -and $white[4] -ge 0.97 * $area) "bottom-right quadrant is exact white"
                $black = [WindowProbe]::Find($bitmap, 0x000000, $red[0], $red[1], $red[2], $red[3])
                Check ($black.Count -gt 0 -and $black[0] -eq $red[0] -and $black[1] -eq $red[1]) "black marker is in the top-left corner"
                $redArea = ($red[2] - $red[0] + 1) * ($red[3] - $red[1] + 1)
                Check (($red[4] + $black[4]) -eq $redArea) "red quadrant contains only exact red and the black marker"
            }
            if ($null -ne $bitmap) { $bitmap.Dispose() }
        } finally { Stop-Viewer $p }
    }

    foreach ($case in @(
        @{ Name = (Sample "25"); Dialog = $true },
        @{ Name = (Sample "26"); Dialog = $true },
        @{ Name = (Sample "23"); Dialog = $false },
        @{ Name = (Sample "24"); Dialog = $false },
        @{ Name = (Sample "21"); Dialog = $false },
        @{ Name = (Sample "27"); Dialog = $false })) {
        Write-Host "`n$($case.Name)"
        $p = Start-Viewer @((Join-Path $Samples $case.Name))
        try {
            $titles = [WindowProbe]::WindowTitles($p.Id)
            if ($case.Dialog) {
                Check ($titles -contains "Cannot open image") "an explanatory error dialog is shown"
                Check (-not $p.HasExited) "the viewer is still running"
            } else {
                Check (($titles -like "$($case.Name)*").Count -gt 0) "window title shows the file name"
                Check (($titles -contains "Cannot open image") -eq $false) "no error dialog"
            }
        } finally { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } }
    }

    Write-Host "`nKeyboard: actual pixels (Ctrl+1) and channel views"
    Add-Type -AssemblyName System.Windows.Forms
    # SendKeys types into whichever window is in front, and Windows may refuse to bring the viewer there
    # (for example while someone is using another program). So each key is sent only after confirming the
    # viewer is the foreground window; otherwise nothing is sent and the dependent checks are skipped.
    function Keys([string[]]$keys) {
        foreach ($k in $keys) {
            [WindowProbe]::Activate($p.MainWindowHandle)
            Start-Sleep -Milliseconds 300
            $front = [WindowProbe]::ForegroundProcessId()
            if ($front -ne $p.Id) {
                $script:inFront = (Get-Process -Id $front -ErrorAction SilentlyContinue).ProcessName
                return $false
            }
            [System.Windows.Forms.SendKeys]::SendWait($k)
            Start-Sleep -Milliseconds 700
        }
        return $true
    }
    function Count([int]$rgb) {
        $p.Refresh()
        $bitmap = [WindowProbe]::Capture($p.MainWindowHandle)
        if ($null -eq $bitmap) { return -1 }
        $found = [WindowProbe]::Find($bitmap, $rgb, 0, 0, $bitmap.Width, $bitmap.Height)
        $bitmap.Dispose()
        if ($found.Count -eq 0) { return 0 } else { return $found[4] }
    }

    $p = Start-Viewer @((Join-Path $Samples (Sample "05")))
    try {
        $message = "100% RGB view shows each 32 x 24 quadrant pixel for pixel (759 red + 9 marker, 768 green, 768 blue)"
        if (Keys "^1", "^3") {
            $ok = WaitFor { $script:seen = "{0}/{1}/{2}" -f (Count 0xFF0000), (Count 0x00FF00), (Count 0x0000FF); $seen -eq "759/768/768" }
            Check $ok "$message; saw $seen"
            if (-not $ok) { Save-Capture ([WindowProbe]::Capture($p.MainWindowHandle)) "100 percent RGB view" }
        } else { Skip $message }
        $message = "alpha view shows column 32 (alpha 126) as grey 126, 48 pixels tall"
        if (Keys "^7") {
            $ok = WaitFor { $script:seen = Count 0x7E7E7E; $seen -eq 48 }
            Check $ok "$message; saw $seen"
            if (-not $ok) { Save-Capture ([WindowProbe]::Capture($p.MainWindowHandle)) "alpha view" }
        } else { Skip $message }
        [void](Keys "^2")
    } finally { Stop-Viewer $p }

    $p = Start-Viewer @((Join-Path $Samples (Sample "01")))
    try {
        $message = "alpha view of a 24-bit image shows the no-alpha pattern, not white"
        if (Keys "^7") { Check (WaitFor { (Count 0x3A3A3A) -gt 0 -and (Count 0x4E4E4E) -gt 0 }) $message } else { Skip $message }
    } finally { Stop-Viewer $p }

    Write-Host "`nInformation panel, scaling, zoom and navigation"
    $p = Start-Viewer @((Join-Path $Samples (Sample "21")))
    try {
        $message = "no unexpected error after panel, scaling, zoom and background commands"
        if (Keys "{F9}", "^+s", "{ADD}", "{ADD}", "{SUBTRACT}", "^0", "^+n", "^b", "^b", "{F9}") {
            Check (-not ([WindowProbe]::WindowTitles($p.Id) -contains "TGAPeek error")) $message
        } else { Skip $message }
        $message = "Right arrow opens the next file in the folder"
        if (Keys "{RIGHT}") { Check (WaitFor { ([WindowProbe]::WindowTitles($p.Id) -like "$(Sample "22")*").Count -gt 0 }) $message } else { Skip $message }
    } finally { Stop-Viewer $p }

    Write-Host "`nWindow size is remembered without growing"
    $p = Start-Viewer @((Join-Path $Samples (Sample "01"))); Stop-Viewer $p
    $first = Read-Settings
    $p = Start-Viewer @((Join-Path $Samples (Sample "01"))); Stop-Viewer $p
    $second = Read-Settings
    Check ($null -ne $first -and $null -ne $second) "settings are saved when the viewer closes"
    Check ($first.WindowWidth -gt 0 -and $first.WindowWidth -eq $second.WindowWidth -and $first.WindowHeight -eq $second.WindowHeight) "saved size is unchanged after a restart ($($first.WindowWidth) x $($first.WindowHeight))"

    Write-Host "`nSeveral files on the command line"
    $three = "01", "05", "10" | ForEach-Object { Join-Path $Samples (Sample $_) }
    $p = Start-Viewer $three
    try {
        $titles = [WindowProbe]::WindowTitles($p.Id)
        Check (($titles -like "01 true-colour*(1 of 3)*").Count -gt 0) "first file shown, position 1 of 3"
    } finally { Stop-Viewer $p }

    Write-Host "`nMissing file"
    $p = Start-Viewer @((Join-Path $Samples "does not exist.tga"))
    try {
        Check ([WindowProbe]::WindowTitles($p.Id) -contains "Cannot open image") "an explanatory error dialog is shown"
    } finally { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } }
}
finally {
    if ($null -ne $backup) { Set-Content -Path $settings -Value $backup -Encoding UTF8 -NoNewline }
    elseif (Test-Path $settings) { Remove-Item $settings }
    # Remove the settings folder only if this run created it and left it empty.
    if (-not $hadSettingsFolder -and (Test-Path $settingsFolder) -and -not (Get-ChildItem $settingsFolder -Force)) { Remove-Item $settingsFolder }
}

Write-Host ""
if ($failures.Count -gt 0) { Write-Host "$($failures.Count) smoke check(s) failed." -ForegroundColor Red; exit 1 }
if ($skipped.Count -gt 0) {
    Write-Host "No check failed, but $($skipped.Count) keyboard check(s) were skipped. Run again without using the keyboard or mouse meanwhile." -ForegroundColor Yellow
    exit 2
}
Write-Host "All smoke checks passed." -ForegroundColor Green
exit 0
