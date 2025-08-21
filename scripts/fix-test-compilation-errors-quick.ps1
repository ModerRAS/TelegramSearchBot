# TelegramSearchBot.Test 编译错误快速修复脚本
# 简化版本，专注于修复最关键的编译错误

param(
    [string]$ProjectPath = ".",
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "HH:mm:ss"
    $color = switch($Level) {
        "ERROR" { "Red" }
        "WARNING" { "Yellow" }
        "SUCCESS" { "Green" }
        default { "White" }
    }
    Write-Host "[$timestamp] $Message" -ForegroundColor $color
}

function Backup-File {
    param([string]$FilePath)
    if (Test-Path $FilePath) {
        $backupPath = "$FilePath.backup"
        Copy-Item $FilePath $backupPath
        return $backupPath
    }
    return $null
}

# 修复AltPhotoControllerTests构造函数参数
function Fix-AltPhotoControllerTests {
    param([string]$FilePath)
    
    Write-Log "修复AltPhotoControllerTests构造函数参数"
    
    if (-not (Test-Path $FilePath)) {
        Write-Log "文件不存在: $FilePath" "ERROR"
        return $false
    }
    
    $backup = Backup-File $FilePath
    if ($WhatIf) { return $true }
    
    try {
        $content = Get-Content $FilePath -Raw
        
        # 修复构造函数调用，确保参数正确
        $oldPattern = 'return new AltPhotoController\(\s*BotClientMock\.Object,\s*_generalLLMServiceMock\.Object,\s*SendMessageServiceMock\.Object,\s*MessageServiceMock\.Object,\s*_loggerMock\.Object,\s*MessageExtensionServiceMock\.Object\s*\);'
        $newPattern = 'return new AltPhotoController(
                BotClientMock.Object,
                _generalLLMServiceMock.Object,
                SendMessageServiceMock.Object,
                MessageServiceMock.Object,
                _loggerMock.Object,
                _sendMessageMock.Object,
                MessageExtensionServiceMock.Object
            );'
        
        if ($content -match $oldPattern) {
            $content = $content -replace $oldPattern, $newPattern
            Set-Content $FilePath $content -NoNewline
            Write-Log "成功修复AltPhotoControllerTests" "SUCCESS"
            return $true
        }
        else {
            Write-Log "未找到需要修复的模式" "WARNING"
            return $true
        }
    }
    catch {
        Write-Log "修复失败: $_" "ERROR"
        if ($backup) { Copy-Item $backup $FilePath -Force }
        return $false
    }
}

# 修复MessageSearchQueriesTests参数顺序
function Fix-MessageSearchQueriesTests {
    param([string]$FilePath)
    
    Write-Log "修复MessageSearchQueriesTests参数顺序"
    
    if (-not (Test-Path $FilePath)) {
        Write-Log "文件不存在: $FilePath" "ERROR"
        return $false
    }
    
    $backup = Backup-File $FilePath
    if ($WhatIf) { return $true }
    
    try {
        $content = Get-Content $FilePath -Raw
        
        # 修复MessageSearchByUserQuery构造函数调用
        $content = $content -replace 'new MessageSearchByUserQuery\(groupId, userId, limit\)', 'new MessageSearchByUserQuery(groupId, userId, "", limit)'
        
        # 修复MessageSearchByDateRangeQuery构造函数调用
        $content = $content -replace 'new MessageSearchByDateRangeQuery\(groupId, startDate, endDate, limit\)', 'new MessageSearchByDateRangeQuery(groupId, startDate, endDate, "", limit)'
        
        Set-Content $FilePath $content -NoNewline
        Write-Log "成功修复MessageSearchQueriesTests" "SUCCESS"
        return $true
    }
    catch {
        Write-Log "修复失败: $_" "ERROR"
        if ($backup) { Copy-Item $backup $FilePath -Force }
        return $false
    }
}

