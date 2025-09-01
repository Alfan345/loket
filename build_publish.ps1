<#
Automated build & publish script
Jalankan: powershell -ExecutionPolicy Bypass -File build_publish.ps1
#>

Write-Host "== RESTORE =="
dotnet restore

Write-Host "== BUILD (Debug) =="
dotnet build -c Debug

$publishRoot = Join-Path $PSScriptRoot "publish"
if (Test-Path $publishRoot) { Remove-Item $publishRoot -Recurse -Force }
New-Item -ItemType Directory -Path $publishRoot | Out-Null

Write-Host "== PUBLISH SERVER =="
dotnet publish QueueServer.Api -c Release -r win-x64 --self-contained true `
  /p:PublishSingleFile=true /p:PublishTrimmed=false -o "$publishRoot/server"

Write-Host "== PUBLISH TELLER =="
dotnet publish TellerApp.Wpf -c Release -r win-x64 --self-contained true `
  /p:PublishSingleFile=true /p:PublishTrimmed=false /p:IncludeNativeLibrariesForSelfExtract=true -o "$publishRoot/teller"

Write-Host "== PUBLISH DISPLAY =="
dotnet publish DisplayApp.Wpf -c Release -r win-x64 --self-contained true `
  /p:PublishSingleFile=true /p:PublishTrimmed=false /p:IncludeNativeLibrariesForSelfExtract=true -o "$publishRoot/display"

Write-Host "== COPY RESOURCES =="
Copy-Item Resources -Destination "$publishRoot/server/Resources" -Recurse
Copy-Item Resources -Destination "$publishRoot/teller/Resources" -Recurse
Copy-Item Resources -Destination "$publishRoot/display/Resources" -Recurse

Write-Host "== DONE =="
Write-Host "Output folder: $publishRoot"