# 更新日志

所有重要的版本更新都会记录在此。

## [0.3.6] - 2026-03-27

### 修复

- **修复 `GetUpdatePath` 无限循环**: 当检查增量更新包时，`VersionRange.GetUpdatePath()` 会进入无限循环。添加 `tv <= current` 检查确保只选择严格大于当前版本的更新包。
  - 影响: `GetUpdatePath(Version, Version, IEnumerable<UpdateCatalogEntry>)`
  - 影响: `GetUpdatePath(Version, Version, IEnumerable<UpdateManifest>)`
  - 修复提交: `98bba22`

### 示例程序

- 新增 `--create-version-chain` 命令用于测试版本链更新
- Demo 程序现在可以正确演示完整更新流程

### 文档

- 添加中文 README 文档
- Demo 程序添加中文使用说明

## [0.3.5] - 2025-01-20

### 已知问题

- `GetUpdatePath` 在某些增量更新场景下可能出现无限循环 (已修复于 0.3.6)

## [0.1.0] - 初始版本

- 双进程自更新库
- Zstd 压缩支持
- SHA512 校验
- 原子性文件替换
