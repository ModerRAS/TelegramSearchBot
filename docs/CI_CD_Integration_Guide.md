# CI/CD集成建议 - TelegramSearchBot TDD实施

## 概述

本文档为TelegramSearchBot项目提供完整的CI/CD集成建议，确保TDD流程能够自动化运行，保证代码质量和持续集成。

## CI/CD流水线设计

### 1. GitHub Actions配置

### 1.1 主流水线配置

```yaml
# .github/workflows/main.yml

name: CI/CD Pipeline

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main ]
  workflow_dispatch:

env:
  DOTNET_VERSION: '9.0.x'
  NODE_VERSION: '18'
  
jobs:
  # 代码质量检查
  code-quality:
    name: Code Quality
    runs-on: ubuntu-latest
    
    steps:
    - name: Checkout code
      uses: actions/checkout@v4
      
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: ${{ env.DOTNET_VERSION }}
        
    - name: Restore dependencies
      run: dotnet restore TelegramSearchBot.sln
      
    - name: Build solution
      run: dotnet build TelegramSearchBot.sln --configuration Release --no-restore
      
    - name: Run code analysis
      run: dotnet format --verify-no-changes TelegramSearchBot.sln
      
    - name: Security scan
      run: dotnet list package --vulnerable --include-transitive TelegramSearchBot.sln

  # 单元测试
  unit-tests:
    name: Unit Tests
    runs-on: ubuntu-latest
    needs: code-quality
    
    steps:
    - name: Checkout code
      uses: actions/checkout@v4
      
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: ${{ env.DOTNET_VERSION }}
        
    - name: Restore dependencies
      run: dotnet restore TelegramSearchBot.sln
      
    - name: Run unit tests
      run: |
        chmod +x ./run_tdd_tests.sh
        ./run_tdd_tests.sh --unit --coverage
      
    - name: Upload coverage to Codecov
      uses: codecov/codecov-action@v3
      with:
        file: ./coverage.xml
        flags: unittests
        name: codecov-umbrella
        fail_ci_if_error: true

  # 集成测试
  integration-tests:
    name: Integration Tests
    runs-on: ubuntu-latest
    needs: code-quality
    
    services:
      postgres:
        image: postgres:15
        env:
          POSTGRES_PASSWORD: testpass
          POSTGRES_DB: testdb
        options: >-
          --health-cmd pg_isready
          --health-interval 10s
          --health-timeout 5s
          --health-retries 5
        ports:
          - 5432:5432
          
      redis:
        image: redis:7-alpine
        options: >-
          --health-cmd "redis-cli ping"
          --health-interval 10s
          --health-timeout 5s
          --health-retries 5
        ports:
          - 6379:6379
    
    steps:
    - name: Checkout code
      uses: actions/checkout@v4
      
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: ${{ env.DOTNET_VERSION }}
        
    - name: Restore dependencies
      run: dotnet restore TelegramSearchBot.sln
      
    - name: Run integration tests
      run: |
        chmod +x ./run_tdd_tests.sh
        ./run_tdd_tests.sh --integration
      env:
        TEST_DATABASE_CONNECTION_STRING: Host=localhost;Database=testdb;Username=postgres;Password=testpass
        TEST_REDIS_CONNECTION_STRING: localhost:6379

  # 性能测试
  performance-tests:
    name: Performance Tests
    runs-on: ubuntu-latest
    needs: [unit-tests, integration-tests]
    
    steps:
    - name: Checkout code
      uses: actions/checkout@v4
      
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: ${{ env.DOTNET_VERSION }}
        
    - name: Restore dependencies
      run: dotnet restore TelegramSearchBot.sln
      
    - name: Run performance tests
      run: |
        chmod +x ./run_tdd_tests.sh
        ./run_tdd_tests.sh --performance

  # 构建和发布
  build-and-deploy:
    name: Build and Deploy
    runs-on: ubuntu-latest
    needs: [unit-tests, integration-tests, performance-tests]
    if: github.ref == 'refs/heads/main'
    
    steps:
    - name: Checkout code
      uses: actions/checkout@v4
      
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: ${{ env.DOTNET_VERSION }}
        
    - name: Restore dependencies
      run: dotnet restore TelegramSearchBot.sln
      
    - name: Build solution
      run: dotnet publish TelegramSearchBot.sln --configuration Release --runtime linux-x64 --self-contained
      
    - name: Upload artifacts
      uses: actions/upload-artifact@v3
      with:
        name: telegram-search-bot
        path: TelegramSearchBot/bin/Release/net9.0/linux-x64/publish/
        
    - name: Deploy to staging
      run: |
        echo "Deploy to staging environment"
        # 添加实际的部署脚本
```

### 1.2 Pull Request流水线

