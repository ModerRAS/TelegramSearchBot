#!/bin/bash

# TelegramSearchBot 性能测试运行脚本
# 用于自动化运行各种性能测试套件

set -e  # 遇到错误时退出

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# 项目根目录
PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TEST_PROJECT="$PROJECT_ROOT/TelegramSearchBot.Test/TelegramSearchBot.Test.csproj"
BENCHMARK_PROGRAM="TelegramSearchBot.Benchmarks.BenchmarkProgram"

# 输出目录
OUTPUT_DIR="$PROJECT_ROOT/BenchmarkResults"
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
RUN_OUTPUT_DIR="$OUTPUT_DIR/run_$TIMESTAMP"

# 日志文件
LOG_FILE="$RUN_OUTPUT_DIR/benchmark_run.log"

# 创建输出目录
mkdir -p "$RUN_OUTPUT_DIR"

# 日志函数
log() {
    echo -e "${GREEN}[$(date '+%Y-%m-%d %H:%M:%S')]${NC} $1" | tee -a "$LOG_FILE"
}

warn() {
    echo -e "${YELLOW}[$(date '+%Y-%m-%d %H:%M:%S')] WARNING:${NC} $1" | tee -a "$LOG_FILE"
}

error() {
    echo -e "${RED}[$(date '+%Y-%m-%d %H:%M:%S')] ERROR:${NC} $1" | tee -a "$LOG_FILE"
}

# 显示帮助信息
show_help() {
    echo -e "${BLUE}TelegramSearchBot 性能测试运行脚本${NC}"
    echo "==================================="
    echo
    echo "用法: $0 [选项] <测试类型>"
    echo
    echo "测试类型:"
    echo "  repository    - MessageRepository 性能测试"
    echo "  processing    - MessageProcessingPipeline 性能测试"
    echo "  search        - Lucene 搜索性能测试"
    echo "  vector        - FAISS 向量搜索性能测试"
    echo "  all           - 运行所有性能测试"
    echo "  quick         - 快速测试 (小数据集)"
    echo
    echo "选项:"
    echo "  -h, --help     显示帮助信息"
    echo "  -c, --config   指定配置文件路径"
    echo "  -o, --output   指定输出目录 (默认: $OUTPUT_DIR)"
    echo "  -r, --release  使用 Release 配置"
    echo "  -v, --verbose  详细输出"
    echo "  --no-clean     不清理临时文件"
    echo
    echo "环境要求:"
    echo "  - .NET 9.0 SDK"
    echo "  - BenchmarkDotNet 0.13.12"
    echo "  - 至少 4GB 内存"
    echo
    echo "示例:"
    echo "  $0 repository"
    echo "  $0 --release all"
    echo "  $0 -c custom-config.json search"
}

# 解析命令行参数
CONFIG_FILE="$PROJECT_ROOT/TelegramSearchBot.Test/Benchmarks/performance-config.json"
BUILD_CONFIG="Debug"
CLEANUP=true
VERBOSE=false
TEST_TYPE=""

while [[ $# -gt 0 ]]; do
    case $1 in
        -h|--help)
            show_help
            exit 0
            ;;
        -c|--config)
            CONFIG_FILE="$2"
            shift 2
            ;;
        -o|--output)
            OUTPUT_DIR="$2"
            RUN_OUTPUT_DIR="$OUTPUT_DIR/run_$TIMESTAMP"
            LOG_FILE="$RUN_OUTPUT_DIR/benchmark_run.log"
            mkdir -p "$RUN_OUTPUT_DIR"
            shift 2
            ;;
        -r|--release)
            BUILD_CONFIG="Release"
            shift
            ;;
        -v|--verbose)
            VERBOSE=true
            shift
            ;;
        --no-clean)
            CLEANUP=false
            shift
            ;;
        -*)
            error "未知选项: $1"
            show_help
            exit 1
            ;;
        *)
            TEST_TYPE="$1"
            shift
            ;;
    esac
done

# 检查测试类型
if [[ -z "$TEST_TYPE" ]]; then
    error "请指定测试类型"
    show_help
    exit 1
fi

# 验证配置文件
if [[ ! -f "$CONFIG_FILE" ]]; then
    error "配置文件不存在: $CONFIG_FILE"
    exit 1
fi

# 检查项目文件
if [[ ! -f "$TEST_PROJECT" ]]; then
    error "测试项目文件不存在: $TEST_PROJECT"
    exit 1
fi

# 检查 .NET SDK
if ! command -v dotnet &> /dev/null; then
    error "未找到 .NET SDK，请安装 .NET 9.0 SDK"
    exit 1
fi

# 获取 .NET 版本
DOTNET_VERSION=$(dotnet --version | head -n1)
log "发现 .NET 版本: $DOTNET_VERSION"

# 构建项目
log "构建测试项目 ($BUILD_CONFIG 配置)..."
cd "$PROJECT_ROOT"

if [[ "$VERBOSE" == true ]]; then
    dotnet build "$TEST_PROJECT" -c "$BUILD_CONFIG" --verbosity normal
else
    dotnet build "$TEST_PROJECT" -c "$BUILD_CONFIG" --verbosity minimal
fi

if [[ $? -ne 0 ]]; then
    error "项目构建失败"
    exit 1
fi

# 复制配置文件到输出目录
cp "$CONFIG_FILE" "$RUN_OUTPUT_DIR/config.json"

