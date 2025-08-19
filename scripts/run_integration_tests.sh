#!/bin/bash

# 运行新创建的集成测试
echo "=== 运行Message领域集成测试 ==="

echo "1. 编译项目..."
dotnet build TelegramSearchBot.sln --configuration Debug > /dev/null 2>&1

if [ $? -ne 0 ]; then
    echo "❌ 编译失败"
    exit 1
fi

echo "✅ 编译成功"

echo ""
echo "2. 运行MessageRepositoryIntegrationTests..."
dotnet test TelegramSearchBot.Test/TelegramSearchBot.Test.csproj --filter "MessageRepositoryIntegrationTests" --logger "console;verbosity=minimal" --no-build

echo ""
echo "3. 运行MessageDatabaseIntegrationTests..."
dotnet test TelegramSearchBot.Test/TelegramSearchBot.Test.csproj --filter "MessageDatabaseIntegrationTests" --logger "console;verbosity=minimal" --no-build

echo ""
echo "=== 集成测试完成 ==="