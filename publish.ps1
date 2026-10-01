$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src/DesktopPet/DesktopPet.csproj'
$output = Join-Path $PSScriptRoot 'publish/win-x64-current'
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $output
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}
$videoSource = Join-Path $PSScriptRoot 'videos'
$videoDestination = Join-Path $output 'Assets/Video'
if (Test-Path $videoSource) {
    New-Item -ItemType Directory -Force -Path $videoDestination | Out-Null
    Get-ChildItem -LiteralPath $videoSource -Filter *.mov -File | Copy-Item -Destination $videoDestination -Force
}
$leftWalkSource = Join-Path $PSScriptRoot 'src/DesktopPet/Assets/Video/LeftWalk'
$leftWalkDestination = Join-Path $videoDestination 'LeftWalk'
if (Test-Path $leftWalkSource) {
    New-Item -ItemType Directory -Force -Path $leftWalkDestination | Out-Null
    Get-ChildItem -LiteralPath $leftWalkSource -Filter *.mov -File | Copy-Item -Destination $leftWalkDestination -Force
}
$ffmpegSource = Join-Path $PSScriptRoot 'tools/ffmpeg.exe'
if (Test-Path $ffmpegSource) {
    New-Item -ItemType Directory -Force -Path $videoDestination | Out-Null
    Copy-Item -LiteralPath $ffmpegSource -Destination (Join-Path $videoDestination 'ffmpeg.exe') -Force
}
Write-Host "Published self-contained Windows x64 build to $output"
