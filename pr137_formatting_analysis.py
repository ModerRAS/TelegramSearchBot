#!/usr/bin/env python3
"""
PR 137 代码格式化错误完整分析工具
基于实际的GitHub Actions日志数据
"""

import re
from collections import defaultdict, Counter

# 实际的GitHub Actions日志数据
ACTUAL_LOG_DATA = """D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot.Test\\Service\\Vector\\VectorPerformanceTests.cs(200,74): error WHITESPACE: Fix whitespace formatting. Replace 10 characters with '\\s'.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot.Test\\Service\\Vector\\VectorPerformanceTests.cs(209,54): error WHITESPACE: Fix whitespace formatting. Replace 14 characters with '\\s'.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot.Test\\Service\\Vector\\VectorPerformanceTests.cs(221,46): error WHITESPACE: Fix whitespace formatting. Replace 14 characters with '\\s'.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot.Test\\Service\\Vector\\VectorPerformanceTests.cs(223,20): error WHITESPACE: Fix whitespace formatting. Replace 18 characters with '\\s'.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot.Test\\Service\\Vector\\VectorPerformanceTests.cs(226,74): error WHITESPACE: Fix whitespace formatting. Replace 22 characters with '\\s'.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot.Test\\Service\\Vector\\VectorSearchIntegrationTests.cs(20,48): error WHITESPACE: Fix whitespace formatting. Replace 2 characters with '\\s'.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot.Test\\Service\\Vector\\VectorSearchIntegrationTests.cs(25,60): error WHITESPACE: Fix whitespace formatting. Replace 6 characters with '\\s'.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot.Test\\Service\\Vector\\VectorSearchIntegrationTests.cs(39,46): error WHITESPACE: Fix whitespace formatting. Replace 10 characters with '\\s'.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot.Test\\View\\SearchViewTests.cs(12,38): error WHITESPACE: Fix whitespace formatting. Replace 2 characters with '\\s'.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot.Test\\View\\SearchViewTests.cs(14,33): error WHITESPACE: Fix whitespace formatting. Replace 6 characters with '\\s'.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot\\Attributes\\InjectableAttribute.cs(23,2): error FINALNEWLINE: Fix final newline. Insert '\\r\\n'.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot\\Controller\\Common\\CommandUrlProcessingController.cs(159,2): error FINALNEWLINE: Fix final newline. Insert '\\r\\n'.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot\\Controller\\Common\\UrlProcessingController.cs(62,2): error FINALNEWLINE: Fix final newline. Insert '\\r\\n'.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot\\Controller\\Manage\\ScheduledTaskController.cs(248,2): error FINALNEWLINE: Fix final newline. Insert '\\r\\n'.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot\\Handler\\MessageVectorGenerationHandler.cs(20,2): error FINALNEWLINE: Fix final newline. Insert '\\r\\n'.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot\\AppBootstrap\\AppBootstrap.cs(1,1): error CHARSET: Fix file encoding.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot\\AppBootstrap\\ASRBootstrap.cs(1,1): error CHARSET: Fix file encoding.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot\\AppBootstrap\\DaemonBootstrap.cs(1,1): error CHARSET: Fix file encoding.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot\\AppBootstrap\\OCRBootstrap.cs(1,1): error CHARSET: Fix file encoding.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot\\AppBootstrap\\QRBootstrap.cs(1,1): error CHARSET: Fix file encoding.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot\\AppBootstrap\\AppBootstrap.cs(1,1): error IMPORTS: Fix imports ordering.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot\\AppBootstrap\\ASRBootstrap.cs(1,1): error IMPORTS: Fix imports ordering.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot\\AppBootstrap\\GeneralBootstrap.cs(1,1): error IMPORTS: Fix imports ordering.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot\\AppBootstrap\\OCRBootstrap.cs(1,1): error IMPORTS: Fix imports ordering.
D:\\a\\TelegramSearchBot\\TelegramSearchBot\\TelegramSearchBot\\AppBootstrap\\QRBootstrap.cs(1,1): error IMPORTS: Fix imports ordering."""

def extract_file_path_info(full_path):
    """从完整路径中提取文件信息"""
    parts = full_path.replace('\\', '/').split('/')
    filename = parts[-1]
    
    # 确定文件类型
    if 'Test' in full_path:
        file_type = '测试文件'
    elif 'Controller' in full_path:
        file_type = '控制器'
    elif 'Service' in full_path:
        file_type = '服务'
    elif 'Model' in full_path:
        file_type = '模型'
    elif 'AppBootstrap' in full_path:
        file_type = '引导程序'
    elif 'Handler' in full_path:
        file_type = '处理器'
    elif 'Helper' in full_path:
        file_type = '辅助类'
    elif 'Interface' in full_path:
        file_type = '接口'
    elif 'View' in full_path:
        file_type = '视图'
    else:
        file_type = '其他'
    
    return filename, file_type

