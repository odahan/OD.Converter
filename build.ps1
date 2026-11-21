param(
    [string]$Configuration = "Release",
    [string]$Output = "publish",
    [switch]$IncludeGui = $true,
    [switch]$CliOnly
)
$ErrorActionPreference = "Stop"
$publishGui = $IncludeGui -and -not $CliOnly
if ($publishGui -and ($Configuration -ne "Release" -or -not [Environment]::Is64BitOperatingSystem)) {
    throw "The combined release requires Release configuration on Windows x64."
}

function Invoke-DotNet([string[]]$Arguments, [string]$LogPath) {
    if ($LogPath) {
        & dotnet @Arguments *> $LogPath
    }
    else {
        & dotnet @Arguments
    }
    if ($LASTEXITCODE -ne 0) {
        $details = if ($LogPath) { " See $LogPath." } else { "" }
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE.$details"
    }
}

function Assert-CanReplaceExecutable([string]$Path) {
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        try {
            [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite).Dispose()
        }
        catch {
            throw "Cannot replace $Path. Close any running instance and check write access. $($_.Exception.Message)"
        }
    }
}

Push-Location $PSScriptRoot
try {
    if ($publishGui) {
        $guiDestination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath(
            (Join-Path $Output "odconv-gui.exe"))
        Assert-CanReplaceExecutable $guiDestination
        $validationRoot = Join-Path $PSScriptRoot "out/release-validation-$([guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $validationRoot | Out-Null
        $testLog = Join-Path $validationRoot "tests.log"
        Invoke-DotNet @("build", "OD.Converter.slnx", "-c", "Release", "-m:1")
        Write-Host "Running tests (log: $testLog)..." -ForegroundColor Cyan
        Invoke-DotNet @("test", "OD.Converter.slnx", "-c", "Release", "-m:1", "--no-build", "--no-restore") -LogPath $testLog
        Write-Host "Tests passed." -ForegroundColor Green
    }
    else {
        Invoke-DotNet @("build", "src/OD.Converter", "-c", $Configuration, "-r", "win-x64", "-m:1")
    }

    Write-Host "Publishing CLI..." -ForegroundColor Cyan
    Invoke-DotNet @("publish", "src/OD.Converter", "-c", $Configuration, "-r", "win-x64", "-m:1", "-o", $Output)
    $cli = (Resolve-Path -LiteralPath (Join-Path $Output "odconv.exe")).Path
    & $cli --help | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "CLI help check failed ($LASTEXITCODE)." }
    & $cli formats | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "CLI format check failed ($LASTEXITCODE)." }

    if ($publishGui) {
        $guiProject = "src/OD.Converter.Gui/OD.Converter.Gui.csproj"
        $guiStaging = Join-Path $validationRoot "gui/publish"
        Write-Host "Publishing GUI..." -ForegroundColor Cyan
        Invoke-DotNet @("build", $guiProject, "-c", "Release", "-r", "win-x64", "-m:1")
        Invoke-DotNet @("publish", $guiProject, "-c", "Release", "-r", "win-x64", "-m:1", "-o", $guiStaging)
        $gui = (Resolve-Path -LiteralPath (Join-Path $guiStaging "odconv-gui.exe")).Path

        $cliValidation = Join-Path $validationRoot "cli"
        $standalone = Join-Path $validationRoot "gui/executable only"
        New-Item -ItemType Directory -Path $standalone -Force | Out-Null
        $standaloneExe = Join-Path $standalone "odconv-gui.exe"
        Copy-Item -LiteralPath $gui -Destination $standaloneExe
        $report = Join-Path $validationRoot "gui/results.json"
        $validator = "tools/OD.Converter.ReleaseValidation/bin/Release/net10.0/win-x64/OD.Converter.ReleaseValidation.exe"
        & $validator $cli "src/OD.Converter/tools/ffmpeg.exe" $cliValidation
        if ($LASTEXITCODE -ne 0) { throw "CLI standalone validation failed ($LASTEXITCODE). See $cliValidation." }

        $environment = @{
            PATH = [Environment]::SystemDirectory
            OD_CONVERTER_FFMPEG = $null
            DOTNET_BUNDLE_EXTRACT_BASE_DIR = (Join-Path $validationRoot "gui/bundle")
        }
        $previous = @{}
        try {
            foreach ($name in $environment.Keys) {
                $previous[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
                [Environment]::SetEnvironmentVariable($name, $environment[$name], "Process")
            }
            $validationArgs = @("--validate-bundle", ('"{0}"' -f $report))
            $process = Start-Process -FilePath $standaloneExe -WorkingDirectory $standalone -WindowStyle Hidden -ArgumentList $validationArgs -PassThru
            try {
                if (-not $process.WaitForExit(120000)) {
                    $process.Kill()
                    $process.WaitForExit()
                    throw "GUI standalone validation exceeded two minutes."
                }
                if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) {
                    throw "GUI standalone validation failed ($($process.ExitCode)). See $report."
                }
            }
            finally { $process.Dispose() }
        }
        finally {
            foreach ($name in $previous.Keys) {
                [Environment]::SetEnvironmentVariable($name, $previous[$name], "Process")
            }
        }

        $cliVersion = (Get-Item -LiteralPath $cli).VersionInfo.ProductVersion
        $guiVersion = (Get-Item -LiteralPath $gui).VersionInfo.ProductVersion
        $guiCheck = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        if ($cliVersion -ne $guiVersion -or $guiCheck.Status -ne "Passed") {
            throw "Release version or GUI validation mismatch. See $report."
        }
        $cliReport = Get-Content -LiteralPath (Join-Path $cliValidation "results.json") -Raw | ConvertFrom-Json
        $cliGuide = ($cliReport.Results | Where-Object Check -eq "embedded-guide-extracted").Path
        $expectedGuide = (Get-FileHash -LiteralPath "docs/user-guide.html" -Algorithm SHA256).Hash
        foreach ($extractedGuide in @($cliGuide, $guiCheck.Guide)) {
            if ([string]::IsNullOrWhiteSpace($extractedGuide) -or
                (Get-FileHash -LiteralPath $extractedGuide -Algorithm SHA256).Hash -ne $expectedGuide) {
                throw "Both standalone executables must extract the current shared user guide. See $validationRoot."
            }
        }

        $lines = foreach ($exe in @($cli, $gui)) {
            $hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $([IO.Path]::GetFileName($exe))"
        }
        Assert-CanReplaceExecutable $guiDestination
        Copy-Item -LiteralPath $gui -Destination $guiDestination -Force
        Write-Host "Validated GUI published: $guiDestination" -ForegroundColor Green
        $lines | Set-Content -LiteralPath (Join-Path $Output "SHA256SUMS.txt") -Encoding Ascii
        Write-Host "Standalone validation reports: $validationRoot" -ForegroundColor Green
    }
    Write-Host "Publish OK" -ForegroundColor Green
}
catch {
    Write-Error -ErrorAction Continue $_
    exit 1
}
finally {
    Pop-Location
}
