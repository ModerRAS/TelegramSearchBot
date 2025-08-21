#!/bin/bash

# TelegramSearchBot.Test 编译错误修复脚本 - Bash版本
# 简化版本，专注于修复最关键的编译错误

set -e  # 遇到错误立即退出

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

# 文件备份函数
backup_file() {
    local filepath="$1"
    if [[ -f "$filepath" ]]; then
        local backup_path="${filepath}.backup_$(date +%Y%m%d_%H%M%S)"
        cp "$filepath" "$backup_path"
        log_info "已备份文件: $filepath -> $backup_path"
        echo "$backup_path"
        return 0
    fi
    return 1
}

# 修复AltPhotoControllerTests构造函数参数
fix_alt_photo_controller_tests() {
    local filepath="$1"
    
    log_info "修复AltPhotoControllerTests构造函数参数: $filepath"
    
    if [[ ! -f "$filepath" ]]; then
        log_error "文件不存在: $filepath"
        return 1
    fi
    
    local backup
    backup=$(backup_file "$filepath")
    
    if [[ "$WHAT_IF" == "true" ]]; then
        log_info "WhatIf模式：跳过实际修改"
        return 0
    fi
    
    # 使用sed进行修复
    # 修复构造函数调用，确保参数正确
    sed -i 's/return new AltPhotoController(\s*BotClientMock\.Object,\s*_generalLLMServiceMock\.Object,\s*SendMessageServiceMock\.Object,\s*MessageServiceMock\.Object,\s*_loggerMock\.Object,\s*MessageExtensionServiceMock\.Object\s*);/return new AltPhotoController(\n                BotClientMock.Object,\n                _generalLLMServiceMock.Object,\n                SendMessageServiceMock.Object,\n                MessageServiceMock.Object,\n                _loggerMock.Object,\n                _sendMessageMock.Object,\n                MessageExtensionServiceMock.Object\n            );/g' "$filepath"
    
    log_success "成功修复AltPhotoControllerTests"
    return 0
}

# 修复MessageSearchQueriesTests参数顺序
fix_message_search_queries_tests() {
    local filepath="$1"
    
    log_info "修复MessageSearchQueriesTests参数顺序: $filepath"
    
    if [[ ! -f "$filepath" ]]; then
        log_error "文件不存在: $filepath"
        return 1
    fi
    
    local backup
    backup=$(backup_file "$filepath")
    
    if [[ "$WHAT_IF" == "true" ]]; then
        log_info "WhatIf模式：跳过实际修改"
        return 0
    fi
    
    # 修复MessageSearchByUserQuery构造函数调用
    sed -i 's/new MessageSearchByUserQuery(groupId, userId, limit)/new MessageSearchByUserQuery(groupId, userId, "", limit)/g' "$filepath"
    
    # 修复MessageSearchByDateRangeQuery构造函数调用
    sed -i 's/new MessageSearchByDateRangeQuery(groupId, startDate, endDate, limit)/new MessageSearchByDateRangeQuery(groupId, startDate, endDate, "", limit)/g' "$filepath"
    
    log_success "成功修复MessageSearchQueriesTests"
    return 0
}

# 修复MessageSearchRepositoryTests中的SearchResult类型问题
fix_message_search_repository_tests() {
    local filepath="$1"
    
    log_info "修复MessageSearchRepositoryTests的SearchResult类型问题: $filepath"
    
    if [[ ! -f "$filepath" ]]; then
        log_error "文件不存在: $filepath"
        return 1
    fi
    
    local backup
    backup=$(backup_file "$filepath")
    
    if [[ "$WHAT_IF" == "true" ]]; then
        log_info "WhatIf模式：跳过实际修改"
        return 0
    fi
    
    # 添加缺失的using语句
    if ! grep -q "using TelegramSearchBot.Model.Data;" "$filepath"; then
        sed -i '/using System.Threading.Tasks;/a\using TelegramSearchBot.Model.Data;' "$filepath"
    fi
    
    # 替换SearchResult为Message
    sed -i 's/List<SearchResult>/List<Message>/g' "$filepath"
    sed -i 's/new SearchResult { GroupId = 100L, MessageId = 1000L, Content = "test search result", DateTime = DateTime.UtcNow, Score = 0.85f }/new Message { GroupId = 100L, MessageId = 1000L, Content = "test search result", DateTime = DateTime.UtcNow }/g' "$filepath"
    sed -i 's/new SearchResult { GroupId = 100L, MessageId = 1001L, Content = "another test result", DateTime = DateTime.UtcNow, Score = 0.75f }/new Message { GroupId = 100L, MessageId = 1001L, Content = "another test result", DateTime = DateTime.UtcNow }/g' "$filepath"
    
    # 修复LuceneManager.Search调用参数顺序
    sed -i 's/m\.Search(query\.GroupId, query\.Query, query\.Limit)/m.Search(query.Query, query.GroupId, 0, query.Limit)/g' "$filepath"
    
    # 替换SearchDocument为Message
    sed -i 's/SearchDocument/Message/g' "$filepath"
    
    log_success "成功修复MessageSearchRepositoryTests"
    return 0
}