# 运行性能测试
run_benchmark() {
    local type="$1"
    local display_name="$2"
    
    log "开始运行 $display_name 性能测试..."
    
    local start_time=$(date +%s)
    local test_output_dir="$RUN_OUTPUT_DIR/$type"
    mkdir -p "$test_output_dir"
    
    # 运行测试
    if [[ "$VERBOSE" == true ]]; then
        dotnet run --project "$TEST_PROJECT" -c "$BUILD_CONFIG" -- \
            --configuration "$BUILD_CONFIG" \
            --artifactsPath "$test_output_dir" \
            "$type" 2>&1 | tee "$test_output_dir/benchmark_output.log"
    else
        dotnet run --project "$TEST_PROJECT" -c "$BUILD_CONFIG" -- \
            --configuration "$BUILD_CONFIG" \
            --artifactsPath "$test_output_dir" \
            "$type" > "$test_output_dir/benchmark_output.log" 2>&1
    fi
    
    local exit_code=$?
    local end_time=$(date +%s)
    local duration=$((end_time - start_time))
    
    if [[ $exit_code -eq 0 ]]; then
        log "$display_name 性能测试完成 (耗时: ${duration}s)"
        
        # 检查结果文件
        if [[ -f "$test_output_dir/results/BenchmarkDotNet.Artifacts/results.html" ]]; then
            log "测试结果已保存到: $test_output_dir/results/BenchmarkDotNet.Artifacts/results.html"
        fi
    else
        error "$display_name 性能测试失败 (耗时: ${duration}s)"
        return 1
    fi
}

# 根据测试类型运行相应的测试
case $TEST_TYPE in
    repository)
        run_benchmark "repository" "MessageRepository"
        ;;
    processing)
        run_benchmark "processing" "MessageProcessingPipeline"
        ;;
    search)
        run_benchmark "search" "Lucene搜索"
        ;;
    vector)
        run_benchmark "vector" "FAISS向量搜索"
        ;;
    all)
        log "运行完整的性能测试套件..."
        run_benchmark "repository" "MessageRepository"
        run_benchmark "processing" "MessageProcessingPipeline"
        run_benchmark "search" "Lucene搜索"
        run_benchmark "vector" "FAISS向量搜索"
        ;;
    quick)
        log "运行快速性能测试..."
        # 运行小数据集测试
        DOTNET_BENCHMARK_FILTER="*Small*" run_benchmark "repository" "MessageRepository (快速)"
        ;;
    *)
        error "未知的测试类型: $TEST_TYPE"
        show_help
        exit 1
        ;;
esac

# 生成摘要报告
generate_summary() {
    local summary_file="$RUN_OUTPUT_DIR/summary.md"
    
    cat > "$summary_file" << EOF
# TelegramSearchBot 性能测试摘要

**测试时间:** $(date '+%Y-%m-%d %H:%M:%S')  
**测试类型:** $TEST_TYPE  
**构建配置:** $BUILD_CONFIG  
**.NET 版本:** $DOTNET_VERSION  

## 测试结果概览

EOF

    # 遍历各个测试目录，提取关键指标
    for test_dir in "$RUN_OUTPUT_DIR"/*/; do
        if [[ -d "$test_dir" && "$test_dir" != "$RUN_OUTPUT_DIR/run_"* ]]; then
            test_name=$(basename "$test_dir")
            results_file="$test_dir/results/BenchmarkDotNet.Artifacts/results.csv"
            
            if [[ -f "$results_file" ]]; then
                echo "### $test_name" >> "$summary_file"
                echo "" >> "$summary_file"
                
                # 提取关键指标 (简化处理)
                echo "| 测试 | 平均时间 | 内存分配 | 每秒操作数 |" >> "$summary_file"
                echo "|------|----------|----------|------------|" >> "$summary_file"
                
                # 这里可以添加更详细的结果解析
                echo "| 待解析 | 待解析 | 待解析 | 待解析 |" >> "$summary_file"
                echo "" >> "$summary_file"
            fi
        fi
    done
    
    cat >> "$summary_file" << EOF

## 文件结构

- \`config.json\` - 测试配置文件
- \`benchmark_run.log\` - 完整的运行日志
- \`[测试类型]/\` - 各个测试的结果目录
  - \`benchmark_output.log\` - 测试输出日志
  - \`results/BenchmarkDotNet.Artifacts/\` - BenchmarkDotNet 生成的结果文件

## 注意事项

1. 性能测试结果受系统当前负载影响，建议在相对空闲的环境中运行
2. 内存使用量可能因垃圾回收而有所波动
3. 向量搜索测试使用模拟数据，实际性能可能因LLM服务而异
4. 建议多次运行测试以获得稳定的性能数据

---
*此报告由性能测试脚本自动生成*
EOF

    log "摘要报告已生成: $summary_file"
}

generate_summary

# 清理临时文件
if [[ "$CLEANUP" == true ]]; then
    log "清理临时文件..."
    # 清理逻辑可以根据需要添加
fi

log "性能测试完成!"
log "结果目录: $RUN_OUTPUT_DIR"
log "摘要报告: $RUN_OUTPUT_DIR/summary.md"

# 显示快速结果
echo
echo -e "${BLUE}=== 快速结果概览 ===${NC}"
echo "测试类型: $TEST_TYPE"
echo "输出目录: $RUN_OUTPUT_DIR"
echo "配置文件: $CONFIG_FILE"
echo "构建配置: $BUILD_CONFIG"
echo
echo -e "${GREEN}测试完成! 查看详细结果:${NC}"
echo "  - 完整日志: $LOG_FILE"
echo "  - 摘要报告: $RUN_OUTPUT_DIR/summary.md"
echo "  - 结果目录: $RUN_OUTPUT_DIR"