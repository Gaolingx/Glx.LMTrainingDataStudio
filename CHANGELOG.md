# Changelog

本文件记录用户可见变更与工程化修复。当前工作池见 [PLAN/TODO/02-Hardening/HANDOFF.md](PLAN/TODO/02-Hardening/HANDOFF.md)；Round 17–32 已归档到 [PLAN/Done/02-Bugfix1/HANDOFF.md](PLAN/Done/02-Bugfix1/HANDOFF.md)。

## [Unreleased] — 2026-11-20（Round 32 / 节点与发布收尾）

### Added

- Recipe 校验问题统一填充稳定错误码，属性面板问题行显示 Code；覆盖节点名、端口、边、环、配置与输出目标冲突。
- 数据工作台为 CSV 字段推断展示类型（integer/number/bool/date/json/string）；仅展示，不改写源文本。
- Sampler count 模式支持按 `StratifyField` 分层：分组 reservoir + 最大余数配额，行数守恒并按源行号输出。
- SFT Export 支持 `name:ratio` 比例拆片与拆片种子；分片临时写入后统一原子替换，并纳入输出目标锁。
- LLM 节点流式输出扩展至 OpenAI Responses 与 Anthropic（文本、工具调用参数、usage、structured_output）。
- 新增 nightly（性能/真实 provider gated）、release（tag 发布）与依赖漏洞审计工作流；新增 Dependabot 配置。

### Changed

- LLM 面板流式说明改为三种 provider 均支持。
- SFT Export 面板新增比例拆片、拆片种子与错误提示（zh-CN/en-US）。
- 示例 Recipe 重新生成，补入 `splitSeed` 默认字段。

### Tests

- `--list-tests`：744 个用例；`dotnet test`：736 通过 / 0 失败 / 7 跳过（另 1 个 `[Explicit]`）。
- 覆盖率：总行 77.55%，Services/Commands 85.07%。
- 新增校验 Code、CSV 推断、count 分层、SFT 拆片成功/失败/冲突、三种 provider 流式、CI 工作流一致性与 Headless 控件测试。

### Known Issues

- CI 依赖审计当前报告上游传递依赖 High 公告（Snappier、SQLitePCLRaw、Tmds.DBus.Protocol），job 暂为 advisory。
- 依赖锁定未启用：Debug-only `Avalonia.Diagnostics` 导致 Debug/Release 锁文件依赖集冲突。
- 安装包干净机器验证、签名、双进程单实例与 10M 行质量/转换基线仍未完成，见 [PLAN/TODO/02-Hardening/HANDOFF.md](PLAN/TODO/02-Hardening/HANDOFF.md)。

## [Unreleased] — 2026-11-18（Round 31 / 代码质量清理）

### Removed

- 删除未引用的 `BlockTypeToColorConverter`、`InverseBoolConverter` 与 `BlockTemplateViewModel.ColorKey`、`BlockNodeViewModel.HeaderColorKey`；块头颜色仍由主题 `*BlockBrush` 资源提供。

### Changed

- `Directory.Build.props` 的 `NoWarn` 从 55 项缩减到 7 项并注明保留理由；`CA1707` 仅对测试项目抑制。
- 修复 CA1068（`RecipeRunner.RunAsync` 令牌参数置末）、CA1725（`ReadPageAsync` 参数名）、CA1806（数值排序解析结果）、CA1869（复用 `JsonSerializerOptions`）、CA1310（测试断言补 `StringComparison`）。

### Tests

- `--list-tests`：709 个用例；`dotnet test`：701 通过 / 0 失败 / 7 跳过（另 1 个 `[Explicit]`）。
- 关闭抑制的全量重建仅剩 8 个规则码（7 个全局 + 测试 CA1707）；本轮为删除/重构，无新增测试。

### Known Issues

- CA1001（可释放字段未接线）与 CA1305/CA1822/CA1859/CA1861/CA1863 仍保留抑制，已在 HANDOFF 7.11 记录跟进项。

## [Unreleased] — 2026-11-18（Round 30 / 数据工作台视图）

### Added

- 数据工作台新增字段表格视图：按顶层字段展示单元格，表头点击排序（升/降序切换），筛选工具栏支持包含、相等、数值比较、空值与正则。
- 排序与筛选在完整数据集上流式构建视图索引；`IDatasetReader.ReadRowsAsync` 支持散列行读取，Parquet 按 row group 批量解码。
- 列宽可拖拽调整，并按字段名保存到设置（加载时钳制 48–800px）。

### Changed

- 视图激活期间禁用行编辑；搜索/校验跳转自动清除视图；存在待保存编辑或转换时拒绝建立视图。
- 视图内“跳转到行”仍按源行号定位，未命中时提示该行不在视图中。
- 视图索引用 4,096 行分块扫描，匹配上限 100 万行；超限提示缩窄筛选。

