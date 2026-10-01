# Builds a self-contained Windows package into out\win (DigDug.exe + SDL2.dll).
# Requires the .NET 8 SDK (https://dotnet.microsoft.com/download). SDL2.dll is downloaded from the official SDL releases.
param([string]$Sdl2Version = "2.30.9")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

dotnet publish -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -o out\win
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not (Test-Path out\win\SDL2.dll)) {
    $zip = Join-Path $env:TEMP "SDL2-$Sdl2Version.zip"
    Invoke-WebRequest "https://github.com/libsdl-org/SDL/releases/download/release-$Sdl2Version/SDL2-$Sdl2Version-win32-x64.zip" -OutFile $zip
    Expand-Archive $zip (Join-Path $env:TEMP "SDL2-$Sdl2Version") -Force
    Copy-Item (Join-Path $env:TEMP "SDL2-$Sdl2Version\SDL2.dll") out\win\
}
Remove-Item out\win\*.pdb -ErrorAction SilentlyContinue
Write-Host "Built out\win\DigDug.exe"
