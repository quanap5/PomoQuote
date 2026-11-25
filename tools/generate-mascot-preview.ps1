<#
.SYNOPSIS
  Draws Assets/Mascot/preview-characters.png: the four characters (robot, animal, man, woman) on a dark and on a light taskbar colour.

.DESCRIPTION
    powershell -File tools/generate-mascot-preview.ps1

  Reads the four sheets written by generate-mascot-sprites.ps1 and shows, for each character, a few frames of idle, walk, run, jump, sit, sleep, wave,
  celebrate, sad and drag, enlarged 6 times with nearest-neighbour scaling. The top block has a dark background, the bottom one a light background.
#>
param([string]$Dir = (Join-Path $PSScriptRoot '..\Assets\Mascot'))
Add-Type -AssemblyName System.Drawing

$cell = 24; $zoom = 5; $gap = 8; $labelW = 72
# row in the sheet, frame in the row: what to show for each character
$picks = @(@(0, 0), @(2, 0), @(2, 3), @(3, 1), @(4, 3), @(6, 1), @(7, 0), @(8, 1), @(9, 1), @(11, 1), @(12, 0))
$characters = 'robot', 'animal', 'man', 'woman'
$backgrounds = @([System.Drawing.Color]::FromArgb(255, 32, 32, 32), [System.Drawing.Color]::FromArgb(255, 243, 243, 243))
$block = $characters.Count * ($cell * $zoom + $gap) + $gap
$width = $labelW + $picks.Count * ($cell * $zoom + $gap) + $gap
$image = New-Object System.Drawing.Bitmap $width, ($block * 2)
$g = [System.Drawing.Graphics]::FromImage($image)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$font = New-Object System.Drawing.Font('Segoe UI', 13, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)

for ($b = 0; $b -lt 2; $b++) {
  $bg = $backgrounds[$b]
  $g.FillRectangle((New-Object System.Drawing.SolidBrush $bg), 0, $b * $block, $width, $block)
  $text = if ($b -eq 0) { [System.Drawing.Brushes]::White } else { [System.Drawing.Brushes]::Black }
  for ($c = 0; $c -lt $characters.Count; $c++) {
    $sheet = [System.Drawing.Image]::FromFile((Resolve-Path (Join-Path $Dir "$($characters[$c])-sprites.png")))
    $y0 = $b * $block + $gap + $c * ($cell * $zoom + $gap)
    $g.DrawString((Get-Culture).TextInfo.ToTitleCase($characters[$c]), $font, $text, 8, $y0 + ($cell * $zoom) / 2 - 8)
    for ($i = 0; $i -lt $picks.Count; $i++) {
      $x0 = $labelW + $i * ($cell * $zoom + $gap)
      $dest = New-Object System.Drawing.Rectangle $x0, $y0, ($cell * $zoom), ($cell * $zoom)
      $src = New-Object System.Drawing.Rectangle ($picks[$i][1] * $cell), ($picks[$i][0] * $cell), $cell, $cell
      $g.DrawImage($sheet, $dest, $src, [System.Drawing.GraphicsUnit]::Pixel)
    }
    $sheet.Dispose()
  }
}

$out = Join-Path $Dir 'preview-characters.png'
$image.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $image.Dispose()
"Wrote $out"