def analyze_pr137_formatting_errors():
    """分析PR 137的代码格式化错误"""
    
    # 解析所有错误
    error_types = Counter()
    file_errors = defaultdict(list)
    file_types = defaultdict(list)
    
    # 正则表达式匹配错误行
    pattern = r'([^:]+\.cs)\((\d+),(\d+)\): error (WHITESPACE|FINALNEWLINE|CHARSET|IMPORTS): (.+)'
    matches = re.findall(pattern, ACTUAL_LOG_DATA)
    
    for match in matches:
        full_path, line, column, error_type, message = match
        filename, file_type = extract_file_path_info(full_path)
        
        error_types[error_type] += 1
        file_errors[filename].append({
            'type': error_type,
            'line': line,
            'column': column,
            'message': message,
            'full_path': full_path
        })
        
        if filename not in file_types[file_type]:
            file_types[file_type].append(filename)
    
    return error_types, file_errors, file_types

def print_comprehensive_report():
    """打印综合错误报告"""
    
    error_types, file_errors, file_types = analyze_pr137_formatting_errors()
    total_errors = sum(error_types.values())
    total_files = len(file_errors)
    
    print("🔍 PR 137 代码格式化错误完整分析报告")
    print("=" * 80)
    
    # 基本统计
    print(f"\n📊 基本统计:")
    print(f"   总错误数量: {total_errors}")
    print(f"   影响文件数: {total_files}")
    print(f"   涉及项目: TelegramSearchBot")
    
    # 错误类型分布
    print(f"\n🚨 错误类型分布:")
    for error_type, count in error_types.most_common():
        percentage = (count / total_errors) * 100
        print(f"   {error_type:12s}: {count:3d} 个错误 ({percentage:5.1f}%)")
    
    # 文件类型分布
    print(f"\n📂 受影响的文件类型:")
    for file_type, files in file_types.items():
        type_error_count = sum(len(file_errors[f]) for f in files)
        print(f"   {file_type:8s}: {len(files):2d} 个文件, {type_error_count:3d} 个错误")
    
    # 最严重的文件
    print(f"\n🔥 错误最多的文件 (Top 10):")
    sorted_files = sorted(file_errors.items(), key=lambda x: len(x[1]), reverse=True)
    for i, (filename, errors) in enumerate(sorted_files[:10], 1):
        error_type_count = Counter(e['type'] for e in errors)
        type_summary = ', '.join(f"{k}:{v}" for k, v in error_type_count.items())
        print(f"   {i:2d}. {filename:35s}: {len(errors):2d} 个错误 ({type_summary})")
    
    # 具体错误示例
    print(f"\n💡 具体错误示例:")
    print(f"   WHITESPACE 错误示例:")
    whitespace_example = next((e for errors in file_errors.values() for e in errors if e['type'] == 'WHITESPACE'), None)
    if whitespace_example:
        print(f"   - 文件: {whitespace_example['full_path'].split('\\')[-1]}")
        print(f"   - 位置: 第{whitespace_example['line']}行, 第{whitespace_example['column']}列")
        print(f"   - 描述: {whitespace_example['message']}")
    
    print(f"\n   FINALNEWLINE 错误示例:")
    finalnewline_example = next((e for errors in file_errors.values() for e in errors if e['type'] == 'FINALNEWLINE'), None)
    if finalnewline_example:
        print(f"   - 文件: {finalnewline_example['full_path'].split('\\')[-1]}")
        print(f"   - 描述: {finalnewline_example['message']}")
    
    # 修复建议
    print(f"\n🛠️  修复建议:")
    print(f"   1. 立即操作:")
    print(f"      dotnet format                    # 自动修复大部分问题")
    print(f"      dotnet format --verify-no-changes # 验证修复结果")
    print(f"   ")
    print(f"   2. 编辑器配置:")
    print(f"      - 设置文件编码为 UTF-8")
    print(f"      - 配置一致的缩进（空格或制表符）") 
    print(f"      - 启用文件末尾自动换行")
    print(f"      - 配置自动导入排序")
    print(f"   ")
    print(f"   3. 预防措施:")
    print(f"      - 设置 git pre-commit hook 运行格式检查")
    print(f"      - 在 CI/CD 中添加格式检查步骤")
    print(f"      - 团队统一开发环境配置")

    # 优先级建议
    print(f"\n⚡ 修复优先级:")
    print(f"   🔴 高优先级: CHARSET 和 FINALNEWLINE 错误（相对容易修复）")
    print(f"   🟡 中优先级: IMPORTS 错误（可使用工具自动修复）")
    print(f"   🟠 低优先级: WHITESPACE 错误（可能需要手动调整）")

if __name__ == "__main__":
    print_comprehensive_report()