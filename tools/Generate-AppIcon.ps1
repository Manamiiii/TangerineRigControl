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
    $background = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(28,32,38))
    $orange = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255,151,48)),25
    $orange.StartCap = 'Round'; $orange.EndCap = 'Round'
    $leaf = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(192,239,80))
    $graphics.FillEllipse($background,4,4,248,248)
    $graphics.DrawArc($orange,52,64,152,152,220,-260)
    $graphics.DrawLine($orange,128,66,128,131)
    $leafShape = New-Object System.Drawing.Drawing2D.GraphicsPath
    $leafShape.AddBezier(143,59,152,24,190,24,207,29)
    $leafShape.AddBezier(207,29,194,63,166,76,143,59)
    $graphics.FillPath($leaf,$leafShape)
    $resized = New-Object System.Drawing.Bitmap $size,$size
    $scaled = [System.Drawing.Graphics]::FromImage($resized)
    $scaled.InterpolationMode = 'HighQualityBicubic'
    $scaled.DrawImage($bitmap,0,0,$size,$size)
    $stream = New-Object System.IO.MemoryStream
    $resized.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,@{ Size=$size; Bytes=$stream.ToArray() }
    if ($size -eq 256) { $resized.Save((Join-Path $assetDirectory 'app-preview.png'),[System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $scaled.Dispose(); $resized.Dispose(); $leafShape.Dispose()
    $leaf.Dispose(); $orange.Dispose(); $background.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
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
