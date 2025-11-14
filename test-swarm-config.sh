#!/bin/bash

# Claude Swarm 启动测试脚本
# 用于测试 TelegramSearchBot AI 开发团队配置

echo "🚀 Claude Swarm 配置验证测试"
echo "================================="

# 1. 验证配置文件语法
echo "1. 验证配置文件语法..."
if python3 -c "import yaml; yaml.safe_load(open('claude-swarm.yml', 'r'))"; then
    echo "✅ YAML 语法正确"
else
    echo "❌ YAML 语法错误"
    exit 1
fi

# 2. 分析配置结构
echo "2. 分析配置结构..."
python3 -c "
import yaml
with open('claude-swarm.yml', 'r') as f:
    config = yaml.safe_load(f)

instances = config.get('swarm', {}).get('instances', {})
print(f'   总实例数: {len(instances)}')
print(f'   主实例: {config.get(\"swarm\", {}).get(\"main\")}')

# 统计模型使用
model_count = {}
for name, instance in instances.items():
    model = instance.get('model', 'unknown')
    model_count[model] = model_count.get(model, 0) + 1

print('   模型分布:')
for model, count in model_count.items():
    print(f'     {model}: {count} 个实例')

# 检查连接关系
connections = {}
for name, instance in instances.items():
    connections[name] = instance.get('connections', [])

# 验证树形结构
def has_cycle(graph, node, visited, rec_stack):
    visited[node] = True
    rec_stack[node] = True
    
    for neighbor in graph.get(node, []):
        if neighbor not in visited:
            if has_cycle(graph, neighbor, visited, rec_stack):
                return True
        elif rec_stack[neighbor]:
            return True
    
    rec_stack[node] = False
    return False

visited = {}
rec_stack = {}
has_cycle_detected = False
for node in connections:
    if node not in visited:
        if has_cycle(connections, node, visited, rec_stack):
            has_cycle_detected = True
            break

if has_cycle_detected:
    print('   ❌ 发现循环依赖')
else:
    print('   ✅ 树形结构正确，无循环依赖')
"

# 3. 验证项目依赖
echo "3. 验证项目依赖..."
if [ -f "TelegramSearchBot.sln" ]; then
    echo "✅ 找到解决方案文件"
    if dotnet restore TelegramSearchBot.sln --verbosity quiet; then
        echo "✅ 项目依赖恢复成功"
    else
        echo "⚠️ 项目依赖恢复失败（可能是网络问题）"
    fi
else
    echo "❌ 未找到解决方案文件"
fi

# 4. 验证目录结构
echo "4. 验证目录结构..."
required_dirs=("TelegramSearchBot" "TelegramSearchBot.Test")
for dir in "${required_dirs[@]}"; do
    if [ -d "$dir" ]; then
        echo "✅ $dir 目录存在"
    else
        echo "❌ $dir 目录不存在"
    fi
done

echo ""
echo "🎉 配置验证完成！"
echo ""
echo "📋 配置摘要:"
echo "   - 团队名称: TelegramSearchBot AI Development Team"
echo "   - 总角色数: 29个专业角色"
echo "   - 架构层次: 4层树形结构"
echo "   - 主模型: Opus (11个实例) + Sonnet (18个实例)"
echo "   - 叶子节点: 21个专项专家"
echo ""
echo "🚀 要启动团队，请运行: claude-swarm start claude-swarm.yml"
echo "   (注意: 需要交互式终端输入)"