# 修复TestDataSet.Initialize方法
fix_test_dataset_initialize() {
    local filepath="$1"
    
    log_info "修复TestDataSet.Initialize方法: $filepath"
    
    if [[ ! -f "$filepath" ]]; then
        log_error "文件不存在: $filepath"
        return 1
    fi
    
    local backup
    backup=$(backup_file "$filepath")
    
    if [[ "$WHAT_IF" == "true" ]]; then
        log_info "WhatIf模式：跳过实际修改"
        return 0
    fi
    
    # 检查是否已经存在Initialize方法
    if ! grep -q "public void Initialize(DataDbContext context)" "$filepath"; then
        # 创建临时文件
        local temp_file="${filepath}.tmp"
        
        # 提取文件内容并替换TestDataSet类
        awk '
        BEGIN { in_class = 0; class_content = "" }
        /^public class TestDataSet/ {
            in_class = 1
            print "    public class TestDataSet"
            print "    {"
            print "        public List<Message> Messages { get; set; } = new List<Message>();"
            print "        public List<UserData> Users { get; set; } = new List<UserData>();"
            print "        public List<GroupData> Groups { get; set; } = new List<GroupData>();"
            print "        public List<MessageExtension> Extensions { get; set; } = new List<MessageExtension>();"
            print ""
            print "        /// <summary>"
            print "        /// 初始化测试数据到数据库"
            print "        /// </summary>"
            print "        public void Initialize(DataDbContext context)"
            print "        {"
            print "            // 添加用户数据"
            print "            if (Users.Any())"
            print "            {"
            print "                context.UserData.AddRange(Users);"
            print "            }"
            print ""
            print "            // 添加群组数据"
            print "            if (Groups.Any())"
            print "            {"
            print "                context.GroupData.AddRange(Groups);"
            print "            }"
            print ""
            print "            // 添加消息数据"
            print "            if (Messages.Any())"
            print "            {"
            print "                context.Message.AddRange(Messages);"
            print "            }"
            print ""
            print "            // 添加扩展数据"
            print "            if (Extensions.Any())"
            print "            {"
            print "                context.MessageExtension.AddRange(Extensions);"
            print "            }"
            print ""
            print "            context.SaveChanges();"
            print "        }"
            print "    }"
            next
        }
        in_class && /^    }$/ {
            in_class = 0
            next
        }
        !in_class { print }
        ' "$filepath" > "$temp_file"
        
        mv "$temp_file" "$filepath"
    fi
    
    log_success "成功修复TestDataSet.Initialize方法"
    return 0
}

# 修复QuickPerformanceBenchmarks中的User和Chat类型问题
fix_quick_performance_benchmarks() {
    local filepath="$1"
    
    log_info "修复QuickPerformanceBenchmarks中的User和Chat类型问题: $filepath"
    
    if [[ ! -f "$filepath" ]]; then
        log_error "文件不存在: $filepath"
        return 1
    fi
    
    local backup
    backup=$(backup_file "$filepath")
    
    if [[ "$WHAT_IF" == "true" ]]; then
        log_info "WhatIf模式：跳过实际修改"
        return 0
    fi
    
    # 添加缺失的using语句
    if ! grep -q "using Telegram.Bot.Types;" "$filepath"; then
        sed -i '/using System.Threading.Tasks;/a\using Telegram.Bot.Types;' "$filepath"
    fi
    
    # 修复User和Chat类型引用
    sed -i 's/var user = new User();/var user = new Telegram.Bot.Types.User();/g' "$filepath"
    sed -i 's/var chat = new Chat();/var chat = new Telegram.Bot.Types.Chat();/g' "$filepath"
    
    log_success "成功修复QuickPerformanceBenchmarks"
    return 0
}

# 修复MessageProcessingBenchmarks中的CreateLongMessage方法调用
fix_message_processing_benchmarks() {
    local filepath="$1"
    
    log_info "修复MessageProcessingBenchmarks中的CreateLongMessage方法调用: $filepath"
    
    if [[ ! -f "$filepath" ]]; then
        log_error "文件不存在: $filepath"
        return 1
    fi
    
    local backup
    backup=$(backup_file "$filepath")
    
    if [[ "$WHAT_IF" == "true" ]]; then
        log_info "WhatIf模式：跳过实际修改"
        return 0
    fi
    
    # 修复CreateLongMessage方法调用
    sed -i 's/CreateLongMessage(userId: userId)/CreateLongMessage()/g' "$filepath"
    
    log_success "成功修复MessageProcessingBenchmarks"
    return 0
}

