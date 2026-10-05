param(
    [Parameter(Mandatory=$true)][string]$GameDir,
    [string]$InteropDir
)
$ErrorActionPreference='Stop'
$buildArgs=@('build',(Join-Path $PSScriptRoot 'src/AL_Pregnancy.csproj'),'-c','Release',"-p:GameDir=$GameDir")
if($InteropDir){$buildArgs+="-p:InteropDir=$InteropDir"}
& dotnet @buildArgs
if($LASTEXITCODE -ne 0){throw "Build failed: $LASTEXITCODE"}
Write-Host 'Built: src/bin/Release/net6.0/AL_Pregnancy.dll'