# 修复MessageSearchRepositoryTests中的SearchResult类型问题
function Fix-MessageSearchRepositoryTests {
    param([string]$FilePath)
    
    Write-Log "修复MessageSearchRepositoryTests的SearchResult类型问题"
    
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
        
        # 替换SearchResult为Message
        $content = $content -replace 'List<SearchResult>', 'List<Message>'
        $content = $content -replace 'new SearchResult \{ GroupId = 100L, MessageId = 1000L, Content = "test search result", DateTime = DateTime\.UtcNow, Score = 0\.85f \}', 'new Message { GroupId = 100L, MessageId = 1000L, Content = "test search result", DateTime = DateTime.UtcNow }'
        $content = $content -replace 'new SearchResult \{ GroupId = 100L, MessageId = 1001L, Content = "another test result", DateTime = DateTime\.UtcNow, Score = 0\.75f \}', 'new Message { GroupId = 100L, MessageId = 1001L, Content = "another test result", DateTime = DateTime.UtcNow }'
        
        # 修复LuceneManager.Search调用参数顺序
        $content = $content -replace 'm\.Search\(query\.GroupId, query\.Query, query\.Limit\)', 'm.Search(query.Query, query.GroupId, 0, query.Limit)'
        
        # 替换SearchDocument为Message
        $content = $content -replace 'SearchDocument', 'Message'
        
        Set-Content $FilePath $content -NoNewline
        Write-Log "成功修复MessageSearchRepositoryTests" "SUCCESS"
        return $true
    }
    catch {
        Write-Log "修复失败: $_" "ERROR"
        if ($backup) { Copy-Item $backup $FilePath -Force }
        return $false
    }
}

# 修复TestDataSet.Initialize方法
function Fix-TestDataSetInitialize {
    param([string]$FilePath)
    
    Write-Log "修复TestDataSet.Initialize方法"
    
    if (-not (Test-Path $FilePath)) {
        Write-Log "文件不存在: $FilePath" "ERROR"
        return $false
    }
    
    $backup = Backup-File $FilePath
    if ($WhatIf) { return $true }
    
    try {
        $content = Get-Content $FilePath -Raw
        
        # 在TestDataSet类中添加Initialize方法
        if ($content -notmatch 'public void Initialize\(DataDbContext context\)') {
            $oldClass = 'public class TestDataSet\s*\{[^}]*\}'
            $newClass = 'public class TestDataSet
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
            
            $content = $content -replace $oldClass, $newClass
            Set-Content $FilePath $content -NoNewline
        }
        
        Write-Log "成功修复TestDataSet.Initialize方法" "SUCCESS"
        return $true
    }
    catch {
        Write-Log "修复失败: $_" "ERROR"
        if ($backup) { Copy-Item $backup $FilePath -Force }
        return $false
    }
}

# 修复QuickPerformanceBenchmarks中的User和Chat类型问题
function Fix-QuickPerformanceBenchmarks {
    param([string]$FilePath)
    
    Write-Log "修复QuickPerformanceBenchmarks中的User和Chat类型问题"
    
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
        
        # 修复User和Chat类型引用
        $content = $content -replace 'var user = new User\(\);', 'var user = new Telegram.Bot.Types.User();'
        $content = $content -replace 'var chat = new Chat\(\);', 'var chat = new Telegram.Bot.Types.Chat();'
        
        Set-Content $FilePath $content -NoNewline
        Write-Log "成功修复QuickPerformanceBenchmarks" "SUCCESS"
        return $true
    }
    catch {
        Write-Log "修复失败: $_" "ERROR"
        if ($backup) { Copy-Item $backup $FilePath -Force }
        return $false
    }
}

# 修复MessageProcessingBenchmarks中的CreateLongMessage方法调用
function Fix-MessageProcessingBenchmarks {
    param([string]$FilePath)
    
    Write-Log "修复MessageProcessingBenchmarks中的CreateLongMessage方法调用"
    
    if (-not (Test-Path $FilePath)) {
        Write-Log "文件不存在: $FilePath" "ERROR"
        return $false
    }
    
    $backup = Backup-File $FilePath
    if ($WhatIf) { return $true }
    
    try {
        $content = Get-Content $FilePath -Raw
        
        # 修复CreateLongMessage方法调用
        $content = $content -replace 'CreateLongMessage\(userId: userId\)', 'CreateLongMessage()'
        
        Set-Content $FilePath $content -NoNewline
        Write-Log "成功修复MessageProcessingBenchmarks" "SUCCESS"
        return $true
    }
    catch {
        Write-Log "修复失败: $_" "ERROR"
        if ($backup) { Copy-Item $backup $FilePath -Force }
        return $false
    }
}

