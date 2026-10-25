<#
.SYNOPSIS
  Draws the application icon (src\TGAPeek.App\TGAPeek.ico): red, green and blue quadrants and a
  checkerboard quadrant (transparency) on a dark rounded square, at 16-256 pixels.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/MakeIcon.ps1
#>
param([string]$Output = (Join-Path $PSScriptRoot "..\src\TGAPeek.App\TGAPeek.ico"))

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function Draw-Frame([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $radius = [Math]::Max(2, [int]($size / 6))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d - 1, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d - 1, $size - $d - 1, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d - 1, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 43, 43, 43))), $path)

    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
    $margin = [Math]::Max(2, [int]($size / 8))
    $half = [int](($size - 2 * $margin) / 2)
    $x0 = $margin; $y0 = $margin; $x1 = $margin + $half; $y1 = $margin + $half
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 230, 40, 40))), $x0, $y0, $half, $half)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 60, 200, 70))), $x1, $y0, $half, $half)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 50, 110, 235))), $x0, $y1, $half, $half)

    $cell = [Math]::Max(1, [int]($half / 4))
    for ($cy = 0; $cy -lt $half; $cy += $cell) {
        for ($cx = 0; $cx -lt $half; $cx += $cell) {
            $light = ((([int]($cx / $cell)) + ([int]($cy / $cell))) % 2) -eq 0
            $color = if ($light) { [System.Drawing.Color]::White } else { [System.Drawing.Color]::FromArgb(255, 190, 190, 190) }
            $w = [Math]::Min($cell, $half - $cx); $h = [Math]::Min($cell, $half - $cy)
            $g.FillRectangle((New-Object System.Drawing.SolidBrush $color), $x1 + $cx, $y1 + $cy, $w, $h)
        }
    }

    $g.Dispose()
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return ,$stream.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = @($sizes | ForEach-Object { ,(Draw-Frame $_) })

$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $out
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $data = $frames[$i]
    $writer.Write([byte]($s % 256)); $writer.Write([byte]($s % 256))
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32)
    $writer.Write([UInt32]$data.Length); $writer.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $frames) { $writer.Write($data) }
$writer.Flush()
[System.IO.File]::WriteAllBytes((Join-Path (Resolve-Path (Split-Path $Output)) (Split-Path $Output -Leaf)), $out.ToArray())
Write-Host "Wrote $Output ($($out.Length) bytes, sizes $($sizes -join ', '))"
