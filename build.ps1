param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "NetworkMonitor\NetworkMonitor.csproj"
$artifacts = Join-Path $root "artifacts"

New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

$targets = @(
    @{ Runtime = "win-x64"; SetupName = "NetworkMonitorSetup-x64.exe" },
    @{ Runtime = "win-x86"; SetupName = "NetworkMonitorSetup-x86.exe" }
)

foreach ($target in $targets) {
    $runtime = $target.Runtime
    $publishDir = Join-Path $root "dist\$runtime"

    dotnet publish $project `
        --configuration $Configuration `
        --runtime $runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:PublishReadyToRun=false `
        --output $publishDir

    Copy-Item -Force `
        -Path (Join-Path $publishDir "NetworkMonitor.exe") `
        -Destination (Join-Path $artifacts $target.SetupName)
}

Write-Host "Done. Setup executables are in the artifacts folder."
