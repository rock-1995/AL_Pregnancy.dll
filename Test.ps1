param([Parameter(Mandatory=$true)][string]$GameDir)
$ErrorActionPreference='Stop'
$projects=@('tests/SelectionRegression.csproj','collision-runtime-tests/CollisionRuntimeRegression.csproj','readiness-tests/ReadinessRegression.csproj','lifetime-tests/LifetimeRegression.csproj','source-lifetime-tests/SourceLifetimeRegression.csproj')
foreach($project in $projects){
    & dotnet run --project (Join-Path $PSScriptRoot $project) -c Release "-p:GameDir=$GameDir"
    if($LASTEXITCODE -ne 0){throw "Tests failed: $project ($LASTEXITCODE)"}
}
