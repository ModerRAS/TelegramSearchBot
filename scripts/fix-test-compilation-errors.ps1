# TelegramSearchBot.Test 编译错误修复脚本
# 此脚本用于修复测试项目中的常见编译错误

param(
    [string]$ProjectPath = ".",
    [switch]$WhatIf,
    [switch]$Verbose
)

# 设置错误处理
$ErrorActionPreference = "Stop"

# 日志函数
function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Write-Host "[$timestamp] [$Level] $Message" -ForegroundColor $(switch($Level) {
        "ERROR" { "Red" }
        "WARNING" { "Yellow" }
        "SUCCESS" { "Green" }
        default { "White" }
    })
}

# 文件备份函数
function Backup-File {
    param([string]$FilePath)
    if (Test-Path $FilePath) {
        $backupPath = "$FilePath.backup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
        Copy-Item $FilePath $backupPath
        Write-Log "已备份文件: $FilePath -> $backupPath" "INFO"
        return $backupPath
    }
    return $null
}

# 修复AltPhotoControllerTests构造函数参数
function Fix-AltPhotoControllerTests {
    param([string]$FilePath)
    
    Write-Log "修复AltPhotoControllerTests构造函数参数: $FilePath"
    
    if (-not (Test-Path $FilePath)) {
        Write-Log "文件不存在: $FilePath" "ERROR"
        return $false
    }
    
    $backup = Backup-File $FilePath
    if ($WhatIf) { return $true }
    
    try {
        $content = Get-Content $FilePath -Raw
        
        # 修复构造函数调用，添加缺失的messageExtensionService参数
        $pattern = 'new AltPhotoController\(\s*BotClientMock\.Object,\s*_generalLLMServiceMock\.Object,\s*SendMessageServiceMock\.Object,\s*MessageServiceMock\.Object,\s*_loggerMock\.Object,\s*MessageExtensionServiceMock\.Object\s*\)'
        $replacement = 'new AltPhotoController(
                BotClientMock.Object,
                _generalLLMServiceMock.Object,
                SendMessageServiceMock.Object,
                MessageServiceMock.Object,
                _loggerMock.Object,
                _sendMessageMock.Object,
                MessageExtensionServiceMock.Object
            )'
        
        $content = $content -replace $pattern, $replacement
        
        Set-Content $FilePath $content -NoNewline
        Write-Log "成功修复AltPhotoControllerTests构造函数参数" "SUCCESS"
        return $true
    }
    catch {
        Write-Log "修复AltPhotoControllerTests失败: $_" "ERROR"
        if ($backup) { Copy-Item $backup $FilePath -Force }
        return $false
    }
}

# 修复MessageSearchQueriesTests参数顺序
function Fix-MessageSearchQueriesTests {
    param([string]$FilePath)
    
    Write-Log "修复MessageSearchQueriesTests参数顺序: $FilePath"
    
    if (-not (Test-Path $FilePath)) {
        Write-Log "文件不存在: $FilePath" "ERROR"
        return $false
    }
    
    $backup = Backup-File $FilePath
    if ($WhatIf) { return $true }
    
    try {
        $content = Get-Content $FilePath -Raw
        
        # 修复MessageSearchByUserQuery构造函数调用
        $pattern1 = 'new MessageSearchByUserQuery\(groupId, userId, limit\)'
        $replacement1 = 'new MessageSearchByUserQuery(groupId, userId, "", limit)'
        
        $content = $content -replace $pattern1, $replacement1
        
        # 修复MessageSearchByDateRangeQuery构造函数调用
        $pattern2 = 'new MessageSearchByDateRangeQuery\(groupId, startDate, endDate, limit\)'
        $replacement2 = 'new MessageSearchByDateRangeQuery(groupId, startDate, endDate, "", limit)'
        
        $content = $content -replace $pattern2, $replacement2
        
        Set-Content $FilePath $content -NoNewline
        Write-Log "成功修复MessageSearchQueriesTests参数顺序" "SUCCESS"
        return $true
    }
    catch {
        Write-Log "修复MessageSearchQueriesTests失败: $_" "ERROR"
        if ($backup) { Copy-Item $backup $FilePath -Force }
        return $false
    }
}

