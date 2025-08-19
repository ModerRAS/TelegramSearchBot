#!/bin/bash

# TDD测试运行脚本 - TelegramSearchBot项目
# 用于运行Message领域的单元测试和集成测试

set -e

echo "=========================================="
echo "TelegramSearchBot TDD 测试运行脚本"
echo "=========================================="

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# 日志函数
log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

log_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# 检查依赖
check_dependencies() {
    log_info "检查依赖..."
    
    if ! command -v dotnet &> /dev/null; then
        log_error ".NET SDK 未安装"
        exit 1
    fi
    
    dotnet_version=$(dotnet --version)
    log_success "找到 .NET SDK 版本: $dotnet_version"
    
    # 检查项目文件
    if [ ! -f "TelegramSearchBot.sln" ]; then
        log_error "未找到解决方案文件 TelegramSearchBot.sln"
        exit 1
    fi
    
    log_success "依赖检查完成"
}

# 恢复依赖
restore_dependencies() {
    log_info "恢复NuGet包依赖..."
    dotnet restore TelegramSearchBot.sln
    log_success "依赖恢复完成"
}

# 构建项目
build_project() {
    log_info "构建项目..."
    dotnet build TelegramSearchBot.sln --configuration Release
    log_success "项目构建完成"
}

# 运行单元测试
run_unit_tests() {
    log_info "运行单元测试..."
    
    # 运行Message领域测试
    log_info "运行Message领域单元测试..."
    dotnet test TelegramSearchBot.Test.csproj \
        --configuration Release \
        --filter "Category=Unit" \
        --logger "console;verbosity=detailed" \
        --collect:"XPlat Code Coverage"
    
    if [ $? -eq 0 ]; then
        log_success "单元测试全部通过"
    else
        log_error "单元测试失败"
        exit 1
    fi
}

# 运行集成测试
run_integration_tests() {
    log_info "运行集成测试..."
    
    # 运行Message领域集成测试
    log_info "运行Message领域集成测试..."
    dotnet test TelegramSearchBot.Test.csproj \
        --configuration Release \
        --filter "Category=Integration" \
        --logger "console;verbosity=detailed"
    
    if [ $? -eq 0 ]; then
        log_success "集成测试全部通过"
    else
        log_warning "部分集成测试失败"
    fi
}

# 运行特定测试类别
run_specific_tests() {
    local test_category=$1
    log_info "运行特定测试类别: $test_category"
    
    dotnet test TelegramSearchBot.Test.csproj \
        --configuration Release \
        --filter "Category=$test_category" \
        --logger "console;verbosity=detailed"
}

# 生成测试覆盖率报告
generate_coverage_report() {
    log_info "生成测试覆盖率报告..."
    
    # 安装reportgenerator（如果未安装）
    if ! command -v reportgenerator &> /dev/null; then
        log_info "安装reportgenerator..."
        dotnet tool install -g dotnet-reportgenerator-globaltool
    fi
    
    # 生成覆盖率报告
    reportgenerator \
        -reports:coverage.xml \
        -targetdir:coverage-report \
        -reporttypes:Html \
        -title:"TelegramSearchBot 测试覆盖率报告"
    
    log_success "覆盖率报告已生成到 coverage-report/index.html"
}

# 运行性能测试
run_performance_tests() {
    log_info "运行性能测试..."
    
    dotnet test TelegramSearchBot.Test.csproj \
        --configuration Release \
        --filter "Category=Performance" \
        --logger "console;verbosity=detailed"
}

# 清理测试数据
cleanup_test_data() {
    log_info "清理测试数据..."
    
    # 删除测试生成的文件
    rm -rf coverage-report/
    rm -f coverage.xml
    rm -f TestResults/
    
    log_success "测试数据清理完成"
}

# 显示帮助信息
show_help() {
    echo "用法: $0 [选项]"
    echo ""
    echo "选项:"
    echo "  -h, --help              显示帮助信息"
    echo "  -u, --unit             只运行单元测试"
    echo "  -i, --integration      只运行集成测试"
    echo "  -p, --performance      只运行性能测试"
    echo "  -c, --category <name>  运行特定测试类别"
    echo "  -f, --full             运行完整测试套件（默认）"
    echo "  --coverage             生成覆盖率报告"
    echo "  --clean                清理测试数据"
    echo ""
    echo "示例:"
    echo "  $0                    # 运行完整测试套件"
    echo "  $0 --unit            # 只运行单元测试"
    echo "  $0 --category Message # 运行Message类别测试"
    echo "  $0 --coverage        # 生成覆盖率报告"
}

# 主函数
main() {
    local mode="full"
    
    # 解析命令行参数
    while [[ $# -gt 0 ]]; do
        case $1 in
            -h|--help)
                show_help
                exit 0
                ;;
            -u|--unit)
                mode="unit"
                shift
                ;;
            -i|--integration)
                mode="integration"
                shift
                ;;
            -p|--performance)
                mode="performance"
                shift
                ;;
            -c|--category)
                mode="category"
                category_name="$2"
                shift 2
                ;;
            -f|--full)
                mode="full"
                shift
                ;;
            --coverage)
                mode="coverage"
                shift
                ;;
            --clean)
                cleanup_test_data
                exit 0
                ;;
            *)
                log_error "未知选项: $1"
                show_help
                exit 1
                ;;
        esac
    done
    
    echo "开始执行TDD测试流程..."
    echo "模式: $mode"
    echo ""
    
    # 检查依赖
    check_dependencies
    
    # 恢复依赖
    restore_dependencies
    
    # 构建项目
    build_project
    
    # 根据模式执行测试
    case $mode in
        "unit")
            run_unit_tests
            ;;
        "integration")
            run_integration_tests
            ;;
        "performance")
            run_performance_tests
            ;;
        "category")
            if [ -z "$category_name" ]; then
                log_error "请指定测试类别名称"
                exit 1
            fi
            run_specific_tests "$category_name"
            ;;
        "coverage")
            run_unit_tests
            generate_coverage_report
            ;;
        "full")
            run_unit_tests
            run_integration_tests
            run_performance_tests
            generate_coverage_report
            ;;
    esac
    
    echo ""
    log_success "TDD测试流程完成！"
    
    # 显示总结
    echo "=========================================="
    echo "测试总结"
    echo "=========================================="
    echo "执行模式: $mode"
    echo "执行时间: $(date)"
    echo ""
    
    if [ "$mode" = "coverage" ] || [ "$mode" = "full" ]; then
        echo "覆盖率报告: coverage-report/index.html"
    fi
}

# 运行主函数
main "$@"