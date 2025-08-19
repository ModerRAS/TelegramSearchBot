#!/bin/bash

# TelegramSearchBot Linux 部署验证脚本
# 
# 这个脚本验证 Linux 部署是否成功配置

# 获取项目根目录
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# 切换到项目根目录
cd "$PROJECT_ROOT"

echo "=== TelegramSearchBot Linux 部署验证 ==="
echo

# 检查操作系统
echo "1. 检查操作系统..."
if [[ "$OSTYPE" == "linux-gnu"* ]]; then
    echo "✅ Linux 操作系统: $(uname -a)"
else
    echo "❌ 非Linux操作系统: $OSTYPE"
    exit 1
fi
echo

# 检查 .NET 运行时
echo "2. 检查 .NET 运行时..."
if command -v dotnet &> /dev/null; then
    dotnet_version=$(dotnet --version)
    echo "✅ .NET 版本: $dotnet_version"
else
    echo "❌ .NET 运行时未安装"
    exit 1
fi
echo

# 检查系统依赖
echo "3. 检查系统依赖..."
dependencies=(
    "libgomp.so.1"
    "libdnnl.so.2"
    "libiomp5.so"
)

for dep in "${dependencies[@]}"; do
    if ldconfig -p | grep -q "$dep"; then
        echo "✅ $dep 已安装"
    else
        echo "❌ $dep 未找到"
    fi
done
echo

# 检查项目文件
echo "4. 检查项目文件..."
project_files=(
    "TelegramSearchBot/TelegramSearchBot.csproj"
    "TelegramSearchBot.Common/TelegramSearchBot.Common.csproj"
    "TelegramSearchBot.Test/TelegramSearchBot.Test.csproj"
)

for file in "${project_files[@]}"; do
    if [[ -f "$file" ]]; then
        echo "✅ $file 存在"
    else
        echo "❌ $file 不存在"
    fi
done
echo

# 检查运行时包配置
echo "5. 检查运行时包配置..."
if grep -q "Sdcb.PaddleInference.runtime.linux-x64.mkl" TelegramSearchBot/TelegramSearchBot.csproj; then
    echo "✅ Linux 运行时包已配置"
else
    echo "❌ Linux 运行时包未配置"
fi

if grep -q "Condition.*linux-x64" TelegramSearchBot/TelegramSearchBot.csproj; then
    echo "✅ 条件编译已配置"
else
    echo "❌ 条件编译未配置"
fi
echo

# 检查构建输出
echo "6. 检查构建输出..."
build_dir="TelegramSearchBot/bin/Release/net9.0/linux-x64"
if [[ -d "$build_dir" ]]; then
    echo "✅ Linux 构建输出目录存在"
    
    # 检查关键原生库
    native_libs=(
        "libpaddle_inference_c.so"
        "libmklml_intel.so"
        "libonnxruntime.so.1.11.1"
        "libpaddle2onnx.so.1.0.0rc2"
        "libdnnl.so.3"
        "libiomp5.so"
    )
    
    for lib in "${native_libs[@]}"; do
        if [[ -f "$build_dir/$lib" ]]; then
            echo "✅ $lib 已构建"
        else
            echo "❌ $lib 未找到"
        fi
    done
else
    echo "❌ Linux 构建输出目录不存在"
fi
echo

# 检查运行脚本
echo "7. 检查运行脚本..."
scripts=(
    "scripts/run_linux.sh"
    "scripts/run_paddle_tests.sh"
)

for script in "${scripts[@]}"; do
    if [[ -f "$script" && -x "$script" ]]; then
        echo "✅ $script 存在且可执行"
    else
        echo "❌ $script 不存在或不可执行"
    fi
done
echo

# 检查文档
echo "8. 检查文档..."
if [[ -f "Docs/LINUX_DEPLOYMENT.md" ]]; then
    echo "✅ Linux 部署文档存在"
else
    echo "❌ Linux 部署文档不存在"
fi
echo

# 运行测试
echo "9. 运行测试..."
if ./scripts/run_paddle_tests.sh > /dev/null 2>&1; then
    echo "✅ PaddleInference 测试通过"
else
    echo "❌ PaddleInference 测试失败"
fi
echo

echo "=== 验证完成 ==="
echo
echo "如果所有检查都通过，说明 Linux 部署配置成功！"
echo "使用以下命令运行应用程序："
echo "  ./scripts/run_linux.sh"
echo
echo "查看 Linux 部署指南："
echo "  cat Docs/LINUX_DEPLOYMENT.md"