```yaml
# .github/workflows/pr.yml

name: Pull Request Checks

on:
  pull_request:
    branches: [ main, develop ]

jobs:
  pr-checks:
    name: PR Checks
    runs-on: ubuntu-latest
    
    strategy:
      matrix:
        check: [format, build, test, security]
        
    steps:
    - name: Checkout code
      uses: actions/checkout@v4
      
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '9.0.x'
        
    - name: Restore dependencies
      run: dotnet restore TelegramSearchBot.sln
      
    - name: Format check
      if: matrix.check == 'format'
      run: dotnet format --verify-no-changes TelegramSearchBot.sln
      
    - name: Build solution
      if: matrix.check == 'build'
      run: dotnet build TelegramSearchBot.sln --configuration Release
      
    - name: Run tests
      if: matrix.check == 'test'
      run: |
        chmod +x ./run_tdd_tests.sh
        ./run_tdd_tests.sh --unit
        
    - name: Security scan
      if: matrix.check == 'security'
      run: dotnet list package --vulnerable --include-transitive TelegramSearchBot.sln
```

### 2. 质量门禁配置

### 2.1 测试覆盖率门禁

```yaml
# .github/workflows/quality-gate.yml

name: Quality Gate

on:
  pull_request:
    branches: [ main ]
  workflow_dispatch:

jobs:
  quality-gate:
    name: Quality Gate
    runs-on: ubuntu-latest
    
    steps:
    - name: Checkout code
      uses: actions/checkout@v4
      
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '9.0.x'
        
    - name: Run tests with coverage
      run: |
        dotnet test TelegramSearchBot.sln \
          --configuration Release \
          --collect:"XPlat Code Coverage" \
          --results-directory TestResults/
          
    - name: Check coverage thresholds
      uses: danielpalme/ReportGenerator-GitHub-Action@5.1.9
      with:
        reports: 'TestResults/coverage.xml'
        targetdir: 'coverage-report'
        reporttypes: 'Html'
        threshold: '80'
        threshold-type: 'line'
        threshold-statistic: 'total'
```

### 2.2 代码质量检查

```yaml
# .github/workflows/code-quality.yml

name: Code Quality

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main ]

jobs:
  code-quality:
    name: Code Quality
    runs-on: ubuntu-latest
    
    steps:
    - name: Checkout code
      uses: actions/checkout@v4
      
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '9.0.x'
        
    - name: Install dotnet format
      run: dotnet tool install -g dotnet-format
      
    - name: Check formatting
      run: dotnet format --verify-no-changes TelegramSearchBot.sln
      
    - name: Run static analysis
      run: |
        dotnet add TelegramSearchBot.sln package Microsoft.CodeAnalysis.NetAnalyzers
        dotnet build TelegramSearchBot.sln --configuration Release
```

### 3. 测试环境配置

### 3.1 Docker测试环境

```dockerfile
# Dockerfile.test

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS test

WORKDIR /app

# 复制项目文件
COPY *.sln ./
COPY TelegramSearchBot/*.csproj ./TelegramSearchBot/
COPY TelegramSearchBot.Test/*.csproj ./TelegramSearchBot.Test/

# 恢复依赖
RUN dotnet restore TelegramSearchBot.sln

# 复制源代码
COPY . .

# 运行测试
CMD ["dotnet", "test", "TelegramSearchBot.sln", "--configuration", "Release", "--logger", "console;verbosity=detailed"]
```

### 3.2 Docker Compose测试环境

```yaml
# docker-compose.test.yml

version: '3.8'

services:
  app:
    build: 
      context: .
      dockerfile: Dockerfile.test
    environment:
      - TEST_DATABASE_CONNECTION_STRING=Host=postgres;Database=testdb;Username=postgres;Password=testpass
      - TEST_REDIS_CONNECTION_STRING=redis:6379
    depends_on:
      - postgres
      - redis
    volumes:
      - ./coverage-report:/app/coverage-report
      
  postgres:
    image: postgres:15
    environment:
      POSTGRES_DB: testdb
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: testpass
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
      
  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    volumes:
      - redis_data:/data
      
volumes:
  postgres_data:
  redis_data:
```

### 4. 监控和报告

### 4.1 测试报告生成

```yaml
# .github/workflows/reports.yml

name: Test Reports

on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]

jobs:
  generate-reports:
    name: Generate Test Reports
    runs-on: ubuntu-latest
    
    steps:
    - name: Checkout code
      uses: actions/checkout@v4
      
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '9.0.x'
        
    - name: Run tests
      run: |
        dotnet test TelegramSearchBot.sln \
          --configuration Release \
          --collect:"XPlat Code Coverage" \
          --logger "trx;LogFileName=test-results.trx" \
          --results-directory TestResults/
          
    - name: Generate HTML report
      uses: danielpalme/ReportGenerator-GitHub-Action@5.1.9
      with:
        reports: 'TestResults/coverage.xml'
        targetdir: 'coverage-report'
        reporttypes: 'Html'
        
    - name: Upload test results
      uses: actions/upload-artifact@v3
      with:
        name: test-results
        path: |
          TestResults/
          coverage-report/
```

