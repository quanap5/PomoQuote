<#
.SYNOPSIS
  Generates the mascot's pixel-art sprite sheet and a labelled contact sheet.

.DESCRIPTION
  The robot is original artwork, drawn here as code so every frame can be edited and reproduced:
  change a pose below, run the script, commit the two PNGs.

    pwsh/powershell -File tools/generate-mascot-sprites.ps1

  Output (in Assets/Mascot): robot-sprites.png (the sheet the app uses) and contact-sheet.png (preview).
  Grid: 24x24 px cells, one cell per frame, 8 columns; each animation is one row. The robot faces right;
  the app mirrors frames for the left. Row order and frame counts must match Assets/Mascot/mascot-config.json.
#>
param(
  [string]$OutDir = (Join-Path $PSScriptRoot '..\Assets\Mascot')
)
Add-Type -AssemblyName System.Drawing

$cell = 24
$columns = 8

# One palette for every frame (index -> colour). '.' is transparent.
$pal = @{
  'K' = [System.Drawing.Color]::FromArgb(255, 28, 32, 48)     # outline
  'B' = [System.Drawing.Color]::FromArgb(255, 96, 140, 196)   # body
  'D' = [System.Drawing.Color]::FromArgb(255, 62, 98, 150)    # body shade
  'L' = [System.Drawing.Color]::FromArgb(255, 176, 206, 238)  # face
  'Y' = [System.Drawing.Color]::FromArgb(255, 255, 214, 64)   # lights / eyes
  'R' = [System.Drawing.Color]::FromArgb(255, 232, 76, 68)    # mouth / alert
  'W' = [System.Drawing.Color]::FromArgb(255, 250, 250, 250)  # white
  'C' = [System.Drawing.Color]::FromArgb(255, 120, 200, 255)  # tear / sparkle
  'G' = [System.Drawing.Color]::FromArgb(255, 120, 220, 140)  # confetti
}

$script:buf = $null
function Clear-Cell { $script:buf = New-Object 'string[,]' $cell, $cell; for ($x = 0; $x -lt $cell; $x++) { for ($y = 0; $y -lt $cell; $y++) { $script:buf[$x, $y] = '.' } } }
function Px($x, $y, $c) { if ($x -ge 0 -and $x -lt $cell -and $y -ge 0 -and $y -lt $cell) { $script:buf[$x, $y] = $c } }
function Rc($x, $y, $w, $h, $c) { for ($i = 0; $i -lt $w; $i++) { for ($j = 0; $j -lt $h; $j++) { Px ($x + $i) ($y + $j) $c } } }
# A box with an outline and a fill.
function Box($x, $y, $w, $h, $fill) { Rc $x $y $w $h 'K'; if ($w -gt 2 -and $h -gt 2) { Rc ($x + 1) ($y + 1) ($w - 2) ($h - 2) $fill } }

