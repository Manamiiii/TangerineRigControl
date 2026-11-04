# SPDX-License-Identifier: GPL-3.0-only
# Copyright (c) 2026 Manamiiii

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path $PSScriptRoot '../src/TangerineRigControl/Assets'
$frames = @()
foreach ($size in @(16, 20, 24, 32, 48, 64, 128, 256)) {
    $bitmap = New-Object System.Drawing.Bitmap 256,256
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = 'AntiAlias'
    $bounds = New-Object System.Drawing.Drawing2D.GraphicsPath
    foreach ($arc in @(@(2,2,180),@(176,2,270),@(176,176,0),@(2,176,90))) {
        $bounds.AddArc($arc[0],$arc[1],78,78,$arc[2],90)
    }
    $bounds.CloseFigure()
    $background = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([Drawing.Rectangle]::new(0,0,256,256)),([Drawing.Color]::FromArgb(255,148,17)),([Drawing.Color]::FromArgb(241,110,0)),70
    $graphics.FillPath($background,$bounds)
    $ink = [Drawing.Color]::FromArgb(41,22,9)
    $outline = New-Object Drawing.Pen $ink,10
    $outline.StartCap = 'Round'; $outline.EndCap = 'Round'; $outline.LineJoin = 'Round'
    $graphics.DrawEllipse($outline,37,57,182,177)
    $graphics.DrawBezier($outline,125,60,120,46,119,31,129,20)
    $leaf = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(152,192,66))
    $leafShape = New-Object Drawing.Drawing2D.GraphicsPath
    $leafShape.AddBezier(126,58,138,24,172,15,203,24)
    $leafShape.AddBezier(203,24,186,55,160,68,126,58)
    $graphics.FillPath($leaf,$leafShape); $graphics.DrawPath($outline,$leafShape)
    $graphics.DrawBezier($outline,129,57,141,49,151,45,161,41)
    # Power control inside the fruit silhouette.
    $outline.Width = 14
    $graphics.DrawArc($outline,83,102,90,90,-51,282)
    $graphics.DrawLine($outline,128,93,128,144)
    $resized = New-Object System.Drawing.Bitmap $size,$size
    $scaled = [System.Drawing.Graphics]::FromImage($resized)
    $scaled.InterpolationMode = 'HighQualityBicubic'
    $scaled.DrawImage($bitmap,0,0,$size,$size)
    $stream = New-Object System.IO.MemoryStream
    $resized.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,@{ Size=$size; Bytes=$stream.ToArray() }
    if ($size -eq 256) { $resized.Save((Join-Path $assetDirectory 'app-preview.png'),[System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $scaled.Dispose(); $resized.Dispose(); $leafShape.Dispose()
    $leaf.Dispose(); $bounds.Dispose(); $outline.Dispose(); $background.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$file = [IO.File]::Create((Join-Path $assetDirectory 'app.ico'))
$writer = New-Object IO.BinaryWriter $file
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) {0} else {$frame.Size}
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose() }