# 修复IntegrationTestBase中的配置问题
function Fix-IntegrationTestBase {
    param([string]$FilePath)
    
    Write-Log "修复IntegrationTestBase中的配置问题"
    
    if (-not (Test-Path $FilePath)) {
        Write-Log "文件不存在: $FilePath" "ERROR"
        return $false
    }
    
    $backup = Backup-File $FilePath
    if ($WhatIf) { return $true }
    
    try {
        $content = Get-Content $FilePath -Raw
        
        # 修复Dictionary<string, string>到Dictionary<string, string?>的转换问题
        $oldConfig = 'new Dictionary<string, string>\s*\{\s*\{ "ConnectionStrings:DefaultConnection", "Data Source=test\.db" \},\s*\{ "AppSettings:BotToken", "test_token" \},\s*\{ "AppSettings:AdminId", "123456" \}\s*\}'
        $newConfig = 'new Dictionary<string, string?>
            {
                { "ConnectionStrings:DefaultConnection", "Data Source=test.db" },
                { "AppSettings:BotToken", "test_token" },
                { "AppSettings:AdminId", "123456" }
            }'
        
        $content = $content -replace $oldConfig, $newConfig
        
        Set-Content $FilePath $content -NoNewline
        Write-Log "成功修复IntegrationTestBase" "SUCCESS"
        return $true
    }
    catch {
        Write-Log "修复失败: $_" "ERROR"
        if ($backup) { Copy-Item $backup $FilePath -Force }
        return $false
    }
}

# 主执行函数
function Main {
    Write-Log "开始修复TelegramSearchBot.Test编译错误"
    
    $testProjectPath = Join-Path $ProjectPath "TelegramSearchBot.Test"
    
    if (-not (Test-Path $testProjectPath)) {
        Write-Log "测试项目路径不存在: $testProjectPath" "ERROR"
        exit 1
    }
    
    $fixes = @(
        @{ Name = "AltPhotoControllerTests"; Path = Join-Path $testProjectPath "Controller/AI/LLM/AltPhotoControllerTests.cs"; Function = "Fix-AltPhotoControllerTests" },
        @{ Name = "MessageSearchQueriesTests"; Path = Join-Path $testProjectPath "Domain/Message/ValueObjects/MessageSearchQueriesTests.cs"; Function = "Fix-MessageSearchQueriesTests" },
        @{ Name = "MessageSearchRepositoryTests"; Path = Join-Path $testProjectPath "Infrastructure/Search/Repositories/MessageSearchRepositoryTests.cs"; Function = "Fix-MessageSearchRepositoryTests" },
        @{ Name = "TestDataSetInitialize"; Path = Join-Path $testProjectPath "Helpers/TestDatabaseHelper.cs"; Function = "Fix-TestDataSetInitialize" },
        @{ Name = "QuickPerformanceBenchmarks"; Path = Join-Path $testProjectPath "Benchmarks/Quick/QuickPerformanceBenchmarks.cs"; Function = "Fix-QuickPerformanceBenchmarks" },
        @{ Name = "MessageProcessingBenchmarks"; Path = Join-Path $testProjectPath "Benchmarks/Domain/Message/MessageProcessingBenchmarks.cs"; Function = "Fix-MessageProcessingBenchmarks" },
        @{ Name = "IntegrationTestBase"; Path = Join-Path $testProjectPath "Base/IntegrationTestBase.cs"; Function = "Fix-IntegrationTestBase" }
    )
    
    $successCount = 0
    $totalCount = $fixes.Count
    
    foreach ($fix in $fixes) {
        Write-Log "正在修复: $($fix.Name)"
        
        if (Test-Path $fix.Path) {
            $result = & $fix.Function -FilePath $fix.Path
            if ($result) {
                $successCount++
                Write-Log "✓ 修复成功: $($fix.Name)" "SUCCESS"
            }
            else {
                Write-Log "✗ 修复失败: $($fix.Name)" "ERROR"
            }
        }
        else {
            Write-Log "? 文件不存在，跳过: $($fix.Path)" "WARNING"
        }
    }
    
    Write-Log "修复完成: $successCount/$totalCount 个修复成功"
    
    if ($successCount -eq $totalCount) {
        Write-Log "所有编译错误已修复！" "SUCCESS"
        
        # 验证修复结果
        if (-not $WhatIf) {
            Write-Log "验证修复结果..."
            try {
                Push-Location $testProjectPath
                $buildResult = dotnet build --verbosity quiet 2>&1
                if ($LASTEXITCODE -eq 0) {
                    Write-Log "项目编译成功！" "SUCCESS"
                }
                else {
                    Write-Log "项目仍有编译错误" "WARNING"
                    $errors = $buildResult | Where-Object { $_ -match 'error CS' }
                    if ($errors) {
                        Write-Log "剩余错误:"
                        $errors | ForEach-Object { Write-Log "  $_" "ERROR" }
                    }
                }
            }
            finally {
                Pop-Location
            }
        }
    }
    else {
        Write-Log "部分修复失败" "ERROR"
        exit 1
    }
}

Main