# Draws one robot. All keys are optional.
#   by    body+head drop in px (negative = up)     hx  head shift     hy  extra head drop
#   ant   antenna bulb shift (-1..1)                eyes  o open, b blink, c closed, w wide, s sad, h happy, l left, r right
#   mouth - none, s smile, o small o, O big, f frown
#   a/b   leg x shift (left/right leg)              la/lb  leg lift in px
#   al/ar arm rect override @(x,y,w,h) in body coordinates (before 'by')   sw  arm swing -1..1
#   fx    extra pixels @( @(x,y,'C'), ... )  absolute
function Robot($p) {
  $by = [int]($p.by); $hx = 1 + [int]($p.hx); $hy = [int]($p.hy); $ant = [int]($p.ant)
  $headTop = 6 + $by + $hy
  # legs (feet stay on row 23; a lifted leg is shorter)
  $legTopBase = 20
  $a = [int]($p.a); $b = [int]($p.b); $la = [int]($p.la); $lb = [int]($p.lb)
  $lh = 4
  if ($p.legs -eq 'sit') {
    Box 10 21 6 3 'B'; Box 15 21 6 3 'B'
  } elseif ($p.legs -eq 'dangle') {
    $sw = [int]($p.sw)
    Box (8 + $sw) (19 + $by) 3 4 'B'; Box (13 - $sw) (19 + $by) 3 4 'B'
  } else {
    $hL = [Math]::Max(1, $lh - $la - [Math]::Max(0, $by)); $hR = [Math]::Max(1, $lh - $lb - [Math]::Max(0, $by))
    if ($hL -lt 2) { Rc (8 + $a) (24 - $hL) 3 $hL 'K' } else { Box (8 + $a) (24 - $hL) 3 $hL 'B' }
    if ($hR -lt 2) { Rc (13 + $b) (24 - $hR) 3 $hR 'K' } else { Box (13 + $b) (24 - $hR) 3 $hR 'B' }
  }
  # arms (drawn before the body so the torso overlaps the shoulder)
  $sw = [int]($p.sw)
  $al = if ($p.al) { $p.al } else { @(5, 15, 2, 4) }
  $ar = if ($p.ar) { $p.ar } else { @(17, 15, 2, 4) }
  Rc ($al[0] + $sw) ($al[1] + $by) $al[2] $al[3] 'K'; Px ($al[0] + $sw) ($al[1] + $by) 'B'
  Rc ($ar[0] - $sw) ($ar[1] + $by) $ar[2] $ar[3] 'K'; Px ($ar[0] - $sw + $ar[2] - 1) ($ar[1] + $by) 'B'
  # body
  Box 7 (14 + $by) 10 6 'B'
  Rc 8 (18 + $by) 8 1 'D'
  Rc 11 (15 + $by) 2 2 'Y'
  # antenna
  $ax = 11 + $ant + $p.hx
  Rc ($ax + 0) (3 + $by + $hy + [int]($p.antdy)) 2 1 'Y'
  Rc 11 (4 + $by + $hy) 2 2 'K'
  # head
  Box (6 + $hx - 1) $headTop 12 8 'L'
  # eyes
  $ex = $hx - 1  # eyes shift with the head
  $ey = $headTop + 3
  $lx = 8 + $ex + 1; $rx = 13 + $ex + 1
  switch ($p.eyes) {
    'b' { Rc $lx ($ey + 1) 2 1 'K'; Rc $rx ($ey + 1) 2 1 'K' }
    'c' { Rc $lx ($ey + 1) 2 1 'K'; Rc $rx ($ey + 1) 2 1 'K' }
    'w' { Rc ($lx - 1) ($ey - 1) 3 3 'W'; Rc ($rx - 0) ($ey - 1) 3 3 'W'; Px ($lx + 1) $ey 'K'; Px ($rx + 1) $ey 'K'; Px $lx ($ey - 1) 'K'; Px ($rx) ($ey - 1) 'K' }
    's' { Rc $lx $ey 2 2 'Y'; Rc $rx $ey 2 2 'Y'; Px $lx ($ey - 1) 'K'; Px ($lx + 1) ($ey) 'K'; Px ($rx + 1) ($ey - 1) 'K'; Px $rx ($ey) 'K' }
    'h' { Px $lx ($ey + 1) 'K'; Px ($lx + 1) $ey 'K'; Px ($lx + 2) ($ey + 1) 'K'; Px $rx ($ey + 1) 'K'; Px ($rx + 1) $ey 'K'; Px ($rx + 2) ($ey + 1) 'K' }
    'l' { Rc $lx $ey 2 2 'Y'; Rc $rx $ey 2 2 'Y'; Px $lx ($ey + 1) 'K'; Px $rx ($ey + 1) 'K' }
    'r' { Rc $lx $ey 2 2 'Y'; Rc $rx $ey 2 2 'Y'; Px ($lx + 1) ($ey + 1) 'K'; Px ($rx + 1) ($ey + 1) 'K' }
    default { Rc $lx $ey 2 2 'Y'; Rc $rx $ey 2 2 'Y'; Px ($lx + 1) ($ey + 1) 'K'; Px ($rx + 1) ($ey + 1) 'K' }
  }
  # mouth
  $mx = 11 + $ex; $my = $headTop + 6
  switch ($p.mouth) {
    's' { Rc $mx $my 3 1 'R' }
    'o' { Rc ($mx + 1) $my 1 1 'R' }
    'O' { Rc $mx $my 3 2 'R'; Px ($mx + 1) ($my + 1) 'K' }
    'f' { Rc $mx ($my + 0) 3 1 'R'; Px ($mx - 1) ($my + 1) 'R'; Px ($mx + 3) ($my + 1) 'R' }
    default { }
  }
  # extras
  foreach ($e in $p.fx) { Px $e[0] $e[1] $e[2] }
}

# Animation table: name, frames. The order is the row order in the sheet.
$anims = [ordered]@{}

