#!/usr/bin/env pwsh

# FAISS向量索引测试运行脚本
# 用于运行所有向量相关的单元测试并生成测试报告

Write-Host "🚀 开始运行FAISS向量索引相关测试..." -ForegroundColor Green
Write-Host ""

# 设置测试过滤器
$testFilters = @(
    "VectorIndexTests",
    "FaissVectorServiceTests", 
    "VectorSearchIntegrationTests",
    "VectorPerformanceTests"
)

$testResults = @()
$totalTests = 0
$passedTests = 0
$failedTests = 0

foreach ($filter in $testFilters) {
    Write-Host "📋 运行测试组: $filter" -ForegroundColor Cyan
    Write-Host "=" * 50
    
    try {
        # 运行测试并捕获输出
        $result = dotnet test "TelegramSearchBot.Test/TelegramSearchBot.Test.csproj" --filter $filter --verbosity normal --logger "console;verbosity=detailed" 2>&1
        
        # 解析测试结果
        $resultText = $result -join "`n"
        
        if ($resultText -match "(\d+) 个测试通过") {
            $passed = [int]$matches[1]
            $passedTests += $passed
            $totalTests += $passed
            Write-Host "✅ $filter : $passed 个测试通过" -ForegroundColor Green
        }
        
        if ($resultText -match "(\d+) 个测试失败") {
            $failed = [int]$matches[1]
            $failedTests += $failed
            $totalTests += $failed
            Write-Host "❌ $filter : $failed 个测试失败" -ForegroundColor Red
        }
        
        if ($resultText -match "没有找到测试") {
            Write-Host "⚠️  $filter : 没有找到匹配的测试" -ForegroundColor Yellow
        }
        
        $testResults += @{
            Filter = $filter
            Output = $resultText
            Success = $LASTEXITCODE -eq 0
        }
        
    } catch {
        Write-Host "❌ 运行 $filter 时发生错误: $($_.Exception.Message)" -ForegroundColor Red
        $testResults += @{
            Filter = $filter
            Output = $_.Exception.Message
            Success = $false
        }
    }
    
    Write-Host ""
}

# 生成测试总结
Write-Host "📊 测试总结" -ForegroundColor Magenta
Write-Host "=" * 50

Write-Host "总测试数: $totalTests" -ForegroundColor White
Write-Host "通过: $passedTests" -ForegroundColor Green
Write-Host "失败: $failedTests" -ForegroundColor Red

if ($failedTests -eq 0 -and $totalTests -gt 0) {
    Write-Host "🎉 所有向量索引测试都通过了！" -ForegroundColor Green
} elseif ($totalTests -eq 0) {
    Write-Host "⚠️  没有找到可运行的测试" -ForegroundColor Yellow
} else {
    Write-Host "⚠️  部分测试失败，请检查详细日志" -ForegroundColor Yellow
}

# 创建测试报告
$reportPath = "VectorTestReport_$(Get-Date -Format 'yyyyMMdd_HHmmss').txt"
$report = @"
FAISS向量索引测试报告
生成时间: $(Get-Date)
==================================================

测试总结:
- 总测试数: $totalTests
- 通过: $passedTests  
- 失败: $failedTests

详细结果:
"@

foreach ($result in $testResults) {
    $report += @"

--------------------------------------------------
测试组: $($result.Filter)
状态: $(if($result.Success) { "成功" } else { "失败" })
--------------------------------------------------
$($result.Output)

"@
}

$report | Out-File -FilePath $reportPath -Encoding UTF8
Write-Host ""
Write-Host "📄 详细测试报告已保存到: $reportPath" -ForegroundColor Cyan

# 如果有失败的测试，返回非零退出码
if ($failedTests -gt 0) {
    exit 1
} else {
    exit 0
} 