### Tests

- `--list-tests`：709 个用例；`dotnet test`：701 通过 / 0 失败 / 7 跳过（另 1 个 `[Explicit]`）。
- 新增 24 项测试：筛选/排序/正则/上限/散列读取、视图分页与编辑禁用、视图内跳行、列宽持久化、设置清洗与 Headless 字段视图绑定。

### Known Issues

- 视图索引常驻内存且上限 100 万行，10M 行未做磁盘索引与实测；CSV 类型推断未做。

## [Unreleased] — 2026-11-18（Round 29 / LLM 流式与模型列表）

### Added

- LLM 节点新增 `Streaming` 开关：OpenAI Chat 走 SSE，累积 content、按 index 合并 tool_calls 并读取 usage；重试从空累积器重启。
- 新增 `LlmModelCatalog` 与面板模型下拉/刷新（OpenAI `/models`、Anthropic `/v1/models`），复用 `ILlmCredentials`。

### Changed

- Responses/Anthropic 在开启流式时仍为一次性响应，面板已注明。
- 模型目录失败在面板内提示；刷新期间切换 provider/endpoint 丢弃过期结果。

### Tests

- `--list-tests`：685 个用例；`dotnet test`：677 通过 / 0 失败 / 7 跳过（另 1 个 `[Explicit]`）。
- 新增 16 项测试：SSE 内容/工具/usage 累积、瞬时错误重试、模型目录、编辑器写回与 Headless 面板绑定。

### Known Issues

- SSE 与模型接口仅用本地桩验证，真实 provider 冒烟仍为 gated；Responses/Anthropic 无流式。

## [Unreleased] — 2026-11-18（Round 28 / 执行并发与溢写清理）

### Added

- 运行期输出写锁：每个目标文件同级 `.lmtds.lock` 以 `FileShare.None` 独占，运行前获取、结束/失败释放，冲突时拒绝启动并列出目标。
- 启动时清理溢写根下的 run-scoped 残留目录；删除失败留待下次重试。

### Changed

- 输出目标定义抽到 `RecipeOutputTargets`，校验与运行期锁共用。
- 取消性能测试改为读取 `LMTDS_CANCEL_BUDGET_MS`，CI 使用 3s 门禁，本机默认 1s。

### Tests

- `--list-tests`：669 个用例；`dotnet test`：661 通过 / 0 失败 / 7 跳过（另 1 个 `[Explicit]`）。
- 新增 10 项测试：写锁冲突/释放、溢写目录启动清理、运行前置锁校验。

### Known Issues

- 锁的跨进程语义以同进程第二次独占打开模拟，未做真实双进程端到端验证。

## [Unreleased] — 2026-11-18（Round 27 / 执行韧性收尾）

### Added

- `SpillThresholdBytes`：节点间缓冲同时受行数与字节预算约束，先到者触发溢写；默认 64 MB，0 关闭字节限制。
- `CheckpointQuota`：启动与运行结束时按运行输出目录大小清理最旧检查点，保护当前运行并保留运行摘要与日志。
- `ExecutionStore.Prune` 与设置项：执行历史按保留数量/天数清理；启动自动应用，Executions 视图提供带确认的“清理…”入口。

### Changed

- 设置面板新增“溢写阈值（MB）/ 检查点配额（MB）/ 执行记录保留数量/天数”，非法值回退默认并告警。
- 阈值 0 语义：行阈值 0 立即溢写；字节阈值 0 关闭字节限制；检查点配额与保留数量/天数为 0 表示不限制。

### Tests

- `--list-tests`：659 个用例；`dotnet test`：651 通过 / 0 失败 / 7 跳过（另 1 个 `[Explicit]`）。
- 新增 13 项测试：字节溢写、检查点配额、执行历史清理、启动维护与设置往返/面板边界。

### Known Issues

- 未做跨进程并发写锁、CI 取消性能门禁与异常退出后的溢写目录启动清理。

## [Unreleased] — 2026-11-18（Round 26 / 诊断脱敏与目录 ACL）

### Added

- 新增 `DiagnosticRedactor`：对 `Authorization` / `Bearer` 头、`apiKey` / `token` / `secret` / `password` 等 JSON 字段、查询参数、`sk-` / `gh*_` / `AKIA` 前缀以及设置和环境变量中的 Key 字面量统一替换为 `[REDACTED]`。
- 新增 `AppDirectorySecurity`：Windows 上把设置、日志与崩溃目录的 ACL 收紧为当前用户、SYSTEM 与 Administrators 完全控制。

### Changed

- 诊断包的 `summary.txt`、`logs/*.log`、`executions/*.json` 与 `settings.redacted.json` 全部经脱敏后写出；活动日志改为共享读取，避免导出时被跳过。
- 设置保存、API Key 迁移、日志初始化与自动崩溃报告目录在首次创建时应用 ACL；失败仅记录告警，非 Windows 为无操作。