$anims['idle'] = @(
  @{ },
  @{ ant = 1 },
  @{ by = 0; eyes = 'b' },
  @{ ant = -1; hy = 0 }
)
$anims['look'] = @(
  @{ },
  @{ hx = 1; eyes = 'r' },
  @{ hx = -1; eyes = 'l' },
  @{ eyes = 'b' }
)
$anims['walk'] = @(
  @{ a = -2; b = 2; by = 1; sw = 1 },
  @{ a = -1; b = 1; lb = 1; sw = 1 },
  @{ a = 0; b = 0; lb = 2; sw = 0 },
  @{ a = 2; b = -2; by = 1; sw = -1 },
  @{ a = 1; b = -1; la = 1; sw = -1 },
  @{ a = 0; b = 0; la = 2; sw = 0 }
)
$anims['run'] = @(
  @{ a = -3; b = 3; by = 1; hx = 1; sw = 2; al = @(5, 14, 2, 3); ar = @(17, 14, 2, 3) },
  @{ a = -1; b = 1; lb = 1; by = 0; hx = 1; sw = 1; al = @(5, 14, 2, 3); ar = @(17, 14, 2, 3) },
  @{ a = 1; b = -1; la = 1; lb = 1; by = -1; hx = 1; sw = 0; al = @(5, 14, 2, 3); ar = @(17, 14, 2, 3) },
  @{ a = 3; b = -3; by = 1; hx = 1; sw = -2; al = @(5, 14, 2, 3); ar = @(17, 14, 2, 3) },
  @{ a = 1; b = -1; la = 1; by = 0; hx = 1; sw = -1; al = @(5, 14, 2, 3); ar = @(17, 14, 2, 3) },
  @{ a = -1; b = 1; la = 1; lb = 1; by = -1; hx = 1; sw = 0; al = @(5, 14, 2, 3); ar = @(17, 14, 2, 3) }
)
$anims['jump'] = @(
  @{ by = 2; a = -1; b = 1; al = @(5, 16, 2, 3); ar = @(17, 16, 2, 3); mouth = 's' },
  @{ by = 3; a = -2; b = 2; al = @(4, 16, 2, 3); ar = @(18, 16, 2, 3); mouth = 's'; eyes = 'b' },
  @{ by = -1; la = 0; lb = 0; al = @(5, 11, 2, 5); ar = @(17, 11, 2, 5); eyes = 'w'; mouth = 'o' },
  @{ by = -1; la = 1; lb = 1; al = @(4, 11, 2, 5); ar = @(18, 11, 2, 5); eyes = 'w'; mouth = 'o' },
  @{ by = 0; la = 2; lb = 2; al = @(4, 13, 2, 4); ar = @(18, 13, 2, 4); mouth = 'o' },
  @{ by = -1; la = 0; lb = 0; al = @(5, 12, 2, 5); ar = @(17, 12, 2, 5); mouth = 'o'; hy = 0 }
)
$anims['land'] = @(
  @{ by = 3; a = -2; b = 2; al = @(4, 16, 2, 3); ar = @(18, 16, 2, 3); eyes = 'b' },
  @{ by = 1; a = -1; b = 1; al = @(5, 15, 2, 4); ar = @(17, 15, 2, 4) },
  @{ }
)
$anims['sit'] = @(
  @{ by = 2; a = -1; b = 1 },
  @{ by = 3; legs = 'sit'; al = @(5, 16, 2, 3); ar = @(17, 16, 2, 3) },
  @{ by = 3; legs = 'sit'; eyes = 'b'; al = @(5, 16, 2, 3); ar = @(17, 16, 2, 3) },
  @{ by = 3; legs = 'sit'; ant = 1; al = @(5, 16, 2, 3); ar = @(17, 16, 2, 3) }
)
$anims['sleep'] = @(
  @{ by = 3; hy = 1; legs = 'sit'; eyes = 'c'; mouth = 'o'; al = @(5, 16, 2, 3); ar = @(17, 16, 2, 3); fx = @(@(19, 9, 'W'), @(20, 9, 'W'), @(19, 10, 'W'), @(20, 11, 'W')) },
  @{ by = 3; hy = 1; legs = 'sit'; eyes = 'c'; al = @(5, 16, 2, 3); ar = @(17, 16, 2, 3); fx = @(@(19, 7, 'W'), @(20, 7, 'W'), @(20, 8, 'W'), @(19, 9, 'W'), @(20, 9, 'W'), @(21, 4, 'W'), @(22, 4, 'W'), @(22, 5, 'W')) },
  @{ by = 3; hy = 1; legs = 'sit'; eyes = 'c'; mouth = 'o'; al = @(5, 16, 2, 3); ar = @(17, 16, 2, 3); fx = @(@(20, 5, 'W'), @(21, 5, 'W'), @(21, 6, 'W'), @(20, 7, 'W'), @(21, 7, 'W'), @(22, 2, 'W'), @(23, 2, 'W')) },
  @{ by = 3; hy = 1; legs = 'sit'; eyes = 'c'; al = @(5, 16, 2, 3); ar = @(17, 16, 2, 3); fx = @(@(21, 3, 'W'), @(22, 3, 'W'), @(22, 4, 'W'), @(21, 5, 'W'), @(22, 5, 'W')) }
)
$anims['wave'] = @(
  @{ eyes = 'h'; mouth = 's'; ar = @(17, 11, 2, 5) },
  @{ eyes = 'h'; mouth = 's'; ar = @(19, 10, 2, 4); fx = @(@(21, 9, 'Y')) },
  @{ eyes = 'h'; mouth = 's'; ar = @(17, 10, 2, 5) },
  @{ eyes = 'h'; mouth = 's'; ar = @(19, 10, 2, 4); fx = @(@(21, 9, 'Y')) },
  @{ eyes = 'h'; mouth = 's'; ar = @(17, 10, 2, 5) },
  @{ eyes = 'o'; mouth = 's'; ar = @(17, 13, 2, 4) }
)
$anims['celebrate'] = @(
  @{ by = 2; a = -1; b = 1; eyes = 'h'; mouth = 'O'; al = @(5, 12, 2, 3); ar = @(17, 12, 2, 3) },
  @{ by = -2; eyes = 'h'; mouth = 'O'; al = @(4, 9, 2, 6); ar = @(18, 9, 2, 6); fx = @(@(3, 6, 'Y'), @(20, 5, 'C'), @(2, 12, 'G'), @(21, 11, 'R')) },
  @{ by = -2; la = 1; lb = 1; eyes = 'h'; mouth = 'O'; al = @(4, 9, 2, 6); ar = @(18, 9, 2, 6); fx = @(@(4, 3, 'G'), @(19, 2, 'Y'), @(1, 9, 'R'), @(22, 8, 'C')) },
  @{ by = 1; eyes = 'h'; mouth = 'O'; al = @(5, 12, 2, 3); ar = @(17, 12, 2, 3); fx = @(@(3, 9, 'C'), @(21, 7, 'G')) },
  @{ by = -2; eyes = 'h'; mouth = 'O'; al = @(4, 9, 2, 6); ar = @(18, 9, 2, 6); fx = @(@(2, 5, 'R'), @(21, 3, 'G'), @(3, 11, 'Y'), @(22, 9, 'C')) },
  @{ by = 0; eyes = 'h'; mouth = 's'; al = @(4, 11, 2, 5); ar = @(18, 11, 2, 5) }
)
$anims['surprised'] = @(
  @{ by = -1; eyes = 'w'; mouth = 'o'; al = @(4, 13, 2, 4); ar = @(18, 13, 2, 4); fx = @(@(11, 0, 'R'), @(12, 0, 'R'), @(11, 1, 'R'), @(12, 1, 'R'), @(11, 3, 'R'), @(12, 3, 'R')) },
  @{ by = 0; hx = -1; eyes = 'w'; mouth = 'o'; a = 1; b = 1; al = @(4, 13, 2, 4); ar = @(18, 13, 2, 4); fx = @(@(11, 0, 'R'), @(12, 0, 'R'), @(11, 1, 'R'), @(12, 1, 'R'), @(11, 3, 'R'), @(12, 3, 'R')) },
  @{ by = 0; hx = -1; eyes = 'w'; mouth = 'o'; a = 1; b = 1; al = @(4, 13, 2, 4); ar = @(18, 13, 2, 4); fx = @(@(11, 1, 'R'), @(12, 1, 'R'), @(11, 2, 'R'), @(12, 2, 'R'), @(11, 4, 'R'), @(12, 4, 'R')) },
  @{ by = 0; eyes = 'o'; mouth = 'o' }
)
$anims['sad'] = @(
  @{ by = 1; hy = 1; eyes = 's'; mouth = 'f'; antdy = 1; ant = -1; al = @(6, 16, 2, 4); ar = @(16, 16, 2, 4) },
  @{ by = 1; hy = 1; eyes = 's'; mouth = 'f'; antdy = 1; ant = -1; al = @(6, 16, 2, 4); ar = @(16, 16, 2, 4); fx = @(@(10, 13, 'C')) },
  @{ by = 1; hy = 1; eyes = 's'; mouth = 'f'; antdy = 1; ant = -1; al = @(6, 16, 2, 4); ar = @(16, 16, 2, 4); fx = @(@(10, 15, 'C')) },
  @{ by = 2; hy = 1; eyes = 's'; mouth = 'f'; antdy = 1; ant = -1; al = @(6, 16, 2, 4); ar = @(16, 16, 2, 4); fx = @(@(10, 18, 'C')) }
)
$anims['drag'] = @(
  @{ by = -1; legs = 'dangle'; sw = 1; eyes = 'w'; mouth = 'o'; al = @(4, 11, 2, 5); ar = @(18, 11, 2, 5) },
  @{ by = -1; legs = 'dangle'; sw = 0; eyes = 'w'; mouth = 'o'; al = @(5, 11, 2, 5); ar = @(17, 11, 2, 5) },
  @{ by = -1; legs = 'dangle'; sw = -1; eyes = 'w'; mouth = 'o'; al = @(4, 11, 2, 5); ar = @(18, 11, 2, 5) },
  @{ by = -1; legs = 'dangle'; sw = 0; eyes = 'w'; mouth = 'o'; al = @(5, 11, 2, 5); ar = @(17, 11, 2, 5) }
)
$anims['shout'] = @(
  @{ by = 0; eyes = 'o'; mouth = 'O'; al = @(4, 10, 2, 6); ar = @(18, 10, 2, 6); fx = @(@(1, 8, 'R'), @(1, 9, 'R'), @(22, 8, 'R'), @(22, 9, 'R')) },
  @{ by = -1; eyes = 'o'; mouth = 'O'; al = @(4, 10, 2, 6); ar = @(18, 10, 2, 6); fx = @(@(0, 6, 'R'), @(0, 7, 'R'), @(23, 6, 'R'), @(23, 7, 'R'), @(1, 11, 'R'), @(22, 11, 'R')) }
)

