#!/bin/bash

# 修复测试编译错误的脚本
# 这个脚本会批量修复常见的编译错误

echo "开始修复测试编译错误..."

# 1. 修复 using 语句问题
echo "1. 修复 using 语句..."

# 添加缺失的 using 语句
find TelegramSearchBot.Test -name "*.cs" -exec sed -i '/using System;/a\using System.Linq;' {} \;

# 2. 修复类型引用问题
echo "2. 修复类型引用..."

# 修复 SearchResult 引用
find TelegramSearchBot.Test -name "*.cs" -exec sed -i 's/SearchResult/Message/g' {} \;

# 3. 修复 MessageId 使用问题
echo "3. 修复 MessageId 使用..."

# 修复 MessageId 构造函数调用
find TelegramSearchBot.Test -name "*.cs" -exec sed -i 's/new MessageId([0-9]\+)/new MessageId(\1L)/g' {} \;

# 4. 修复 Directory 歧义问题
echo "4. 修复 Directory 歧义..."

# 修复 System.IO.Directory 引用
find TelegramSearchBot.Test -name "*.cs" -exec sed -i 's/Directory\.CreateDirectory/System.IO.Directory.CreateDirectory/g' {} \;
find TelegramSearchBot.Test -name "*.cs" -exec sed -i 's/Directory\.Exists/System.IO.Directory.Exists/g' {} \;

# 5. 修复测试断言问题
echo "5. 修复测试断言..."

# 修复过时的 Assert.Throws 调用
find TelegramSearchBot.Test -name "*.cs" -exec sed -i 's/Assert\.Throws<Exception>/Assert.ThrowsAsync<Exception>/g' {} \;

echo "修复完成！"
echo "请运行 'dotnet build TelegramSearchBot.Test' 验证修复结果"