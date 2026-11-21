@echo off
setlocal

pushd "%~dp0"
if errorlevel 1 (
    echo Could not open the project directory.
    pause
    exit /b 1
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File ".\build.ps1" %*
set "publishExitCode=%ERRORLEVEL%"
popd

if not "%publishExitCode%"=="0" echo Publish failed with exit code %publishExitCode%.
pause
exit /b %publishExitCode%
