param(
    [string]$Version = '1.0.0',
    [int]$VersionCode = 1,
    [string]$Dotnet = 'dotnet',
    [string]$AndroidSdk = $env:ANDROID_HOME,
    [string]$Iscc = 'iscc',
    [switch]$SkipAndroid
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repo 'dist'
$publish = Join-Path $dist 'windows'
$releases = Join-Path $dist 'releases'
New-Item -ItemType Directory -Force -Path $publish,$releases | Out-Null
function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Build step failed: $Command (exit $LASTEXITCODE)" }
}
Push-Location $repo
try {
    Invoke-Checked $Dotnet @('build','desktop/AudioConnect.Tests/AudioConnect.Tests.csproj','-c','Release')
    Invoke-Checked $Dotnet @('run','--project','desktop/AudioConnect.Tests','-c','Release','--no-build')
    Invoke-Checked $Dotnet @('publish','desktop/AudioConnect.Windows/AudioConnect.Windows.csproj','-c','Release','-r','win-x64','--self-contained','true',"-p:Version=$Version",'-o',$publish)
    $adbSource = Join-Path $AndroidSdk 'platform-tools'
    $adbDestination = Join-Path $publish 'platform-tools'
    New-Item -ItemType Directory -Force -Path $adbDestination | Out-Null
    foreach ($file in @('adb.exe','AdbWinApi.dll','AdbWinUsbApi.dll','NOTICE.txt','source.properties')) {
        Copy-Item -LiteralPath (Join-Path $adbSource $file) -Destination $adbDestination
    }
    foreach ($file in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $publish }
    $dotnetDirectory = Split-Path -Parent (Get-Command $Dotnet -ErrorAction Stop).Source
    $licenses = Join-Path $publish 'licenses'
    New-Item -ItemType Directory -Force -Path $licenses | Out-Null
    Copy-Item -LiteralPath (Join-Path $dotnetDirectory 'LICENSE.txt') -Destination (Join-Path $licenses 'DOTNET-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $dotnetDirectory 'ThirdPartyNotices.txt') -Destination (Join-Path $licenses 'DOTNET-THIRD-PARTY-NOTICES.txt')
    Invoke-Checked $Iscc @("/DAppVersion=$Version","/DPublishDir=$publish","/DOutputDirPath=$releases",'installer/AudioConnect.iss')
    if (!$SkipAndroid) {
        if (!$env:AC_KEYSTORE) { throw 'Set AC_KEYSTORE and signing password environment variables for an update-compatible release APK.' }
        Push-Location android
        try { Invoke-Checked '.\gradlew.bat' @('testDebugUnitTest','lintRelease','assembleRelease',"-PappVersion=$Version","-PappVersionCode=$VersionCode",'--console=plain') }
        finally { Pop-Location }
        Copy-Item -LiteralPath 'android/app/build/outputs/apk/release/app-release.apk' -Destination (Join-Path $releases "AudioConnect-$Version-android.apk")
    }
    $checksums = Get-ChildItem -LiteralPath $releases -File | Where-Object { $_.Extension -in '.exe','.apk' } | Sort-Object Name | ForEach-Object {
        "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
    }
    $checksums | Set-Content -LiteralPath (Join-Path $releases 'SHA256SUMS.txt') -Encoding utf8
}
finally { Pop-Location }
