# Draws app.ico: green rounded tile, a 3x3 sheet grid, the middle row hidden behind a mask bar.
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\src\SheetMask.App\app.ico'
$sizes = 16, 32, 48, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $r = [math]::Max(2, [int]($s * 0.2))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $w = $s - 1
    $path.AddArc(0, 0, $r, $r, 180, 90); $path.AddArc($w - $r, 0, $r, $r, 270, 90)
    $path.AddArc($w - $r, $w - $r, $r, $r, 0, 90); $path.AddArc(0, $w - $r, $r, $r, 90, 90); $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(31, 107, 85))), $path)
    $m = $s * 0.2; $cell = ($s - 2 * $m) / 3; $gap = [math]::Max(1, $s * 0.035)
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(236, 246, 241))
    for ($row = 0; $row -lt 3; $row++) {
        for ($col = 0; $col -lt 3; $col++) {
            if ($row -eq 1) { continue }
            $g.FillRectangle($white, [float]($m + $col * $cell + $gap / 2), [float]($m + $row * $cell + $gap / 2), [float]($cell - $gap), [float]($cell - $gap))
        }
    }
    $amber = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(242, 178, 82))
    $g.FillRectangle($amber, [float]($m - $gap), [float]($m + $cell + $gap / 2), [float](3 * $cell + 2 * $gap), [float]($cell - $gap))
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $bmp.Dispose()
}
# ICO container with PNG entries
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset); $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
"icon written: $out"
