#!/bin/bash

# TelegramSearchBot.Search.Tests 运行脚本
# 用于运行搜索领域的所有测试

echo "🚀 开始运行 TelegramSearchBot.Search.Tests"

# 检查.NET SDK是否可用
if ! command -v dotnet &> /dev/null; then
    echo "❌ 错误: .NET SDK 未找到"
    exit 1
fi

# 获取脚本所在目录
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEST_PROJECT_DIR="$SCRIPT_DIR/TelegramSearchBot.Search.Tests"

# 检查测试项目是否存在
if [ ! -d "$TEST_PROJECT_DIR" ]; then
    echo "❌ 错误: 测试项目目录不存在: $TEST_PROJECT_DIR"
    exit 1
fi

echo "📁 测试项目目录: $TEST_PROJECT_DIR"

# 函数：运行特定类别的测试
run_tests() {
    local filter="$1"
    local description="$2"
    
    echo ""
    echo "🔍 运行 $description"
    echo "================================"
    
    cd "$TEST_PROJECT_DIR"
    
    if [ -n "$filter" ]; then
        dotnet test --filter "$filter" --verbosity normal
    else
        dotnet test --verbosity normal
    fi
    
    local exit_code=$?
    
    if [ $exit_code -eq 0 ]; then
        echo "✅ $description 通过"
    else
        echo "❌ $description 失败"
    fi
    
    return $exit_code
}

# 函数：显示帮助信息
show_help() {
    echo "📖 TelegramSearchBot.Search.Tests 运行脚本"
    echo ""
    echo "用法:"
    echo "  $0 [选项]"
    echo ""
    echo "选项:"
    echo "  -h, --help        显示帮助信息"
    echo "  -a, --all         运行所有测试（默认）"
    echo "  -l, --lucene      只运行Lucene搜索测试"
    echo "  -v, --vector      只运行向量搜索测试"
    echo "  -i, --integration 只运行集成测试"
    echo "  -p, --performance 只运行性能测试"
    echo "  -c, --clean       清理测试输出"
    echo ""
    echo "示例:"
    echo "  $0                运行所有测试"
    echo "  $0 -l             只运行Lucene测试"
    echo "  $0 -p             只运行性能测试"
}

# 函数：清理测试输出
clean_test_output() {
    echo "🧹 清理测试输出..."
    
    # 清理临时测试目录
    find /tmp -name "TelegramSearchBot_Test_*" -type d -exec rm -rf {} + 2>/dev/null || true
    
    # 清理项目输出目录
    if [ -d "$TEST_PROJECT_DIR/bin" ]; then
        rm -rf "$TEST_PROJECT_DIR/bin"
    fi
    
    if [ -d "$TEST_PROJECT_DIR/obj" ]; then
        rm -rf "$TEST_PROJECT_DIR/obj"
    fi
    
    echo "✅ 清理完成"
}

# 解析命令行参数
case "${1:-}" in
    -h|--help)
        show_help
        exit 0
        ;;
    -a|--all|"")
        echo "🎯 运行所有测试"
        # 运行所有测试
        run_tests "" "所有测试"
        ;;
    -l|--lucene)
        echo "🎯 运行Lucene搜索测试"
        run_tests "FullyQualifiedName~Lucene" "Lucene搜索测试"
        ;;
    -v|--vector)
        echo "🎯 运行向量搜索测试"
        run_tests "FullyQualifiedName~Vector" "向量搜索测试"
        ;;
    -i|--integration)
        echo "🎯 运行集成测试"
        run_tests "FullyQualifiedName~Integration" "集成测试"
        ;;
    -p|--performance)
        echo "🎯 运行性能测试"
        run_tests "FullyQualifiedName~Performance" "性能测试"
        ;;
    -c|--clean)
        clean_test_output
        exit 0
        ;;
    *)
        echo "❌ 未知选项: $1"
        echo ""
        show_help
        exit 1
        ;;
esac

# 显示测试结果摘要
echo ""
echo "📊 测试完成"
echo "================================"
echo "📁 测试项目: TelegramSearchBot.Search.Tests"
echo "🔧 测试框架: xUnit"
echo "📦 依赖: .NET 9.0, Lucene.NET, FAISS"
echo ""
echo "📖 更多信息请查看: $TEST_PROJECT_DIR/README.md"
echo ""

# 检查是否有测试失败
if [ $? -eq 0 ]; then
    echo "🎉 所有测试通过！"
    exit 0
else
    echo "⚠️  部分测试失败，请检查输出日志"
    exit 1
fi