$rows = $anims.Count
$sheet = New-Object System.Drawing.Bitmap ($columns * $cell), ($rows * $cell), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
for ($x = 0; $x -lt $sheet.Width; $x++) { for ($y = 0; $y -lt $sheet.Height; $y++) { $sheet.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, 0, 0, 0)) } }

$row = 0
foreach ($name in $anims.Keys) {
  $col = 0
  foreach ($frame in $anims[$name]) {
    Clear-Cell
    Robot $frame
    for ($x = 0; $x -lt $cell; $x++) { for ($y = 0; $y -lt $cell; $y++) { $c = $script:buf[$x, $y]; if ($c -ne '.') { $sheet.SetPixel($col * $cell + $x, $row * $cell + $y, $pal[$c]) } } }
    $col++
  }
  $row++
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$sheetPath = Join-Path $OutDir 'robot-sprites.png'
$sheet.Save($sheetPath, [System.Drawing.Imaging.ImageFormat]::Png)

# Contact sheet: every animation on a labelled row, enlarged with nearest-neighbour, light and dark backgrounds.
$zoom = 4
$labelW = 110
$pad = 6
$cw = $labelW + $columns * ($cell * $zoom + $pad) + $pad
$ch = $rows * ($cell * $zoom + $pad) + $pad
$contact = New-Object System.Drawing.Bitmap $cw, $ch
$g = [System.Drawing.Graphics]::FromImage($contact)
$g.Clear([System.Drawing.Color]::FromArgb(255, 58, 66, 84))
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$font = New-Object System.Drawing.Font('Segoe UI', 11, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$small = New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$brush = [System.Drawing.Brushes]::White
$row = 0
foreach ($name in $anims.Keys) {
  $y0 = $pad + $row * ($cell * $zoom + $pad)
  $g.DrawString($name, $font, $brush, 8, $y0 + 4)
  $g.DrawString("$($anims[$name].Count) frames", $small, [System.Drawing.Brushes]::LightGray, 8, $y0 + 22)
  for ($col = 0; $col -lt $columns; $col++) {
    $x0 = $labelW + $pad + $col * ($cell * $zoom + $pad)
    $bg = if (($row + $col) % 2 -eq 0) { [System.Drawing.Color]::FromArgb(255, 86, 98, 122) } else { [System.Drawing.Color]::FromArgb(255, 214, 222, 232) }
    if ($col -lt $anims[$name].Count) {
      $g.FillRectangle((New-Object System.Drawing.SolidBrush $bg), $x0, $y0, $cell * $zoom, $cell * $zoom)
      $g.DrawImage($sheet, (New-Object System.Drawing.Rectangle $x0, $y0, ($cell * $zoom), ($cell * $zoom)), (New-Object System.Drawing.Rectangle ($col * $cell), ($row * $cell), $cell, $cell), [System.Drawing.GraphicsUnit]::Pixel)
    }
  }
  $row++
}
$contactPath = Join-Path $OutDir 'contact-sheet.png'
$contact.Save($contactPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $contact.Dispose(); $sheet.Dispose()
"Wrote $sheetPath ($($columns * $cell)x$($rows * $cell), $rows animations) and $contactPath"
