param(
  [string]$Rid = "win-x64",
  [switch]$SelfContained = $true
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSCommandPath
Set-Location $root

Write-Host "== Clean =="
dotnet clean

$pub = Join-Path $root "publish"
Remove-Item $pub -Recurse -Force -ErrorAction Ignore
New-Item $pub -ItemType Directory -Force | Out-Null

$common = @(
  "-c","Release",
  "-r",$Rid,
  "--self-contained",$SelfContained.IsPresent,
  "-p:PublishSingleFile=false",
  "-p:PublishReadyToRun=false",
  "-p:IncludeNativeLibrariesForSelfExtract=false",
  "-p:DebugType=None"
)

Write-Host "== Publish Server =="
dotnet publish .\QueueServer.Api\QueueServer.Api.csproj $common -o "$pub\server"
New-Item "$pub\server\App_Data" -ItemType Directory -Force | Out-Null
New-Item "$pub\server\wwwroot\videos" -ItemType Directory -Force | Out-Null

Write-Host "== Publish Admin =="
dotnet publish .\AdminApp.Wpf\AdminApp.Wpf.csproj $common -o "$pub\admin"

Write-Host "== Publish Teller =="
dotnet publish .\TellerApp.Wpf\TellerApp.Wpf.csproj $common -o "$pub\teller"

Write-Host "== Publish Display =="
dotnet publish .\DisplayApp.Wpf\DisplayApp.Wpf.csproj $common -o "$pub\display"

# Copy resource folders (libvlc, audio, Resources) ke display kalau belum otomatis
$displayResSrc = ".\DisplayApp.Wpf\Resources"
if (Test-Path $displayResSrc) { Copy-Item $displayResSrc "$pub\display\Resources" -Recurse -Force }

$audioSrc = ".\DisplayApp.Wpf\audio"
if (Test-Path $audioSrc) { Copy-Item $audioSrc "$pub\display\audio" -Recurse -Force }

$libVlcSrc = ".\DisplayApp.Wpf\libvlc"
if (Test-Path $libVlcSrc) { Copy-Item $libVlcSrc "$pub\display\libvlc" -Recurse -Force }

Write-Host "== DONE. Output: $pub =="