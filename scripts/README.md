# Scripts 目录

这个目录包含了TelegramSearchBot项目的各种测试和构建脚本。

## 测试脚本

### `run_tdd_tests.sh`
运行TDD（测试驱动开发）测试套件，验证核心功能的测试覆盖。

### `run_message_tests.sh`
运行Message领域相关的所有测试，包括单元测试和集成测试。

### `run_integration_tests.sh`
运行集成测试，验证各组件之间的交互。

### `run_controller_tests.sh`
运行控制器层的测试，验证API端点。

### `run_search_tests.sh`
运行搜索功能的测试，包括Lucene和向量搜索。

### `run_performance_tests.sh`
运行性能测试，验证系统的响应时间和吞吐量。

### `test_sendmessage_simple.sh`
简单的消息发送测试脚本。

## 使用方法

```bash
# 给脚本添加执行权限
chmod +x scripts/*.sh

# 运行特定测试
./scripts/run_tdd_tests.sh

# 或者使用bash运行
bash scripts/run_integration_tests.sh
```

## 注意事项

- 所有脚本都从项目根目录运行
- 确保已安装所有必要的依赖
- 某些测试可能需要特定的环境配置