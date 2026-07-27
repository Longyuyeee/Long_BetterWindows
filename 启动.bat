@echo off
setlocal
pushd "%~dp0"

echo Starting Long Assistant (Release)...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-long.ps1"
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo.
    echo Start failed with exit code %EXIT_CODE%.
    pause
)

popd
exit /b %EXIT_CODE%
