#!/bin/bash

# Long窗口 v1.6 - 一键启动脚本 (Linux/macOS)

echo "===================================="
echo "  Long窗口 v1.6 - 一键启动脚本"
echo "===================================="
echo

# 检查是否在项目根目录
if [ ! -f "src/LongBetterWindows.Host/LongBetterWindows.Host.csproj" ]; then
    echo "[错误] 请在项目根目录运行此脚本！"
    exit 1
fi

# 步骤1: 构建项目
echo "[1/3] 正在构建项目..."
dotnet build -c Release --nologo -v quiet
if [ $? -ne 0 ]; then
    echo "[错误] 构建失败！"
    exit 1
fi
echo "[√] 构建成功"
echo

# 步骤2: 复制插件
echo "[2/3] 正在复制 WebView 插件..."
BUILD_DIR="src/LongBetterWindows.Host/bin/Release/net8.0-windows"
PLUGIN_DIR="$BUILD_DIR/Plugins"

mkdir -p "$PLUGIN_DIR"

for dir in src/*/; do
    if [ -f "$dir/manifest.json" ] && [ ! -f "$dir/$(basename $dir).csproj" ]; then
        plugin_name=$(basename "$dir")
        echo "  - 复制 $plugin_name"
        mkdir -p "$PLUGIN_DIR/$plugin_name"
        cp -r "$dir"* "$PLUGIN_DIR/$plugin_name/" 2>/dev/null
    fi
done
echo "[√] 插件复制完成"
echo

# 步骤3: 启动应用
echo "[3/3] 正在启动 Long窗口..."
echo
echo "===================================="
echo "  应用已启动！"
echo "===================================="
echo

cd "$BUILD_DIR"
./LongBetterWindows.Host.exe