# 修复MessageSearchRepositoryTests中的SearchResult类型问题
function Fix-MessageSearchRepositoryTests {
    param([string]$FilePath)
    
    Write-Log "修复MessageSearchRepositoryTests的SearchResult类型问题: $FilePath"
    
    if (-not (Test-Path $FilePath)) {
        Write-Log "文件不存在: $FilePath" "ERROR"
        return $false
    }
    
    $backup = Backup-File $FilePath
    if ($WhatIf) { return $true }
    
    try {
        $content = Get-Content $FilePath -Raw
        
        # 添加缺失的using语句
        if ($content -notmatch 'using TelegramSearchBot\.Model\.Data;') {
            $content = $content -replace '(using System\.Threading\.Tasks;)', "`$1`r`nusing TelegramSearchBot.Model.Data;"
        }
        
        # 修复SearchResult类型引用 - 替换为Message
        $pattern1 = 'new List<SearchResult>'
        $replacement1 = 'new List<Message>'
        
        $content = $content -replace $pattern1, $replacement1
        
        # 修复SearchResult初始化
        $pattern2 = 'new SearchResult \{ GroupId = 100L, MessageId = 1000L, Content = "test search result", DateTime = DateTime\.UtcNow, Score = 0\.85f \}'
        $replacement2 = 'new Message { GroupId = 100L, MessageId = 1000L, Content = "test search result", DateTime = DateTime.UtcNow }'
        
        $content = $content -replace $pattern2, $replacement2
        
        $pattern3 = 'new SearchResult \{ GroupId = 100L, MessageId = 1001L, Content = "another test result", DateTime = DateTime\.UtcNow, Score = 0\.75f \}'
        $replacement3 = 'new Message { GroupId = 100L, MessageId = 1001L, Content = "another test result", DateTime = DateTime.UtcNow }'
        
        $content = $content -replace $pattern3, $replacement3
        
        # 修复LuceneManager.Search调用参数顺序
        $pattern4 = '_mockLuceneManager\.Setup\(m => m\.Search\(query\.GroupId, query\.Query, query\.Limit\)\)'
        $replacement4 = '_mockLuceneManager.Setup(m => m.Search(query.Query, query.GroupId, 0, query.Limit))'
        
        $content = $content -replace $pattern4, $replacement4
        
        # 修复SearchDocument类型引用
        $pattern5 = 'SearchDocument'
        $replacement5 = 'Message'
        
        $content = $content -replace $pattern5, $replacement5
        
        Set-Content $FilePath $content -NoNewline
        Write-Log "成功修复MessageSearchRepositoryTests的SearchResult类型问题" "SUCCESS"
        return $true
    }
    catch {
        Write-Log "修复MessageSearchRepositoryTests失败: $_" "ERROR"
        if ($backup) { Copy-Item $backup $FilePath -Force }
        return $false
    }
}

# 修复TestDataSet.Initialize方法
function Fix-TestDataSetInitialize {
    param([string]$FilePath)
    
    Write-Log "修复TestDataSet.Initialize方法: $FilePath"
    
    if (-not (Test-Path $FilePath)) {
        Write-Log "文件不存在: $FilePath" "ERROR"
        return $false
    }
    
    $backup = Backup-File $FilePath
    if ($WhatIf) { return $true }
    
    try {
        $content = Get-Content $FilePath -Raw
        
        # 在TestDataSet类中添加Initialize方法
        $pattern = 'public class TestDataSet\s*\{[^}]*\}'
        $replacement = 'public class TestDataSet
    {
        public List<Message> Messages { get; set; } = new List<Message>();
        public List<UserData> Users { get; set; } = new List<UserData>();
        public List<GroupData> Groups { get; set; } = new List<GroupData>();
        public List<MessageExtension> Extensions { get; set; } = new List<MessageExtension>();

        /// <summary>
        /// 初始化测试数据到数据库
        /// </summary>
        public void Initialize(DataDbContext context)
        {
            // 添加用户数据
            if (Users.Any())
            {
                context.UserData.AddRange(Users);
            }

            // 添加群组数据
            if (Groups.Any())
            {
                context.GroupData.AddRange(Groups);
            }

            // 添加消息数据
            if (Messages.Any())
            {
                context.Message.AddRange(Messages);
            }

            // 添加扩展数据
            if (Extensions.Any())
            {
                context.MessageExtension.AddRange(Extensions);
            }

            context.SaveChanges();
        }
    }'
        
        $content = $content -replace $pattern, $replacement
        
        Set-Content $FilePath $content -NoNewline
        Write-Log "成功修复TestDataSet.Initialize方法" "SUCCESS"
        return $true
    }
    catch {
        Write-Log "修复TestDataSet.Initialize方法失败: $_" "ERROR"
        if ($backup) { Copy-Item $backup $FilePath -Force }
        return $false
    }
}

