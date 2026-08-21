param(
    [string]$Configuration = "Release",
    [string]$Output = "publish"
)
$ErrorActionPreference = "Stop"
Write-Host "Build odconv $Configuration win-x64..." -ForegroundColor Cyan
dotnet build src/OD.Converter -c $Configuration -r win-x64 -m:1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Publish self-contained..." -ForegroundColor Cyan
dotnet publish src/OD.Converter -c $Configuration -r win-x64 --self-contained true /p:PublishSingleFile=true -o $Output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$exe = Join-Path $Output "odconv.exe"
$size = (Get-Item $exe).Length / 1MB
Write-Host "OK $exe ($([math]::Round($size,1)) MB)" -ForegroundColor Green
& $exe --help
& $exe formats | Out-Null
Write-Host "Publish OK" -ForegroundColor Green
