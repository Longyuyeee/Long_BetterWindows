@echo off
setlocal
pushd "%~dp0"

echo Building Long Assistant release packages...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0package-long.ps1" -OpenOutput
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo.
    echo Packaging failed with exit code %EXIT_CODE%.
)

echo.
pause
popd
exit /b %EXIT_CODE%
