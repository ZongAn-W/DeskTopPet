$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src/DesktopPet/DesktopPet.csproj'
$output = Join-Path $PSScriptRoot 'publish/win-x64'
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $output
Write-Host "Published self-contained Windows x64 build to $output"
