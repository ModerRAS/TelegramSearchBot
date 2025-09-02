#!/usr/bin/env python3
"""
PR 137 代码格式化错误提取工具
从GitHub Actions日志中提取和分析代码格式化错误
"""

import re
from collections import defaultdict, Counter

def parse_formatting_errors(log_content):
    """解析代码格式化错误日志"""
    
    # 错误分类计数器
    error_types = Counter()
    file_errors = defaultdict(list)
    
    # 正则表达式模式
    patterns = {
        'WHITESPACE': r'([^:]+\.cs)\((\d+),(\d+)\): error WHITESPACE: (.+)',
        'FINALNEWLINE': r'([^:]+\.cs)\((\d+),(\d+)\): error FINALNEWLINE: (.+)',
        'CHARSET': r'([^:]+\.cs)\((\d+),(\d+)\): error CHARSET: (.+)',
        'IMPORTS': r'([^:]+\.cs)\((\d+),(\d+)\): error IMPORTS: (.+)'
    }
    
    for error_type, pattern in patterns.items():
        matches = re.findall(pattern, log_content)
        for match in matches:
            file_path, line, column, message = match
            # 简化文件路径
            simple_path = file_path.split('\\')[-1] if '\\' in file_path else file_path.split('/')[-1]
            
            error_types[error_type] += 1
            file_errors[simple_path].append({
                'type': error_type,
                'line': line,
                'column': column,
                'message': message,
                'full_path': file_path
            })
    
    return error_types, file_errors

def generate_summary_report(error_types, file_errors):
    """生成错误摘要报告"""
    
    print("=" * 60)
    print("PR 137 代码格式化错误摘要")
    print("=" * 60)
    
    # 总体统计
    total_errors = sum(error_types.values())
    total_files = len(file_errors)
    
    print(f"\n📊 总体统计:")
    print(f"  - 总错误数: {total_errors}")
    print(f"  - 影响文件数: {total_files}")
    
    print(f"\n🔍 错误类型分布:")
    for error_type, count in error_types.most_common():
        print(f"  - {error_type}: {count} 个错误")
    
    # 最严重的文件
    print(f"\n🚨 错误最多的文件 (Top 10):")
    sorted_files = sorted(file_errors.items(), key=lambda x: len(x[1]), reverse=True)
    for i, (filename, errors) in enumerate(sorted_files[:10]):
        print(f"  {i+1:2d}. {filename}: {len(errors)} 个错误")
        
        # 显示错误类型分布
        type_count = Counter(error['type'] for error in errors)
        type_str = ", ".join(f"{t}:{c}" for t, c in type_count.items())
        print(f"      ({type_str})")
    
    # 按错误类型分组的文件
    print(f"\n📂 按错误类型分组:")
    for error_type in error_types:
        files_with_type = [f for f, errors in file_errors.items() 
                          if any(e['type'] == error_type for e in errors)]
        print(f"  - {error_type}: {len(files_with_type)} 个文件")

def generate_detailed_report(file_errors):
    """生成详细错误报告"""
    
    print("\n" + "=" * 60)
    print("详细错误报告")
    print("=" * 60)
    
    # 按目录分组
    directory_groups = defaultdict(list)
    for filename, errors in file_errors.items():
        if 'Test' in filename:
            directory_groups['测试文件'].append((filename, errors))
        elif 'Controller' in errors[0]['full_path']:
            directory_groups['控制器'].append((filename, errors))
        elif 'Service' in errors[0]['full_path']:
            directory_groups['服务'].append((filename, errors))
        elif 'Model' in errors[0]['full_path']:
            directory_groups['模型'].append((filename, errors))
        else:
            directory_groups['其他'].append((filename, errors))
    
    for group_name, group_files in directory_groups.items():
        if not group_files:
            continue
            
        print(f"\n📁 {group_name} ({len(group_files)} 个文件):")
        
        # 显示前5个最严重的文件
        sorted_group = sorted(group_files, key=lambda x: len(x[1]), reverse=True)
        for filename, errors in sorted_group[:5]:
            print(f"  - {filename}: {len(errors)} 个错误")
            
            # 显示前3个具体错误
            for error in errors[:3]:
                print(f"    第{error['line']}行: {error['type']} - {error['message'][:50]}...")
            
            if len(errors) > 3:
                print(f"    ... 还有 {len(errors) - 3} 个错误")

def main():
    """主函数"""
    
    # 示例日志内容（从实际日志中提取的部分）
    sample_log = """
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot.Test\\Service\\Vector\\VectorPerformanceTests.cs(200,74): error WHITESPACE: Fix whitespace formatting. Replace 10 characters with '\\s'.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot.Test\\Service\\Vector\\VectorPerformanceTests.cs(209,54): error WHITESPACE: Fix whitespace formatting. Replace 14 characters with '\\s'.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot\\Attributes\\InjectableAttribute.cs(23,2): error FINALNEWLINE: Fix final newline. Insert '\\r\\n'.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot\\AppBootstrap\\AppBootstrap.cs(1,1): error CHARSET: Fix file encoding.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot\\AppBootstrap\\AppBootstrap.cs(1,1): error IMPORTS: Fix imports ordering.
"""
    
    # 实际使用时，这里应该是从GitHub API获取的完整日志
    # log_content = get_github_action_logs(owner, repo, job_id)
    
    # 解析错误
    error_types, file_errors = parse_formatting_errors(sample_log)
    
    # 生成报告
    generate_summary_report(error_types, file_errors)
    generate_detailed_report(file_errors)
    
    print(f"\n💡 修复建议:")
    print(f"  1. 运行 'dotnet format' 自动修复大部分问题")
    print(f"  2. 检查编辑器配置，确保使用UTF-8编码")
    print(f"  3. 配置自动导入排序")
    print(f"  4. 设置文件末尾自动添加换行符")

if __name__ == "__main__":
    main()