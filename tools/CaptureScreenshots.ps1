<#
.SYNOPSIS
  Captures reference screenshots of the viewer in fixed states, for docs/DESIGN.md.

.DESCRIPTION
  Launches the viewer on generated samples, drives it through UI Automation (toolbar and menu
  buttons) and messages posted to its own windows (pointer position), and saves each window
  capture as a PNG. It never sends keystrokes or mouse input to the desktop, so it cannot type
  into other programs; viewer windows do open on screen while it runs. The viewer's settings file
  is replaced by a fixed one during the run (fixed window size, information panel shown) and
  restored afterwards. Viewers are closed without saving settings.

.EXAMPLE
  dotnet run --project tools/TGAPeek.SampleGenerator -- samples/generated
  powershell -ExecutionPolicy Bypass -File tools/CaptureScreenshots.ps1 -Exe artifacts\publish\win-x64-framework-dependent\TGAPeek.exe -Out docs\design\screenshots -Prefix after
#>
param(
    [string]$Exe = (Join-Path $PSScriptRoot "..\artifacts\publish\win-x64-framework-dependent\TGAPeek.exe"),
    [string]$Samples = (Join-Path $PSScriptRoot "..\samples\generated"),
    [string]$Out = (Join-Path $PSScriptRoot "..\docs\design\screenshots"),
    [string]$Prefix = "after"
)

