param(
    [string]$SharpProsperoRoot = $env:SHARPPROSPERO_ROOT,
    [string]$OutputFolder = (Join-Path $PSScriptRoot 'out')
)

$ErrorActionPreference = 'Stop'

if (-not $SharpProsperoRoot) {
    throw 'Set SHARPPROSPERO_ROOT or pass -SharpProsperoRoot.'
}

$buildApp = Join-Path $SharpProsperoRoot 'build\build-app.ps1'
if (-not (Test-Path $buildApp)) {
    throw "build-app.ps1 not found under $SharpProsperoRoot"
}

$env:SHARPPROSPERO_ROOT = $SharpProsperoRoot

& $buildApp `
    -ProjectPath (Join-Path $PSScriptRoot 'src\NoUpdate.csproj') `
    -Payload `
    -Output Folder `
    -OutputFolder $OutputFolder
