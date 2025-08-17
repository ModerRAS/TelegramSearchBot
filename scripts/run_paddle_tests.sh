#!/bin/bash

# 获取项目根目录
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# 设置库路径
export LD_LIBRARY_PATH="$PROJECT_ROOT/.nuget/packages/sdcb.paddleinference.runtime.linux-x64.mkl/3.1.0.54/runtimes/linux-x64/native:$LD_LIBRARY_PATH"

# 运行测试
cd "$PROJECT_ROOT"
dotnet test --filter "PaddleInferenceLinuxCompatibilityTests"