# 修复QuickPerformanceBenchmarks中的User和Chat类型问题
function Fix-QuickPerformanceBenchmarks {
    param([string]$FilePath)
    
    Write-Log "修复QuickPerformanceBenchmarks中的User和Chat类型问题: $FilePath"
    
    if (-not (Test-Path $FilePath)) {
        Write-Log "文件不存在: $FilePath" "ERROR"
        return $false
    }
    
    $backup = Backup-File $FilePath
    if ($WhatIf) { return $true }
    
    try {
        $content = Get-Content $FilePath -Raw
        
        # 添加缺失的using语句
        if ($content -notmatch 'using Telegram\.Bot\.Types;') {
            $content = $content -replace '(using System\.Threading\.Tasks;)', "`$1`r`nusing Telegram.Bot.Types;"
        }
        
        # 修复User和Chat类型引用 - 添加完全限定名
        $pattern1 = 'var user = new User\(\);'
        $replacement1 = 'var user = new Telegram.Bot.Types.User();'
        
        $content = $content -replace $pattern1, $replacement1
        
        $pattern2 = 'var chat = new Chat\(\);'
        $replacement2 = 'var chat = new Telegram.Bot.Types.Chat();'
        
        $content = $content -replace $pattern2, $replacement2
        
        Set-Content $FilePath $content -NoNewline
        Write-Log "成功修复QuickPerformanceBenchmarks中的User和Chat类型问题" "SUCCESS"
        return $true
    }
    catch {
        Write-Log "修复QuickPerformanceBenchmarks失败: $_" "ERROR"
        if ($backup) { Copy-Item $backup $FilePath -Force }
        return $false
    }
}

# 修复MessageProcessingBenchmarks中的CreateLongMessage方法调用
function Fix-MessageProcessingBenchmarks {
    param([string]$FilePath)
    
    Write-Log "修复MessageProcessingBenchmarks中的CreateLongMessage方法调用: $FilePath"
    
    if (-not (Test-Path $FilePath)) {
        Write-Log "文件不存在: $FilePath" "ERROR"
        return $false
    }
    
    $backup = Backup-File $FilePath
    if ($WhatIf) { return $true }
    
    try {
        $content = Get-Content $FilePath -Raw
        
        # 修复CreateLongMessage方法调用 - 移除userId参数
        $pattern = 'CreateLongMessage\(userId: userId\)'
        $replacement = 'CreateLongMessage()'
        
        $content = $content -replace $pattern, $replacement
        
        Set-Content $FilePath $content -NoNewline
        Write-Log "成功修复MessageProcessingBenchmarks中的CreateLongMessage方法调用" "SUCCESS"
        return $true
    }
    catch {
        Write-Log "修复MessageProcessingBenchmarks失败: $_" "ERROR"
        if ($backup) { Copy-Item $backup $FilePath -Force }
        return $false
    }
}

# 修复IntegrationTestBase中的配置问题
function Fix-IntegrationTestBase {
    param([string]$FilePath)
    
    Write-Log "修复IntegrationTestBase中的配置问题: $FilePath"
    
    if (-not (Test-Path $FilePath)) {
        Write-Log "文件不存在: $FilePath" "ERROR"
        return $false
    }
    
    $backup = Backup-File $FilePath
    if ($WhatIf) { return $true }
    
    try {
        $content = Get-Content $FilePath -Raw
        
        # 修复Dictionary<string, string>到Dictionary<string, string?>的转换问题
        $pattern = 'new Dictionary<string, string>\s*\{\s*\{ "ConnectionStrings:DefaultConnection", "Data Source=test\.db" \},\s*\{ "AppSettings:BotToken", "test_token" \},\s*\{ "AppSettings:AdminId", "123456" \}\s*\}'
        $replacement = 'new Dictionary<string, string?>
            {
                { "ConnectionStrings:DefaultConnection", "Data Source=test.db" },
                { "AppSettings:BotToken", "test_token" },
                { "AppSettings:AdminId", "123456" }
            }'
        
        $content = $content -replace $pattern, $replacement
        
        Set-Content $FilePath $content -NoNewline
        Write-Log "成功修复IntegrationTestBase中的配置问题" "SUCCESS"
        return $true
    }
    catch {
        Write-Log "修复IntegrationTestBase失败: $_" "ERROR"
        if ($backup) { Copy-Item $backup $FilePath -Force }
        return $false
    }
}

