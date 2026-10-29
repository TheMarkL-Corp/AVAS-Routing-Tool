Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase, System.Drawing

function Build-Visual-P1B([int]$size) {
    $scale = [double]$size / 128.0
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()

    $st = New-Object System.Windows.Media.ScaleTransform($scale, $scale)
    $dc.PushTransform($st)

    # 1. Background Tile (Gradient #0F172A to #1E293B with #38BDF8 30% border)
    $p0 = New-Object System.Windows.Point(0, 0)
    $p128 = New-Object System.Windows.Point(128, 128)
    $cTile1 = [System.Windows.Media.Color]::FromRgb(15, 23, 42)   # #0F172A
    $cTile2 = [System.Windows.Media.Color]::FromRgb(30, 41, 59)   # #1E293B
    $tileGrad = New-Object System.Windows.Media.LinearGradientBrush($cTile1, $cTile2, $p0, $p128)

    $bColor = [System.Windows.Media.Color]::FromArgb(76, 56, 189, 248) # #38BDF8 30%
    $tileBorderPen = New-Object System.Windows.Media.Pen((New-Object System.Windows.Media.SolidColorBrush($bColor)), 2.0)
    $dc.DrawRoundedRectangle($tileGrad, $tileBorderPen, (New-Object System.Windows.Rect(1.5, 1.5, 125, 125)), 28, 28)

    # 2. Upward Chevron "A" (Top half: #06B6D4 to #38BDF8)
    $pA1 = New-Object System.Windows.Point(32, 56)
    $pA2 = New-Object System.Windows.Point(64, 24)
    $cACyan = [System.Windows.Media.Color]::FromRgb(6, 182, 212)   # #06B6D4
    $cASky  = [System.Windows.Media.Color]::FromRgb(56, 189, 248)  # #38BDF8
    $aGrad = New-Object System.Windows.Media.LinearGradientBrush($cACyan, $cASky, $pA1, $pA2)
    $aPen = New-Object System.Windows.Media.Pen($aGrad, 8.0)
    $aPen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
    $aPen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $aPen.LineJoin = [System.Windows.Media.PenLineJoin]::Round

    $aGeo = New-Object System.Windows.Media.StreamGeometry
    $ctxA = $aGeo.Open()
    $ctxA.BeginFigure((New-Object System.Windows.Point(30, 60)), $false, $false)
    $ctxA.LineTo((New-Object System.Windows.Point(64, 26)), $true, $true)
    $ctxA.LineTo((New-Object System.Windows.Point(98, 60)), $true, $true)
    $ctxA.Close()
    $dc.DrawGeometry($null, $aPen, $aGeo)

    # 3. Downward Chevron "V" (Bottom half: #3B82F6 to #1D4ED8)
    $pV1 = New-Object System.Windows.Point(32, 72)
    $pV2 = New-Object System.Windows.Point(64, 104)
    $cVBlue1 = [System.Windows.Media.Color]::FromRgb(59, 130, 246)  # #3B82F6
    $cVBlue2 = [System.Windows.Media.Color]::FromRgb(29, 78, 216)   # #1D4ED8
    $vGrad = New-Object System.Windows.Media.LinearGradientBrush($cVBlue1, $cVBlue2, $pV1, $pV2)
    $vPen = New-Object System.Windows.Media.Pen($vGrad, 8.0)
    $vPen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
    $vPen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $vPen.LineJoin = [System.Windows.Media.PenLineJoin]::Round

    $vGeo = New-Object System.Windows.Media.StreamGeometry
    $ctxV = $vGeo.Open()
    $ctxV.BeginFigure((New-Object System.Windows.Point(30, 68)), $false, $false)
    $ctxV.LineTo((New-Object System.Windows.Point(64, 102)), $true, $true)
    $ctxV.LineTo((New-Object System.Windows.Point(98, 68)), $true, $true)
    $ctxV.Close()
    $dc.DrawGeometry($null, $vPen, $vGeo)

    # 4. Horizontal Routing Stream Bus (passing through center)
    # Left Input Stream (#06B6D4)
    $inPen = New-Object System.Windows.Media.Pen((New-Object System.Windows.Media.SolidColorBrush($cACyan)), 4.0)
    $inPen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
    $inPen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $dc.DrawLine($inPen, (New-Object System.Windows.Point(18, 64)), (New-Object System.Windows.Point(48, 64)))
    $dc.DrawEllipse((New-Object System.Windows.Media.SolidColorBrush($cACyan)), $null, (New-Object System.Windows.Point(18, 64)), 4.0, 4.0)

    # Right Output Stream (#60A5FA)
    $cOut = [System.Windows.Media.Color]::FromRgb(96, 165, 250) # #60A5FA
    $outPen = New-Object System.Windows.Media.Pen((New-Object System.Windows.Media.SolidColorBrush($cOut)), 4.0)
    $outPen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
    $outPen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $dc.DrawLine($outPen, (New-Object System.Windows.Point(80, 64)), (New-Object System.Windows.Point(110, 64)))
    $dc.DrawEllipse((New-Object System.Windows.Media.SolidColorBrush($cOut)), $null, (New-Object System.Windows.Point(110, 64)), 4.0, 4.0)

    # 5. Central Surgical Cross Core Node (#2563EB fill, #FFFFFF border & cross)
    $cCore = [System.Windows.Media.Color]::FromRgb(37, 99, 235)  # #2563EB
    $white = [System.Windows.Media.Color]::FromRgb(255, 255, 255) # #FFFFFF
    $whiteBrush = New-Object System.Windows.Media.SolidColorBrush($white)
    $coreBorder = New-Object System.Windows.Media.Pen($whiteBrush, 2.5)
    $dc.DrawRoundedRectangle((New-Object System.Windows.Media.SolidColorBrush($cCore)), $coreBorder, (New-Object System.Windows.Rect(52, 52, 24, 24)), 6, 6)

    # Inner Surgical Cross
    $crossPen = New-Object System.Windows.Media.Pen($whiteBrush, 3.0)
    $crossPen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
    $crossPen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $dc.DrawLine($crossPen, (New-Object System.Windows.Point(64, 57)), (New-Object System.Windows.Point(64, 71)))
    $dc.DrawLine($crossPen, (New-Object System.Windows.Point(57, 64)), (New-Object System.Windows.Point(71, 64)))

    # 6. Chevrons Apex Accents (White Dots)
    $dc.DrawEllipse($whiteBrush, $null, (New-Object System.Windows.Point(64, 26)), 4.0, 4.0)
    $dc.DrawEllipse($whiteBrush, $null, (New-Object System.Windows.Point(64, 102)), 4.0, 4.0)

    $dc.Pop() # Pop scale transform
    $dc.Close()

    return $dv
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngEntries = @()

foreach ($s in $sizes) {
    $visual = Build-Visual-P1B $s
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($s, $s, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($visual)

    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    $pngBytes = $ms.ToArray()
    $pngEntries += ,@($s, $pngBytes)

    if ($s -eq 256 -or $s -eq 32) {
        $previewPath = "C:\Users\POC-615\Documents\GitHub\AVAS-Routing-Tool\docs\icon_preview_${s}.png"
        [System.IO.File]::WriteAllBytes($previewPath, $pngBytes)
    }
}

# Assemble Multi-Resolution Windows ICO
$icoMs = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($icoMs)

# Header
$bw.Write([uint16]0) # Reserved
$bw.Write([uint16]1) # Type = 1 (Icon)
$bw.Write([uint16]$pngEntries.Count)

$offset = 6 + ($pngEntries.Count * 16)
foreach ($entry in $pngEntries) {
    $s = $entry[0]
    $data = $entry[1]
    $w = if ($s -ge 256) { [byte]0 } else { [byte]$s }
    $h = if ($s -ge 256) { [byte]0 } else { [byte]$s }
    $bw.Write($w)
    $bw.Write($h)
    $bw.Write([byte]0) # Color count
    $bw.Write([byte]0) # Reserved
    $bw.Write([uint16]1) # Planes
    $bw.Write([uint16]32) # Bit count
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$offset)
    $offset += $data.Length
}

foreach ($entry in $pngEntries) {
    $data = $entry[1]
    $bw.Write($data)
}

$bw.Flush()
$icoBytes = $icoMs.ToArray()

$targetIcoPath = "C:\Users\POC-615\Documents\GitHub\AVAS-Routing-Tool\src\AvasRoutingTool\Assets\AppIcon.ico"
[System.IO.File]::WriteAllBytes($targetIcoPath, $icoBytes)
Write-Output "Successfully generated P1B AppIcon.ico ($($icoBytes.Length) bytes)"

# Verify with IconBitmapDecoder
$readMs = New-Object System.IO.MemoryStream(,$icoBytes)
$decoder = New-Object System.Windows.Media.Imaging.IconBitmapDecoder($readMs, [System.Windows.Media.Imaging.BitmapCreateOptions]::None, [System.Windows.Media.Imaging.BitmapCacheOption]::Default)
Write-Output "IconBitmapDecoder verified $($decoder.Frames.Count) frames:"
foreach ($f in $decoder.Frames) {
    Write-Output " - $($f.PixelWidth)x$($f.PixelHeight)"
}