### Tests

- `--list-tests`：646 个用例；`dotnet test`：638 通过 / 0 失败 / 7 跳过（另 1 个 `[Explicit]`）。
- 新增 `DiagnosticSecurityTests` 8 项：脱敏规则、普通文本不误伤、诊断包不含密钥、Windows ACL 生效与设置目录保护。

### Known Issues

- ACL 仅在 Windows 本机验证，未覆盖干净用户/其他机器；web_fetch 域名白名单与崩溃报告保留策略仍未做。

## [Unreleased] — 2026-11-18（Round 25 / web_fetch DNS 加固与设置通知）

### Added

- 新增 `IAppSettingsService` / `AppSettingsService`：保存当前设置并在 Apply 成功后发布 `Changed`；主窗口通过 `SettingsService` 暴露给长生命周期服务。
- 数据工作台订阅设置变更，保存新设置后自动重建读取器工厂、批量转换器与校验/搜索/转换/统计子服务。

### Changed

- `web_fetch` 只解析一次主机名，并固定连接已校验的 IP（`SocketsHttpHandler.ConnectCallback`），关闭 DNS 重绑定窗口；任一解析结果为私网/回环即拒绝，混合结果同样拒绝。
- 修复数据工作台读取器工厂长期持有旧设置、批量转换器不随设置更新的问题；新打开或覆盖重开的数据集使用当前缓存目录与容量。
- 保留测试用 `HttpClient` 注入口；未注入时使用固定地址客户端，重定向仍关闭。

### Tests

- `--list-tests`：638 个用例；`dotnet test`：630 通过 / 0 失败 / 7 跳过（另 1 个 `[Explicit]`）。
- 新增 web_fetch 混合私网结果拒绝、只解析一次并固定地址、设置变更事件、数据工作台缓存目录切换与 Apply 通知等 6 项测试。

### Known Issues

- 未在真实公网执行抓取，也未手工验证 HTTPS/SNI；诊断日志内容脱敏、目录 ACL 与域名白名单仍未做。

## [Unreleased] — 2026-11-18（Round 24 / API Key DPAPI 加密）

### Added

- 设置中的 OpenAI / Anthropic API Key 改用 Windows DPAPI（当前用户作用域）加密后写入 `settings.json`；内存中仍保持明文供请求使用。
- 加载设置时自动识别旧明文 Key 并原地加密回写，磁盘文件不再保留明文；加密值带 `dpapi:v1:` 前缀，便于识别与后续版本迁移。
- 无法解密（换用户、损坏、跨平台）时给出明确警告并忽略该 Key，回退到同名环境变量。

### Changed

- 非 Windows 平台不再持久化 API Key（只使用环境变量），避免在不支持 DPAPI 的环境写入明文。
- 诊断包导出仍先清空 Key；加密后的值同样不会出现在脱敏设置中。

### Tests

- `--list-tests`：632 个用例；`dotnet test`：624 通过 / 0 失败 / 7 跳过（另 1 个 `[Explicit]`）。
- 新增 DPAPI 落盘断言、旧明文迁移、损坏密文告警、无保护能力时丢弃 Key 等 6 项 `SettingsStoreTests`。

### Known Issues

- DPAPI 绑定当前 Windows 用户；更换用户或迁移机器后需重新输入 Key（环境变量仍可兜底）。
- 设置变更通知已在 Round 25 完成；`Settings`/索引缓存 schema 版本与迁移策略仍未做。

## [Unreleased] — 2026-11-18（Round 23 / Tool Profile 枚举与输出冲突校验）

### Added

- Tool Profile 参数树新增“枚举”与“默认值”编辑：枚举支持逗号分隔字符串或 JSON 数组，默认值按 JSON 解析，并与参数 JSON Schema 双向同步。
- Recipe 校验新增输出目标冲突检测：SFT 输出、LLM/Simulation 失败样本、Validator reject 文件与 Sampler 分片文件（`<dir>/<split>.jsonl`）重复时直接报错；相同 Sampler 输出目录也视为冲突。

### Changed

- 非法枚举/默认值 JSON 在 Tool Profile 面板内提示，并保留上一次有效 Schema，不再静默丢弃。
- 新增 zh-CN/en-US 本地化键（枚举、默认值）。

### Tests

- `--list-tests`：626 个用例；`dotnet test`：618 通过 / 0 失败 / 7 跳过（另 1 个 `[Explicit]`）。
- 新增 Tool Profile 枚举/默认值读写与错误测试、输出目标冲突 4 项校验测试、参数树 Headless 检查。

### Known Issues

- 输出冲突检测只覆盖同一 Recipe 内的节点；两次独立运行同时写同一路径仍无跨进程锁。
- SFT 仍无按比例拆片与输出路径存在性提示。

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
