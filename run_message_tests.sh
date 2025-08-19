#!/bin/bash

# 运行Message领域测试验证脚本
echo "=== 运行Message领域测试 ==="

# 尝试运行新创建的测试文件
echo "1. 运行MessageSearchQueriesTests..."
dotnet test TelegramSearchBot.sln --filter "MessageSearchQueriesTests" --logger "console;verbosity=minimal" 2>/dev/null

echo "2. 运行MessageEventsTests..."
dotnet test TelegramSearchBot.sln --filter "MessageEventsTests" --logger "console;verbosity=minimal" 2>/dev/null

echo "3. 运行MessageApplicationServiceTests..."
dotnet test TelegramSearchBot.sln --filter "MessageApplicationServiceTests" --logger "console;verbosity=minimal" 2>/dev/null

echo "4. 运行MessageSearchRepositoryTests..."
dotnet test TelegramSearchBot.sln --filter "MessageSearchRepositoryTests" --logger "console;verbosity=minimal" 2>/dev/null

echo "5. 运行MessageAggregateBusinessRulesTests..."
dotnet test TelegramSearchBot.sln --filter "MessageAggregateBusinessRulesTests" --logger "console;verbosity=minimal" 2>/dev/null

echo "=== 测试完成 ==="