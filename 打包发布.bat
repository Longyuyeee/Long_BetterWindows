@echo off
chcp 65001 >nul
title Long窗口 - 打包发布

echo ====================================
echo   Long窗口 v1.6 - 打包发布脚本
echo ====================================
echo.

:: 检查是否在项目根目录
if not exist "src\LongBetterWindows.Host\LongBetterWindows.Host.csproj" (
    echo [错误] 请在项目根目录运行此脚本！
    pause
    exit /b 1
)

:: 获取版本号
set "VERSION=1.6.1"
set "OUTPUT_DIR=releases\v%VERSION%"

:: 步骤1: 清理旧的发布文件
echo [1/5] 清理旧的发布文件...
if exist "%OUTPUT_DIR%" rmdir /s /q "%OUTPUT_DIR%"
mkdir "%OUTPUT_DIR%"
echo [√] 清理完成
echo.

:: 步骤2: 构建项目（Release 配置）
echo [2/5] 正在构建项目 (Release)...
dotnet build -c Release --nologo
if %ERRORLEVEL% neq 0 (
    echo [错误] 构建失败！
    pause
    exit /b 1
)
echo [√] 构建成功
echo.

:: 步骤3: 复制主程序
echo [3/5] 复制主程序文件...
set "BUILD_DIR=src\LongBetterWindows.Host\bin\Release\net8.0-windows"
xcopy /Y /Q "%BUILD_DIR%\*.exe" "%OUTPUT_DIR%\" >nul
xcopy /Y /Q "%BUILD_DIR%\*.dll" "%OUTPUT_DIR%\" >nul
xcopy /Y /Q "%BUILD_DIR%\*.json" "%OUTPUT_DIR%\" >nul
if exist "%BUILD_DIR%\runtimes" xcopy /E /Y /Q "%BUILD_DIR%\runtimes" "%OUTPUT_DIR%\runtimes\" >nul
echo [√] 主程序复制完成
echo.

:: 步骤4: 复制所有插件
echo [4/5] 复制插件...
if not exist "%OUTPUT_DIR%\Plugins" mkdir "%OUTPUT_DIR%\Plugins"

:: 复制 DLL 插件
for /d %%D in (src\*Plugin) do (
    if exist "%%D\bin\Release\net8.0-windows\%%~nD.dll" (
        echo   - 复制 DLL 插件: %%~nD
        copy /Y "%%D\bin\Release\net8.0-windows\%%~nD.dll" "%OUTPUT_DIR%\Plugins\" >nul
    )
)

:: 复制 WebView 插件
for /d %%D in (src\*) do (
    if exist "%%D\manifest.json" (
        if not exist "%%D\%%~nD.csproj" (
            echo   - 复制 WebView 插件: %%~nD
            if not exist "%OUTPUT_DIR%\Plugins\%%~nD" mkdir "%OUTPUT_DIR%\Plugins\%%~nD"
            xcopy /E /Y /Q "%%D\*" "%OUTPUT_DIR%\Plugins\%%~nD\" >nul
        )
    )
)
echo [√] 插件复制完成
echo.

:: 步骤5: 创建说明文件
echo [5/5] 创建说明文件...
(
echo Long窗口 v%VERSION% - Windows 效率增强平台
echo.
echo 使用说明:
echo 1. 双击 LongBetterWindows.Host.exe 启动程序
echo 2. 首次启动会自动加载所有插件
echo 3. 使用 Ctrl+Space 打开工具中心
echo.
echo 底层能力: 20 项
echo 插件总数: 21 个
echo.
echo 新增能力:
echo - system.clipboard.monitor: 剪贴板变更监听
echo - network.ports: 端口与进程映射
echo - system.performance: 系统性能监控
echo - filesystem.advanced: 高级文件系统操作
echo - text.pinyin: 拼音匹配与转换
echo - system.cache: 系统缓存清理
echo - system.schedule: 定时任务调度
echo - system.audio: 音频控制
echo.
echo 开发者: Kiro
echo 日期: 2026-07-16
) > "%OUTPUT_DIR%\README.txt"
echo [√] 说明文件创建完成
echo.

:: 计算文件大小
for /f "tokens=3" %%a in ('dir "%OUTPUT_DIR%" /s /-c ^| find "个文件"') do set SIZE=%%a

echo ====================================
echo   打包完成！
echo ====================================
echo.
echo 发布目录: %OUTPUT_DIR%
echo 文件大小: %SIZE% 字节
echo.
echo 可以将此目录压缩为 ZIP 分发
echo.
pause
