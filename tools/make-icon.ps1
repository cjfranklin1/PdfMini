param(
  [string]$Out = "D:\Hermes\Codex\pdfmini\assets\app.ico",
  [string]$Preview = "D:\Hermes\Codex\pdfmini\assets\icon_preview.png"
)
Add-Type -AssemblyName System.Drawing

function Add-RoundedRect($path, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
  $d = $r * 2
  $path.AddArc($x, $y, $d, $d, 180, 90)
  $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
  $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
  $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
  $path.CloseFigure()
}

# 画一枚图标：深墨蓝圆角底 + 白纸（带折角）+ 朱红印章
function New-IconBitmap([int]$size) {
  $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.Clear([System.Drawing.Color]::Transparent)
  [float]$s = $size

  # 底色
  $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
  Add-RoundedRect $bg 0 0 $s $s ($s * 0.23)
  $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
  if ($size -ge 48) {
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 38, 68, 98)), ([System.Drawing.Color]::FromArgb(255, 17, 32, 48)), 90.0
  } else {
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 28, 52, 76))
  }
  $g.FillPath($brush, $bg)
  $brush.Dispose()

  # 一圈浅色描边，小尺寸下更容易分辨
  if ($size -ge 32) {
    $penEdge = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(60, 255, 255, 255)), ([float][Math]::Max(1.0, $s * 0.012))
    $g.DrawPath($penEdge, $bg)
    $penEdge.Dispose()
  }

  # 白纸（先画一层柔和投影，让纸从底上"浮"起来）
  [float]$px = $s * 0.245; [float]$py = $s * 0.185
  [float]$pw = $s * 0.50;  [float]$ph = $s * 0.63
  [float]$fold = $s * 0.155
  if ($size -ge 32) {
    $shadowPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-RoundedRect $shadowPath ($px + $s * 0.018) ($py + $s * 0.022) $pw $ph ($s * 0.012)
    $shadow = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(80, 0, 0, 0))
    $g.FillPath($shadow, $shadowPath)
    $shadow.Dispose(); $shadowPath.Dispose()
  }
  $paper = New-Object System.Drawing.Drawing2D.GraphicsPath
  $paper.AddLine($px, $py, ($px + $pw - $fold), $py)
  $paper.AddLine(($px + $pw - $fold), $py, ($px + $pw), ($py + $fold))
  $paper.AddLine(($px + $pw), ($py + $fold), ($px + $pw), ($py + $ph))
  $paper.AddLine(($px + $pw), ($py + $ph), $px, ($py + $ph))
  $paper.CloseFigure()
  $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 247, 249, 252))
  $g.FillPath($white, $paper)
  $white.Dispose()

  # 折角
  $corner = New-Object System.Drawing.Drawing2D.GraphicsPath
  $corner.AddLine(($px + $pw - $fold), $py, ($px + $pw - $fold), ($py + $fold))
  $corner.AddLine(($px + $pw - $fold), ($py + $fold), ($px + $pw), ($py + $fold))
  $corner.CloseFigure()
  $grey = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 176, 196, 214))
  $g.FillPath($grey, $corner)
  $grey.Dispose()

  # 纸上的三道墨线（文档感），小尺寸省略
  if ($size -ge 32) {
    $ink = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(120, 60, 78, 96)), ([float][Math]::Max(1.0, $s * 0.016))
    for ($i = 0; $i -lt 3; $i++) {
      $ly = $py + $s * (0.30 + 0.115 * $i)
      $g.DrawLine($ink, ($px + $s * 0.075), $ly, ($px + $pw - $s * 0.075), $ly)
    }
    $ink.Dispose()
  }

  # 朱红小印章（搭在纸右下角）
  [float]$ss = $s * 0.185
  [float]$sx = $s * 0.615; [float]$sy = $s * 0.595
  $seal = New-Object System.Drawing.Drawing2D.GraphicsPath
  Add-RoundedRect $seal $sx $sy $ss $ss ($s * 0.035)
  $red = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 182, 56, 44))
  $g.FillPath($red, $seal)
  $red.Dispose()
  if ($size -ge 48) {
    $ring = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(130, 255, 236, 232)), ([float]($s * 0.012))
    $g.DrawPath($ring, $seal)
    $ring.Dispose()
  }
  $seal.Dispose()
  $paper.Dispose()
  $corner.Dispose()
  $bg.Dispose()
  $g.Dispose()
  return $bmp
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($sz in $sizes) {
  $bmp = New-IconBitmap $sz
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $frames += , @{ size = $sz; bytes = $ms.ToArray() }
  $ms.Dispose()
  $bmp.Dispose()
}

# 组装 ICO（Vista+ 支持 PNG 帧）
$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
  $dim = $f.size; if ($dim -eq 256) { $dim = 0 }
  $bw.Write([Byte]$dim); $bw.Write([Byte]$dim)
  $bw.Write([Byte]0); $bw.Write([Byte]0)
  $bw.Write([UInt16]1); $bw.Write([UInt16]32)
  $bw.Write([UInt32]$f.bytes.Length)
  $bw.Write([UInt32]$offset)
  $offset += $f.bytes.Length
}
foreach ($f in $frames) { $bw.Write($f.bytes) }
$bw.Flush(); $bw.Close(); $fs.Close()

# 预览图：各尺寸放大到接近同一视觉高度，便于肉眼检查
$scaleFor = @{ 16 = 4; 24 = 4; 32 = 3; 48 = 2; 64 = 2; 128 = 1; 256 = 1 }
$pad = 18
$w = $pad
$h = 0
foreach ($f in $frames) {
  $sc = $scaleFor[[int]$f.size]
  $w += [int]($f.size * $sc) + $pad
  if (($f.size * $sc) -gt $h) { $h = [int]($f.size * $sc) }
}
$h += $pad * 2
$prev = New-Object System.Drawing.Bitmap $w, $h
$pg = [System.Drawing.Graphics]::FromImage($prev)
$pg.Clear([System.Drawing.Color]::FromArgb(255, 236, 238, 241))
$pg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$pg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$x = $pad
foreach ($f in $frames) {
  $sc = $scaleFor[[int]$f.size]
  $ms = [System.IO.MemoryStream]::new([byte[]]$f.bytes)
  $img = [System.Drawing.Image]::FromStream($ms)
  $dw = [int]($f.size * $sc)
  $pg.DrawImage($img, $x, $pad + ($h - $pad * 2 - $dw), $dw, $dw)
  $img.Dispose(); $ms.Dispose()
  $x += $dw + $pad
}
$pg.Dispose()
$prev.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png)
$prev.Dispose()

Write-Output ("ico=" + $Out + " bytes=" + (Get-Item $Out).Length + " frames=" + $frames.Count)
Write-Output ("preview=" + $Preview)
