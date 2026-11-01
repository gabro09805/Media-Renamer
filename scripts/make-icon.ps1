# Genera src\MediaRenamer.App\Assets\MediaRenamer.ico (ridisegnabile: modificare Draw).
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot "..\src\MediaRenamer.App\Assets\MediaRenamer.ico"

function RoundRect($x, $y, $w, $h, $r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}

function Draw($s) {
    $bmp = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.Clear([Drawing.Color]::Transparent)
    $u = $s / 100.0
    # sfondo azzurro a gradiente
    $bg = RoundRect (4 * $u) (4 * $u) (92 * $u) (92 * $u) (20 * $u)
    $br = New-Object Drawing.Drawing2D.LinearGradientBrush ([Drawing.PointF]::new(0, 0)), ([Drawing.PointF]::new($s, $s)), ([Drawing.Color]::FromArgb(90, 190, 255)), ([Drawing.Color]::FromArgb(47, 100, 224))
    $g.FillPath($br, $bg)
    # schermo: cornice bianca e vetro blu con play
    $g.FillPath([Drawing.Brushes]::White, (RoundRect (14 * $u) (16 * $u) (72 * $u) (48 * $u) (7 * $u)))
    $gl = New-Object Drawing.Drawing2D.LinearGradientBrush ([Drawing.PointF]::new(0, 20 * $u)), ([Drawing.PointF]::new(0, 60 * $u)), ([Drawing.Color]::FromArgb(60, 140, 240)), ([Drawing.Color]::FromArgb(30, 80, 190))
    $g.FillPath($gl, (RoundRect (18 * $u) (20 * $u) (64 * $u) (40 * $u) (4 * $u)))
    $tri = [Drawing.PointF]::new(43 * $u, 28 * $u), [Drawing.PointF]::new(43 * $u, 52 * $u), [Drawing.PointF]::new(63 * $u, 40 * $u)
    $g.FillPolygon([Drawing.Brushes]::White, $tri)
    # etichetta con nome rinominato in arancione e cursore di testo
    $g.FillPath([Drawing.Brushes]::White, (RoundRect (22 * $u) (71 * $u) (56 * $u) (18 * $u) (9 * $u)))
    $or = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 140, 30))
    $g.FillPath($or, (RoundRect (29 * $u) (77 * $u) (28 * $u) (6 * $u) (3 * $u)))
    $g.FillPath($or, (RoundRect (61 * $u) (74 * $u) (4 * $u) (12 * $u) (2 * $u)))
    $g.Dispose(); return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$png = foreach ($s in $sizes) {
    $b = Draw $s; $ms = New-Object IO.MemoryStream
    $b.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $b.Dispose(); ,$ms.ToArray()
}
$fs = [IO.File]::Create($out); $w = New-Object IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$off = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$d); $w.Write([byte]$d); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$png[$i].Length); $w.Write([uint32]$off)
    $off += $png[$i].Length
}
foreach ($p in $png) { $w.Write($p) }
$w.Close(); $fs.Close()
Write-Host "Creato $out"
