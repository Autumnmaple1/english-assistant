# Rebuild the Windows icon from the same geometry as Assets/ScreenEnglish.svg.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetPath = Join-Path $PSScriptRoot 'ScreenEnglish/Assets'
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform($size / 256.0, $size / 256.0)
    $background = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#242725'))
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc(8,8,112,112,180,90); $path.AddArc(136,8,112,112,270,90)
    $path.AddArc(136,136,112,112,0,90); $path.AddArc(8,136,112,112,90,90); $path.CloseFigure()
    $g.FillPath($background,$path); $path.Dispose()
    $pen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#F3F2EB'),12)
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $book = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $book.AddBezier(128,78,107,64,81,62,56,68); $book.AddLine(56,68,56,172)
    $book.AddBezier(56,172,83,166,107,170,128,186); $book.AddBezier(128,186,149,170,173,166,200,172)
    $book.AddLine(200,172,200,68); $book.AddBezier(200,68,175,62,149,64,128,78); $book.CloseFigure()
    $g.DrawPath($pen,$book); $g.DrawLine($pen,128,80,128,181)
    $accent = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#A9C3AF'),9)
    $accent.StartCap = $accent.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    if ($size -ge 24) { $g.DrawLine($accent,155,101,178,101); $g.DrawLine($accent,155,124,173,124) }
    $stream = [System.IO.MemoryStream]::new(); $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,$stream.ToArray()
    if ($size -eq 256) { $bitmap.Save((Join-Path $assetPath 'ScreenEnglish.png'),[System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $accent.Dispose(); $book.Dispose(); $pen.Dispose(); $background.Dispose(); $g.Dispose(); $bitmap.Dispose()
}
$file = [System.IO.File]::Create((Join-Path $assetPath 'ScreenEnglish.ico'))
$writer = [System.IO.BinaryWriter]::new($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i=0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
$writer.Dispose()


