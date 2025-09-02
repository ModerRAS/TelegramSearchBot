#!/bin/bash

# TelegramSearchBot Linux 运行脚本
# 
# 原本实现：直接运行 dotnet run 命令
# 简化实现：设置必要的环境变量后运行，确保 Linux 上的原生库能正确加载
# 
# 这个脚本解决了 Linux 上的 PaddleInference 库依赖问题，
# 通过设置 LD_LIBRARY_PATH 环境变量来确保运行时库能被正确找到。

# 获取脚本所在目录
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# 获取项目根目录（scripts的上一级目录）
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# 设置 PaddleInference Linux 运行时库路径
PADDLE_LINUX_RUNTIME_PATH="$PROJECT_ROOT/.nuget/packages/sdcb.paddleinference.runtime.linux-x64.mkl/3.1.0.54/runtimes/linux-x64/native"

# 检查运行时库是否存在
if [ ! -d "$PADDLE_LINUX_RUNTIME_PATH" ]; then
    echo "错误：找不到 PaddleInference Linux 运行时库"
    echo "请确保已安装 Linux 运行时包：Sdcb.PaddleInference.runtime.linux-x64.mkl"
    exit 1
fi

# 设置库路径环境变量
export LD_LIBRARY_PATH="$PADDLE_LINUX_RUNTIME_PATH:$LD_LIBRARY_PATH"

echo "已设置 Linux 运行时库路径: $PADDLE_LINUX_RUNTIME_PATH"
echo "正在启动 TelegramSearchBot..."

# 运行应用程序
cd "$PROJECT_ROOT/TelegramSearchBot"
dotnet run "$@"