$ErrorActionPreference = "Stop"
$Exe = (Resolve-Path $Exe).Path
$Samples = (Resolve-Path $Samples).Path
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class ShotProbe
{
    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [StructLayout(LayoutKind.Sequential)] private struct Pt { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr ChildWindowFromPointEx(IntPtr parent, Pt point, uint flags);
    [DllImport("user32.dll")] private static extern int MapWindowPoints(IntPtr from, IntPtr to, ref Pt point, uint count);

    /// <summary>Visible top-level windows of a process, main window first.</summary>
    public static IntPtr[] Windows(int processId, IntPtr main)
    {
        var list = new List<IntPtr>();
        EnumWindows(delegate (IntPtr hwnd, IntPtr lParam)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == processId && IsWindowVisible(hwnd) && hwnd != main) list.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        list.Reverse(); // bottom of the z-order first, so later windows are drawn on top
        list.Insert(0, main);
        return list.ToArray();
    }

    /// <summary>The main window with any visible pop-ups (menus) drawn at their screen positions.</summary>
    public static Bitmap Capture(IntPtr[] windows)
    {
        SetThreadDpiAwarenessContext(new IntPtr(-4));
        Rect m; GetWindowRect(windows[0], out m);
        var result = new Bitmap(m.Right - m.Left, m.Bottom - m.Top, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            foreach (var hwnd in windows)
            {
                Rect r; GetWindowRect(hwnd, out r);
                if (r.Right <= r.Left || r.Bottom <= r.Top) continue;
                using (var part = new Bitmap(r.Right - r.Left, r.Bottom - r.Top, PixelFormat.Format32bppArgb))
                {
                    using (var pg = Graphics.FromImage(part)) { IntPtr hdc = pg.GetHdc(); PrintWindow(hwnd, hdc, 2); pg.ReleaseHdc(hdc); }
                    g.DrawImageUnscaled(part, r.Left - m.Left, r.Top - m.Top);
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Tells the child window at a fraction of the main window's client area that the pointer is
    /// there (posted messages only; the real pointer does not move); optionally clicks it.
    /// </summary>
    public static void Point(IntPtr main, double fx, double fy, bool click)
    {
        SetThreadDpiAwarenessContext(new IntPtr(-4));
        Rect c; GetClientRect(main, out c);
        var pt = new Pt { X = (int)(c.Right * fx), Y = (int)(c.Bottom * fy) };
        IntPtr hwnd = ChildWindowFromPointEx(main, pt, 0x0001 | 0x0004); // skip invisible and transparent
        while (hwnd != IntPtr.Zero && hwnd != main)
        {
            var local = pt;
            MapWindowPoints(main, hwnd, ref local, 1);
            IntPtr deeper = ChildWindowFromPointEx(hwnd, local, 0x0001 | 0x0004);
            if (deeper == IntPtr.Zero || deeper == hwnd) break;
            hwnd = deeper;
        }
        MapWindowPoints(main, hwnd, ref pt, 1);
        IntPtr lp = new IntPtr((pt.Y << 16) | (pt.X & 0xFFFF));
        PostMessage(hwnd, 0x0200, IntPtr.Zero, lp);                  // WM_MOUSEMOVE
        if (click)
        {
            PostMessage(hwnd, 0x0201, new IntPtr(1), lp);            // WM_LBUTTONDOWN
            PostMessage(hwnd, 0x0202, IntPtr.Zero, lp);              // WM_LBUTTONUP
        }
    }
}
"@

$settings = Join-Path $env:LOCALAPPDATA "TGAPeek\settings.json"
$settingsFolder = Split-Path $settings
$hadSettingsFolder = Test-Path $settingsFolder
$backup = if (Test-Path $settings) { Get-Content $settings -Raw -Encoding UTF8 } else { $null }

function Sample([string]$prefix) { (Get-ChildItem -LiteralPath $Samples -Filter "$prefix *" | Select-Object -First 1).FullName }

function Start-Viewer([string]$file) {
    $arguments = if ($file) { '"' + $file + '"' } else { $null }
    $p = if ($arguments) { Start-Process -FilePath $Exe -ArgumentList $arguments -PassThru } else { Start-Process -FilePath $Exe -PassThru }
    $deadline = (Get-Date).AddSeconds(15)
    do { Start-Sleep -Milliseconds 200; $p.Refresh() } while ($p.MainWindowHandle -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline)
    Start-Sleep -Milliseconds 1200
    # Remember the real main window: while a menu is open, Windows may report the menu instead.
    $p | Add-Member -NotePropertyName Main -NotePropertyValue $p.MainWindowHandle
    return $p
}

function Find([System.Windows.Automation.AutomationElement]$root, [string]$name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Invoke-Named($p, [string]$name) {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($p.Main)
    $element = Find $root $name
    if ($null -eq $element) { throw "No control named '$name'." }
    $pattern = $null
    if ($element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { $pattern.Invoke() }
    elseif ($element.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$pattern)) { $pattern.Expand() }
    else { throw "Control '$name' cannot be invoked." }
    Start-Sleep -Milliseconds 700
}

# Fractions are of the main window's client area; keep them left of the information panel.
function Point-Canvas($p, [double]$fx, [double]$fy, [bool]$click) {
    [ShotProbe]::Point($p.Main, $fx, $fy, $click)
    Start-Sleep -Milliseconds 600
}

function Shot($p, [string]$name) {
    $p.Refresh()
    $bitmap = [ShotProbe]::Capture([ShotProbe]::Windows($p.Id, $p.Main))
    $file = Join-Path $Out "$Prefix-$name.png"
    $bitmap.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    Write-Host "saved $file"
}

function Stop-Viewer($p) { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; $p.WaitForExit() } }

try {
    New-Item -ItemType Directory -Force $settingsFolder | Out-Null
    '{ "WindowX": 120, "WindowY": 80, "WindowWidth": 1280, "WindowHeight": 800, "WindowMaximized": false, "InfoPanelVisible": true, "InfoPanelWidth": 400, "Scaling": "Nearest", "Checkerboard": true, "BackgroundColor": "#808080", "MaxDecodeMemoryMB": 4096 }' |
        Set-Content -Path $settings -Encoding UTF8

    $p = Start-Viewer $null
    try { Shot $p "empty" } finally { Stop-Viewer $p }

    $p = Start-Viewer (Sample "21")
    try {
        Point-Canvas $p 0.22 0.35 $true
        Shot $p "rgba-inspector"
        Invoke-Named $p "View"
        Shot $p "menu"
    } finally { Stop-Viewer $p }

    $p = Start-Viewer (Sample "05")
    try {
        Point-Canvas $p 0.45 0.40 $true
        Invoke-Named $p "Red channel as greyscale (Ctrl+4)"
        Shot $p "red-channel"
        Invoke-Named $p "Alpha channel as greyscale (Ctrl+7)"
        Shot $p "alpha-channel"
    } finally { Stop-Viewer $p }

    $p = Start-Viewer (Sample "23")
    try { Shot $p "partial-image" } finally { Stop-Viewer $p }
}
finally {
    if ($null -ne $backup) { Set-Content -Path $settings -Value $backup -Encoding UTF8 -NoNewline }
    elseif (Test-Path $settings) { Remove-Item $settings }
    if (-not $hadSettingsFolder -and (Test-Path $settingsFolder) -and -not (Get-ChildItem $settingsFolder -Force)) { Remove-Item $settingsFolder }
}
