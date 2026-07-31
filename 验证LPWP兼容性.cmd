@echo off
setlocal
pushd "%~dp0"

if "%~1"=="" (
    set "OUTPUT_DIR=%TEMP%\LongAssistant-LPWP-%RANDOM%-%RANDOM%"
) else (
    set "OUTPUT_DIR=%~1"
)

echo Verifying Long Assistant LPWP compatibility...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0verify-lpwp-compatibility.ps1" -OutputDirectory "%OUTPUT_DIR%"
set "EXIT_CODE=%ERRORLEVEL%"

if "%EXIT_CODE%"=="0" (
    echo.
    echo Verification passed. Report: %OUTPUT_DIR%\lpwp-compatibility-report.json
) else (
    echo.
    echo Verification failed with exit code %EXIT_CODE%.
)

popd
exit /b %EXIT_CODE%
