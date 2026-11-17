# Changelog

本文件记录用户可见变更与工程化修复。完整未完成清单见 [PLAN/TODO/01-Bugfix1/HANDOFF.md](PLAN/TODO/01-Bugfix1/HANDOFF.md)。

## [Unreleased] — 2026-11-18（Round 22 / 数据工作台 messages 结构化编辑）

### Added

- 数据工作台行编辑器新增“消息”标签页：把顶层 `messages` 数组展示为角色/内容表单，支持增删消息与上下移动。
- 每条消息可展开编辑 `tool_calls`（JSON 数组）；结构化内容（数组/对象）按 JSON 编辑并标记“内容为 JSON”。
- 应用消息时保留消息中未暴露的字段（如 `name`、`tool_call_id`）与行内其他顶层字段；Raw JSON 仍作为兜底。
- 字段与消息同时有未应用修改时，“应用字段”会一并提交消息修改，避免互相覆盖。

### Changed

- 行编辑器标签顺序调整为“字段 / 消息 / 原始 JSON”；有 `messages` 时默认打开消息页，否则维持字段页或原始 JSON。
- 新增 zh-CN/en-US 本地化键（消息页、增删/上下移、应用消息）。

### Tests

- `--list-tests`：620 个用例；`dotnet test`：612 通过 / 0 失败 / 7 跳过（另 1 个 `[Explicit]`）。
- 新增 `DatasetMessageEditorTests`（读写往返、字段保留、结构化内容、tool_calls 校验）、数据工作台消息表单与字段合并回归、Headless 面板检查。

### Known Issues

- 除 `messages` 外的嵌套结构仍需在 Raw JSON 中编辑。
- 数据工作台暂无列排序/条件筛选/列宽持久化（191）。

## [Unreleased] — 2026-11-18（Round 21 / LLM 工具回填与 Anthropic 结构化输出）

### Added

- LLM 节点支持工具回填：`MaxToolRounds` 内执行模型返回的 `tool_calls`，将结果作为 tool 消息回灌后再次调用，并输出完整 assistant/tool 轨迹。
- LLM 工具执行方式可选模拟工具（确定性 JSON）或真实沙箱（python / PowerShell / bash / web_fetch）。
- Anthropic Messages 结构化输出：`ResponseSchema` 通过强制 `structured_output` 工具返回 JSON。
- LLM 面板新增工具回填轮数与工具执行方式；校验新增负轮数、无 Tool Profile、Anthropic 结构化输出与工具回填组合警告。

### Changed

- `LlmBlockConfig` 新增 `maxToolRounds`、`toolMode`、`sandbox`；默认 `maxToolRounds=0` 保持“只记录 tool_calls”的旧行为。
- OpenAI Chat/Responses 与 Anthropic 的请求构造支持工具消息往返（`tool_call_id`、`function_call_output`、`tool_result`）。
- 示例 Recipe 重新生成以包含新增默认字段。

### Tests

- `--list-tests`：604 个用例；`dotnet test`：596 通过 / 0 失败 / 7 跳过（另 1 个 `[Explicit]`）。
- 新增 `LlmToolLoopTests`、provider 工具序列化与 Anthropic 强制工具测试、编辑器/校验/Headless 回归。

### Known Issues

- LLM 节点仍无流式输出与模型列表。
- Tool Profile 仍无 MCP 适配与 Enum/Default 可视化编辑。
- Anthropic 结构化输出与工具回填不能同时生效（强制工具会占用本轮调用），校验会给出警告。

## [Unreleased] — 2026-11-18（Round 20 / SFT 导出能力与路径选择）

### Added

- SFT Export 支持安全追加：先复制现有文件到临时副本，成功后再原子替换，失败不破坏原文件。
- SFT Export 支持目标格式 Auto / OpenAI / ShareGPT / Alpaca，转换后再应用字段白名单。
- SFT Export 支持按行数分片：`name.part001.jsonl`、`name.part002.jsonl`…，失败时清理所有临时分片。
- SFT 字段选择器：从 JSONL/CSV/Parquet 样本读取顶层字段，复选、全选/清空，并与字段文本框双向同步。
- LLM/Simulation 失败样本、Validator reject 输出、Sampler 输出目录新增 Browse。
- Recipe 校验新增不支持的目标格式、负分片、追加 + 分片组合的警告。

### Changed

- `SftExportBlockConfig` 新增 `append`、`targetFormat`、`splitMaxRows`；旧 Recipe 按默认值（透传/单文件/覆盖）加载。
- 重新生成 `examples/*.recipe.json` 以包含新增默认字段。

### Tests

- `--list-tests`：590 个用例；`dotnet test`：582 通过 / 0 失败 / 7 跳过（另 1 个 `[Explicit]`）。
- 新增 `SftExportExecutorTests`、`SftExportFieldSelectorTests`、校验与序列化回归，以及 SFT 面板 Headless 控件检查。

## [Unreleased] — 2026-11-18（Round 19 / 数据工作台与入口打磨）

### Added

- 数据工作台搜索结果支持“上一处/下一处”匹配导航，选中即跳转并首尾循环。
- 样本预览支持 50 行分页与上一页/下一页，显示当前页码。
- `File → Recent` 打开时自动移除失效路径，并提供“清空最近文件”（立即持久化）。
- 执行详情在输出目录存在时显示“打开文件夹”。

### Tests

- `--list-tests`：563 个用例；`dotnet test`：556 通过 / 0 失败 / 7 跳过。
- 新增搜索导航、预览分页/起始行、最近文件清理与输出目录检查测试。

## [Unreleased] — 2026-11-18（Round 18 / 测试与 CI 门禁）

### Added

- 单实例与启动链路测试：命令行 Recipe 参数过滤、启动 Recipe 单次消费、第二实例检测、命名管道转发与主实例缺失回退。
- 打包一致性测试：版本单一来源、MSIX 资产与 `.recipe` 关联、Inno 关联/`%1` 转发、脚本 `-Version` 贯通、Release 不含 `Avalonia.Diagnostics`。
- 本地化一致性测试：zh-CN/en-US key 集合、占位符、重复 key 与静态引用 key 存在性。
- 设置面板：缓存/运行/临时目录 Browse 按钮；数值项改为有界 `NumericUpDown`；Apply 前校验范围与目录可写性，错误显示在面板内。
- `scripts/check-coverage.ps1` 与 `coverlet.msbuild`，CI 覆盖率门禁为总行 ≥60%、Services/Commands ≥70%。
- CI 新增格式门禁、Debug/Release 矩阵、示例再生成漂移检查、PowerShell 脚本解析与 `publish-win-x64.ps1` 版本冒烟，并加入 concurrency/timeout/NuGet 缓存。

### Changed

- `Directory.Build.props` 移除硬编码 `AssemblyVersion`/`FileVersion`/`InformationalVersion`，由 `Version` 统一派生；发布脚本 `-Version` 现在会写入 EXE 文件版本。
- 全仓执行 `dotnet format`，统一导入顺序、缩进与行尾；CSV 多行测试 fixture 显式使用 LF，避免跨平台行尾漂移。

### Tests

- `--list-tests`：557 个用例；`dotnet test`：550 通过 / 0 失败 / 7 跳过。
- 覆盖率（本地）：总行 74.64%，Services/Commands 行 84.32%。
- `dotnet format --verify-no-changes` 通过；发布冒烟产物 FileVersion=0.0.0.0；示例无漂移。

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
- CI 尚未包含 nightly gated job、依赖安全扫描与跨平台（Linux/macOS）构建。
