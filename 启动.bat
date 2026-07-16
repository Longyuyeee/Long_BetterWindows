@echo off
chcp 65001 >nul
title Long窗口 - 一键启动

echo ====================================
echo   Long窗口 v1.6 - 一键启动脚本
echo ====================================
echo.

:: 检查是否在项目根目录
if not exist "src\LongBetterWindows.Host\LongBetterWindows.Host.csproj" (
    echo [错误] 请在项目根目录运行此脚本！
    pause
    exit /b 1
)

:: 步骤1: 构建项目
echo [1/3] 正在构建项目...
dotnet build -c Release --nologo -v quiet
if %ERRORLEVEL% neq 0 (
    echo [错误] 构建失败！
    pause
    exit /b 1
)
echo [√] 构建成功
echo.

:: 步骤2: 复制插件
echo [2/3] 正在复制 WebView 插件...
set "BUILD_DIR=src\LongBetterWindows.Host\bin\Release\net8.0-windows"
set "PLUGIN_DIR=%BUILD_DIR%\Plugins"

:: 创建插件目录
if not exist "%PLUGIN_DIR%" mkdir "%PLUGIN_DIR%"

:: 复制所有 WebView 插件
for /d %%D in (src\*) do (
    if exist "%%D\manifest.json" (
        if not exist "%%D\%%~nD.csproj" (
            echo   - 复制 %%~nD
            xcopy /Y /Q "%%D\*" "%PLUGIN_DIR%\%%~nD\" >nul 2>&1
            if not exist "%PLUGIN_DIR%\%%~nD" mkdir "%PLUGIN_DIR%\%%~nD"
            copy /Y "%%D\manifest.json" "%PLUGIN_DIR%\%%~nD\" >nul 2>&1
            if exist "%%D\index.html" copy /Y "%%D\index.html" "%PLUGIN_DIR%\%%~nD\" >nul 2>&1
            if exist "%%D\*.js" copy /Y "%%D\*.js" "%PLUGIN_DIR%\%%~nD\" >nul 2>&1
            if exist "%%D\*.css" copy /Y "%%D\*.css" "%PLUGIN_DIR%\%%~nD\" >nul 2>&1
        )
    )
)
echo [√] 插件复制完成
echo.

:: 步骤3: 启动应用
echo [3/3] 正在启动 Long窗口...
echo.
echo ====================================
echo   应用已启动！
echo ====================================
echo.

cd "%BUILD_DIR%"
start "" "LongBetterWindows.Host.exe"

timeout /t 2 >nul
exit
