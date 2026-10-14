param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
$destinationPath = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Force -Path $destinationPath | Out-Null
$installer = Join-Path $destinationPath 'innosetup-6.7.3.exe'
Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -OutFile $installer
$expected = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Inno Setup download checksum mismatch' }
$compiler = Join-Path $destinationPath 'compiler'
$install = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/CURRENTUSER',"/DIR=$compiler") -WindowStyle Hidden -Wait -PassThru
if ($install.ExitCode -ne 0) { throw "Inno Setup installation failed: $($install.ExitCode)" }
$iscc = Join-Path $compiler 'ISCC.exe'
if (!(Test-Path -LiteralPath $iscc)) { throw 'Inno Setup compiler missing after installation' }
Write-Output $iscc
