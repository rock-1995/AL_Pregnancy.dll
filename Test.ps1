param([Parameter(Mandatory=$true)][string]$GameDir)
$ErrorActionPreference='Stop'
$projects=@(
    'breast-physics-tests/BreastPhysicsRegression.csproj',
    'unrestricted-tests/UnrestrictedRegression.csproj',
    'tests/SelectionRegression.csproj',
    'collision-runtime-tests/CollisionRuntimeRegression.csproj',
    'readiness-tests/ReadinessRegression.csproj',
    'lifetime-tests/LifetimeRegression.csproj',
    'source-lifetime-tests/SourceLifetimeRegression.csproj',
    'clothing-runtime-tests/ClothingRuntimeRegression.csproj',
    'skirt-tests/SkirtRegression.csproj',
    'body-change-tests/BodyChangeRegression.csproj'
)
foreach($project in $projects){
    & dotnet run --project (Join-Path $PSScriptRoot $project) -c Release "-p:GameDir=$GameDir"
    if($LASTEXITCODE -ne 0){throw "Tests failed: $project ($LASTEXITCODE)"}
}