# 主函数
function Main {
    Write-Log "开始修复TelegramSearchBot.Test编译错误" "INFO"
    Write-Log "项目路径: $ProjectPath" "INFO"
    
    if ($WhatIf) {
        Write-Log "WhatIf模式启用 - 只显示将要执行的操作" "WARNING"
    }
    
    $testProjectPath = Join-Path $ProjectPath "TelegramSearchBot.Test"
    
    if (-not (Test-Path $testProjectPath)) {
        Write-Log "测试项目路径不存在: $testProjectPath" "ERROR"
        exit 1
    }
    
    $fixes = @(
        @{
            Name = "AltPhotoControllerTests"
            Path = Join-Path $testProjectPath "Controller/AI/LLM/AltPhotoControllerTests.cs"
            Function = "Fix-AltPhotoControllerTests"
        },
        @{
            Name = "MessageSearchQueriesTests"
            Path = Join-Path $testProjectPath "Domain/Message/ValueObjects/MessageSearchQueriesTests.cs"
            Function = "Fix-MessageSearchQueriesTests"
        },
        @{
            Name = "MessageSearchRepositoryTests"
            Path = Join-Path $testProjectPath "Infrastructure/Search/Repositories/MessageSearchRepositoryTests.cs"
            Function = "Fix-MessageSearchRepositoryTests"
        },
        @{
            Name = "TestDataSetInitialize"
            Path = Join-Path $testProjectPath "Helpers/TestDatabaseHelper.cs"
            Function = "Fix-TestDataSetInitialize"
        },
        @{
            Name = "QuickPerformanceBenchmarks"
            Path = Join-Path $testProjectPath "Benchmarks/Quick/QuickPerformanceBenchmarks.cs"
            Function = "Fix-QuickPerformanceBenchmarks"
        },
        @{
            Name = "MessageProcessingBenchmarks"
            Path = Join-Path $testProjectPath "Benchmarks/Domain/Message/MessageProcessingBenchmarks.cs"
            Function = "Fix-MessageProcessingBenchmarks"
        },
        @{
            Name = "IntegrationTestBase"
            Path = Join-Path $testProjectPath "Base/IntegrationTestBase.cs"
            Function = "Fix-IntegrationTestBase"
        }
    )
    
    $successCount = 0
    $totalCount = $fixes.Count
    
    foreach ($fix in $fixes) {
        Write-Log "正在修复: $($fix.Name)" "INFO"
        
        if (Test-Path $fix.Path) {
            $result = & $fix.Function -FilePath $fix.Path
            if ($result) {
                $successCount++
                Write-Log "修复成功: $($fix.Name)" "SUCCESS"
            }
            else {
                Write-Log "修复失败: $($fix.Name)" "ERROR"
            }
        }
        else {
            Write-Log "文件不存在，跳过: $($fix.Path)" "WARNING"
        }
    }
    
    Write-Log "修复完成: $successCount/$totalCount 个修复成功" "INFO"
    
    if ($successCount -eq $totalCount) {
        Write-Log "所有编译错误已修复！" "SUCCESS"
        
        # 验证修复结果
        if (-not $WhatIf) {
            Write-Log "验证修复结果..." "INFO"
            try {
                Push-Location $testProjectPath
                $buildResult = dotnet build --verbosity quiet
                if ($LASTEXITCODE -eq 0) {
                    Write-Log "项目编译成功！" "SUCCESS"
                }
                else {
                    Write-Log "项目仍有编译错误，请检查输出" "WARNING"
                    if ($Verbose) {
                        Write-Host $buildResult
                    }
                }
            }
            finally {
                Pop-Location
            }
        }
    }
    else {
        Write-Log "部分修复失败，请检查错误信息" "ERROR"
        exit 1
    }
}

# 执行主函数
Main