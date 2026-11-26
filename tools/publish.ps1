[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [switch]$FrameworkDependent
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\LightningStart\LightningStart.csproj'
$distRoot = Join-Path $root 'dist'

if (-not (Test-Path $project)) {
    throw "Project file not found: $project"
}

$running = Get-Process -Name 'LightningStart' -ErrorAction SilentlyContinue
if ($running) {
    $names = ($running | Select-Object -ExpandProperty ProcessName -Unique) -join ', '
    throw "Please close the running app before publishing: $names"
}

$projectXml = [xml](Get-Content -Raw -Encoding UTF8 -LiteralPath $project)
$version = @($projectXml.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1
if (-not $version) {
    throw 'Unable to read <Version> from the project file.'
}

$packageKind = if ($FrameworkDependent) { "$Runtime-framework-dependent" } else { $Runtime }
$packageName = "LightningStart-$version-$packageKind"
$publishDir = Join-Path $distRoot $packageName

if (Test-Path $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
$publishArgs = @(
    'publish', $project,
    '-c', 'Release',
    '-r', $Runtime,
    '--self-contained', $selfContained,
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:PublishTrimmed=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '--nologo',
    '-o', $publishDir
)

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Get-ChildItem -LiteralPath $publishDir -Recurse -File -Filter '*.pdb' |
    Remove-Item -Force

$exePath = Join-Path $publishDir 'LightningStart.exe'
if (-not (Test-Path $exePath)) {
    throw "Published executable not found: $exePath"
}

$zipPath = Join-Path $distRoot "$packageName.zip"
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$hashLine = "$hash  $([System.IO.Path]::GetFileName($zipPath))"
Set-Content -LiteralPath "$zipPath.sha256" -Value $hashLine -Encoding Ascii

Write-Host ''
Write-Host "Published: $zipPath"
Write-Host "SHA256:   $hash"
