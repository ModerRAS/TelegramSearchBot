#!/bin/bash

# Controller测试运行脚本
# 运行所有Controller相关的单元测试和集成测试

echo "🧪 开始运行Controller层测试..."
echo "================================"

# 设置颜色输出
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# 计数器
TOTAL_TESTS=0
PASSED_TESTS=0
FAILED_TESTS=0

# 运行测试函数
run_test() {
    local test_name=$1
    local test_filter=$2
    
    echo -e "${YELLOW}运行测试: $test_name${NC}"
    
    if dotnet test TelegramSearchBot.sln --filter "$test_filter" --verbosity normal --no-build; then
        echo -e "${GREEN}✅ $test_name 通过${NC}"
        ((PASSED_TESTS++))
    else
        echo -e "${RED}❌ $test_name 失败${NC}"
        ((FAILED_TESTS++))
    fi
    
    ((TOTAL_TESTS++))
    echo "--------------------------------"
}

# 1. 基础Controller结构测试
run_test "Controller基础结构测试" "FullyQualifiedName~ControllerBasicTests"

# 2. MessageController测试
run_test "MessageController测试" "FullyQualifiedName~MessageControllerTests"

# 3. SearchController测试
run_test "SearchController测试" "FullyQualifiedName~SearchControllerTests"

# 4. AI Controller测试
run_test "AutoOCRController测试" "FullyQualifiedName~AutoOCRControllerTests"

# 5. Bilibili Controller测试
run_test "BiliMessageController测试" "FullyQualifiedName~BiliMessageControllerTests"

# 6. Controller集成测试
run_test "Controller集成测试" "FullyQualifiedName~ControllerIntegrationTests"

# 输出总结
echo ""
echo "================================"
echo -e "${YELLOW}测试总结${NC}"
echo "================================"
echo -e "总测试数: ${YELLOW}$TOTAL_TESTS${NC}"
echo -e "通过: ${GREEN}$PASSED_TESTS${NC}"
echo -e "失败: ${RED}$FAILED_TESTS${NC}"

if [ $FAILED_TESTS -eq 0 ]; then
    echo -e "${GREEN}🎉 所有Controller测试通过！${NC}"
    exit 0
else
    echo -e "${RED}⚠️  有 $FAILED_TESTS 个测试失败${NC}"
    exit 1
fi