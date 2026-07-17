@echo off
chcp 65001 >nul
title Long窗口 - 一键启动

echo ====================================
echo   Long窗口 v1.8 - 一键启动
echo ====================================
echo.

if not exist "src\LongBetterWindows.Host\LongBetterWindows.Host.csproj" (
    echo [错误] 请在项目根目录执行
    pause
    exit /b 1
)

echo [1/2] 构建项目...
dotnet build -c Release --nologo -v quiet
if %ERRORLEVEL% neq 0 (
    echo [错误] 构建失败
    pause
    exit /b 1
)
echo [OK] 构建成功
echo.

echo [2/2] 启动应用...
cd src\LongBetterWindows.Host\bin\Release\net8.0-windows
start "" "LongBetterWindows.Host.exe"
echo.
echo 应用已启动！
timeout /t 2 >nul
exit
