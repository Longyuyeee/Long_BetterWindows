@echo off
chcp 65001 >nul
title Long窗口 - 打包发布

echo ====================================
echo   Long窗口 v1.8 - 打包发布
echo ====================================
echo.

set "VERSION=1.8.0"
set "OUTPUT=releases\v%VERSION%"

echo [1/3] 清理旧文件...
if exist "%OUTPUT%" rmdir /s /q "%OUTPUT%"
mkdir "%OUTPUT%"

echo [2/3] 构建 Release...
dotnet build -c Release --nologo
if %ERRORLEVEL% neq 0 (
    echo [错误] 构建失败
    pause
    exit /b 1
)

echo [3/3] 复制文件...
set "SRC=src\LongBetterWindows.Host\bin\Release\net8.0-windows"
xcopy /E /Y /Q "%SRC%\*" "%OUTPUT%\" >nul

echo.
echo ====================================
echo   打包完成: %OUTPUT%
echo ====================================
pause
