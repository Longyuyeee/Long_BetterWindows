@echo off
chcp 65001 >nul
title Long窗口 - 开发模式

echo ====================================
echo   Long窗口 v1.6 - 开发模式
echo ====================================
echo.

:: 检查是否在项目根目录
if not exist "src\LongBetterWindows.Host\LongBetterWindows.Host.csproj" (
    echo [错误] 请在项目根目录运行此脚本！
    pause
    exit /b 1
)

:: 构建并运行（Debug 模式）
echo [1/2] 正在构建项目 (Debug)...
dotnet build -c Debug --nologo
if %ERRORLEVEL% neq 0 (
    echo [错误] 构建失败！
    pause
    exit /b 1
)
echo [√] 构建成功
echo.

echo [2/2] 启动应用（开发模式）...
echo.
echo ====================================
echo   开发模式已启动！
echo   - 支持热重载
echo   - 详细日志输出
echo ====================================
echo.

dotnet run --project src\LongBetterWindows.Host\LongBetterWindows.Host.csproj -c Debug --no-build
