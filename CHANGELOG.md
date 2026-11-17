# Changelog

本文件记录用户可见变更与工程化修复。完整未完成清单见 [PLAN/TODO/01-Bugfix1/HANDOFF.md](PLAN/TODO/01-Bugfix1/HANDOFF.md)。

## [Unreleased] — 2026-11-17（Round 17 / Bugfix1）

### Added

- 新增共享 `RecipeFileExtensions`：命令行、文件选择器与保存链统一支持 `.json` / `.recipe`。
- 数据工作台支持跨格式编辑保存（JSONL / CSV / Parquet），不再要求先手工转换。
- 数据工作台分页请求支持取消与过期结果保护；切换文件不会让旧页回写新文件。
- 恢复执行新增节点输出 SHA256 与 Recipe 指纹校验；检查点被篡改或 Recipe 已修改时拒绝恢复。
- 新增共享 Cleanup 规则编辑器：Cleaner 与 Validator 均可编辑 5 个布尔规则、正则替换、忽略大小写与规则顺序。
- Seed 节点新增 Streaming 开关与说明。
- LLM 节点新增 `response_schema` 编辑期校验。
- SFT Export 字段白名单新增重复/空白校验。
- 新增 `scripts/version.ps1`，构建、MSIX、Inno 与发布脚本默认读取 `Directory.Build.props` 版本。

### Changed

- `Directory.Build.props` 成为产品版本单一来源；发布 zip 与 MSIX 文件名包含版本。
- MSIX 打包脚本会把 `-Version` 写入 `AppxManifest.xml`，并引用 `Wide310x150Logo`。
- Inno 打包脚本支持 `-Version`，通过 ISCC 预处理器传入安装器版本。
- EXE 嵌入 `app-icon.ico`；`Avalonia.Diagnostics` 改为仅 Debug 引用。
- README 与 STATUS 同步当前能力、测试计数与恢复/跨格式编辑状态。

### Fixed

- 双击或命令行打开 `.recipe` 不再被忽略；第二实例转发失败会回退到独立启动并记录日志。
- 保存对话框输入 `.recipe` 时不再强制追加 `.json`。
- 多余的命令行 Recipe 参数会写入日志告警，不再静默丢弃。
- 发布包不再包含 `Avalonia.Diagnostics`。

### Tests

- `--list-tests`：539 个用例。
- `dotnet test`：531 通过 / 0 失败 / 7 跳过（5 性能 gated + 2 provider gated），另有 1 个 `[Explicit]` 示例生成测试。
- 新增回归覆盖：Recipe 扩展名、节点编辑器、Cleanup 共享模板、分页取消、跨格式保存、恢复哈希/Recipe 指纹。

### Known Issues

- LLM 节点仍不执行 `tool_calls` 回填；Anthropic 无结构化输出约束。
- Tool Profile MCP、容器级沙箱、Sampler count 模式分层仍未实现。
- API Key 仍以明文保存在本机 `settings.json`；诊断包会脱敏，但磁盘文件未加密。
- MSIX/Inno 安装包尚未在干净机器完成安装验证与签名。
- CI 仍未包含格式、覆盖率、打包冒烟与 nightly gated job。
