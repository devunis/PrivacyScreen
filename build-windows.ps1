$ErrorActionPreference = "Stop"

Set-Location $PSScriptRoot

Write-Host "▸ Windows publish"
dotnet publish .\Windows\PrivacyScreen.Windows.csproj -c Release -r win-x64 --self-contained false

Write-Host ""
Write-Host "Done:"
Write-Host "Windows\bin\Release\net8.0-windows10.0.17763.0\win-x64\publish\PrivacyScreen.Windows.exe"