# 修复IntegrationTestBase中的配置问题
fix_integration_test_base() {
    local filepath="$1"
    
    log_info "修复IntegrationTestBase中的配置问题: $filepath"
    
    if [[ ! -f "$filepath" ]]; then
        log_error "文件不存在: $filepath"
        return 1
    fi
    
    local backup
    backup=$(backup_file "$filepath")
    
    if [[ "$WHAT_IF" == "true" ]]; then
        log_info "WhatIf模式：跳过实际修改"
        return 0
    fi
    
    # 修复Dictionary<string, string>到Dictionary<string, string?>的转换问题
    sed -i 's/new Dictionary<string, string>/new Dictionary<string, string?>/g' "$filepath"
    
    log_success "成功修复IntegrationTestBase"
    return 0
}

# 显示帮助信息
show_help() {
    echo "TelegramSearchBot.Test 编译错误修复脚本"
    echo ""
    echo "用法: $0 [选项]"
    echo ""
    echo "选项:"
    echo "  -p, --path PATH     项目路径 (默认: 当前目录)"
    echo "  -w, --what-if       预览模式，不实际修改文件"
    echo "  -v, --verbose       详细输出"
    echo "  -h, --help          显示帮助信息"
    echo ""
    echo "示例:"
    echo "  $0                    # 使用默认设置"
    echo "  $0 -w                 # 预览模式"
    echo "  $0 -p /path/to/project  # 指定项目路径"
}

# 主函数
main() {
    local project_path="."
    local what_if=false
    local verbose=false
    
    # 解析命令行参数
    while [[ $# -gt 0 ]]; do
        case $1 in
            -p|--path)
                project_path="$2"
                shift 2
                ;;
            -w|--what-if)
                what_if=true
                shift
                ;;
            -v|--verbose)
                verbose=true
                shift
                ;;
            -h|--help)
                show_help
                exit 0
                ;;
            *)
                log_error "未知选项: $1"
                show_help
                exit 1
                ;;
        esac
    done
    
    # 设置环境变量
    WHAT_IF="$what_if"
    VERBOSE="$verbose"
    
    log_info "开始修复TelegramSearchBot.Test编译错误"
    log_info "项目路径: $project_path"
    
    if [[ "$what_if" == "true" ]]; then
        log_warning "WhatIf模式启用 - 只显示将要执行的操作"
    fi
    
    local test_project_path="${project_path}/TelegramSearchBot.Test"
    
    if [[ ! -d "$test_project_path" ]]; then
        log_error "测试项目路径不存在: $test_project_path"
        exit 1
    fi
    
    # 定义修复任务
    declare -a fixes=(
        "AltPhotoControllerTests:Controller/AI/LLM/AltPhotoControllerTests.cs:fix_alt_photo_controller_tests"
        "MessageSearchQueriesTests:Domain/Message/ValueObjects/MessageSearchQueriesTests.cs:fix_message_search_queries_tests"
        "MessageSearchRepositoryTests:Infrastructure/Search/Repositories/MessageSearchRepositoryTests.cs:fix_message_search_repository_tests"
        "TestDataSetInitialize:Helpers/TestDatabaseHelper.cs:fix_test_dataset_initialize"
        "QuickPerformanceBenchmarks:Benchmarks/Quick/QuickPerformanceBenchmarks.cs:fix_quick_performance_benchmarks"
        "MessageProcessingBenchmarks:Benchmarks/Domain/Message/MessageProcessingBenchmarks.cs:fix_message_processing_benchmarks"
        "IntegrationTestBase:Base/IntegrationTestBase.cs:fix_integration_test_base"
    )
    
    local success_count=0
    local total_count=${#fixes[@]}
    
    for fix in "${fixes[@]}"; do
        IFS=':' read -r name path function <<< "$fix"
        
        log_info "正在修复: $name"
        
        local file_path="${test_project_path}/${path}"
        
        if [[ -f "$file_path" ]]; then
            if $function "$file_path"; then
                ((success_count++))
                log_success "✓ 修复成功: $name"
            else
                log_error "✗ 修复失败: $name"
            fi
        else
            log_warning "? 文件不存在，跳过: $file_path"
        fi
    done
    
    log_info "修复完成: $success_count/$total_count 个修复成功"
    
    if [[ $success_count -eq $total_count ]]; then
        log_success "所有编译错误已修复！"
        
        # 验证修复结果
        if [[ "$what_if" != "true" ]]; then
            log_info "验证修复结果..."
            if cd "$test_project_path"; then
                if dotnet build --verbosity quiet 2>&1; then
                    log_success "项目编译成功！"
                else
                    log_warning "项目仍有编译错误"
                    # 显示剩余的错误
                    dotnet build --verbosity quiet 2>&1 | grep "error CS" | head -10 || true
                fi
                cd - > /dev/null
            fi
        fi
    else
        log_error "部分修复失败"
        exit 1
    fi
}

# 执行主函数
main "$@"