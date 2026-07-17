@echo off
chcp 65001 >nul
title Long窗口 - 开发模式

echo ====================================
echo   Long窗口 v1.8 - 开发模式
echo ====================================
echo.

if not exist "src\LongBetterWindows.Host\LongBetterWindows.Host.csproj" (
    echo [错误] 请在项目根目录执行
    pause
    exit /b 1
)

echo [1/1] 构建并启动 (Debug)...
dotnet run --project src\LongBetterWindows.Host\LongBetterWindows.Host.csproj -c Debug
