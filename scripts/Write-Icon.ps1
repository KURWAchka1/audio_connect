$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repo = Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Force -Path (Join-Path $repo 'assets') | Out-Null
$bitmap = New-Object System.Drawing.Bitmap 64,64
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(36,107,224))
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White),4
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
foreach ($bar in @(@(16,28,36),@(24,21,43),@(32,15,49),@(40,21,43),@(48,28,36))) { $graphics.DrawLine($pen,$bar[0],$bar[1],$bar[0],$bar[2]) }
$icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
$stream = [System.IO.File]::Create((Join-Path $repo 'assets/app.ico'))
$icon.Save($stream)
$stream.Dispose(); $icon.Dispose(); $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