### 4.2 性能监控

```yaml
# .github/workflows/performance.yml

name: Performance Monitoring

on:
  schedule:
    - cron: '0 0 * * *'  # 每天运行
  workflow_dispatch:

jobs:
  performance-test:
    name: Performance Test
    runs-on: ubuntu-latest
    
    steps:
    - name: Checkout code
      uses: actions/checkout@v4
      
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '9.0.x'
        
    - name: Run performance tests
      run: |
        chmod +x ./run_tdd_tests.sh
        ./run_tdd_tests.sh --performance
        
    - name: Upload performance results
      uses: actions/upload-artifact@v3
      with:
        name: performance-results
        path: performance-results/
```

### 5. 部署策略

### 5.1 蓝绿部署

```yaml
# .github/workflows/blue-green-deploy.yml

name: Blue-Green Deployment

on:
  push:
    branches: [ main ]

jobs:
  deploy:
    name: Deploy
    runs-on: ubuntu-latest
    environment: production
    
    steps:
    - name: Checkout code
      uses: actions/checkout@v4
      
    - name: Build application
      run: |
        dotnet publish TelegramSearchBot.sln --configuration Release --runtime linux-x64 --self-contained
        
    - name: Deploy to green environment
      run: |
        echo "Deploying to green environment"
        # 部署到绿色环境的脚本
        
    - name: Run smoke tests
      run: |
        echo "Running smoke tests"
        # 运行冒烟测试
        
    - name: Switch traffic to green
      run: |
        echo "Switching traffic to green environment"
        # 切换流量的脚本
        
    - name: Decommission blue environment
      run: |
        echo "Decommissioning blue environment"
        # 停用蓝色环境的脚本
```

### 6. 本地开发环境

### 6.1 Git Hooks

```bash
# .git/hooks/pre-commit

#!/bin/bash

echo "Running pre-commit hooks..."

# 运行格式检查
dotnet format --verify-no-changes
if [ $? -ne 0 ]; then
    echo "Code formatting issues found. Please run 'dotnet format' to fix."
    exit 1
fi

# 运行单元测试
chmod +x ./run_tdd_tests.sh
./run_tdd_tests.sh --unit
if [ $? -ne 0 ]; then
    echo "Unit tests failed. Please fix before committing."
    exit 1
fi

echo "Pre-commit hooks passed."
```

### 6.2 本地开发脚本

```bash
# scripts/dev-setup.sh

#!/bin/bash

echo "Setting up development environment..."

# 安装依赖
dotnet restore TelegramSearchBot.sln

# 安装全局工具
dotnet tool install -g dotnet-format
dotnet tool install -g dotnet-reportgenerator-globaltool

# 运行初始测试
chmod +x ./run_tdd_tests.sh
./run_tdd_tests.sh --full

echo "Development environment setup complete."
```

### 7. 监控和告警

### 7.1 测试失败告警

```yaml
# .github/workflows/notifications.yml

name: Notifications

on:
  workflow_run:
    workflows: ["CI/CD Pipeline"]
    types:
      - completed
    branches: [ main ]

jobs:
  notify:
    name: Notify
    runs-on: ubuntu-latest
    if: ${{ github.event.workflow_run.conclusion == 'failure' }}
    
    steps:
    - name: Send Slack notification
      uses: 8398a7/action-slack@v3
      with:
        status: failure
        channel: '#ci-cd'
        webhook_url: ${{ secrets.SLACK_WEBHOOK }}
      env:
        SLACK_WEBHOOK_URL: ${{ secrets.SLACK_WEBHOOK }}
```

### 8. 最佳实践建议

### 8.1 流水线优化

1. **并行化测试执行**
   - 使用测试分类并行运行
   - 优化测试依赖关系
   - 使用测试容器隔离环境

2. **缓存优化**
   - 缓存NuGet包
   - 缓存Docker镜像
   - 缓存构建结果

3. **渐进式部署**
   - 金丝雀发布
   - 蓝绿部署
   - 特性开关

### 8.2 安全考虑

1. **密钥管理**
   - 使用GitHub Secrets
   - 轮换访问令牌
   - 最小权限原则

2. **环境隔离**
   - 开发/测试/生产环境分离
   - 网络隔离
   - 数据隔离

### 8.3 性能优化

1. **构建优化**
   - 增量构建
   - 并行构建
   - 构建缓存

2. **测试优化**
   - 测试并行化
   - 测试数据管理
   - Mock策略优化

这个CI/CD集成建议为TelegramSearchBot项目提供了完整的自动化测试和部署流程，确保TDD实践能够在团队中有效实施，并保持代码质量和开发效率。