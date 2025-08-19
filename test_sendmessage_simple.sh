#!/bin/bash

# 简化的SendMessage测试脚本
echo "开始测试SendMessage服务..."

# 编译主项目
echo "编译主项目..."
dotnet build TelegramSearchBot.sln --configuration Debug --no-restore

# 运行SendMessageSimpleTests
echo "运行SendMessage测试..."
dotnet test TelegramSearchBot.Test --filter "FullyQualifiedName~SendMessageSimpleTests" --no-build --verbosity normal

echo "测试完成！"