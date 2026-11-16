# PLAN / TODO — 实施计划与验收标准

> 最后更新：2026-11-16
> 当前基线：.NET 9 / Avalonia 11.3 / NUnit 4
> 工作方式：小步提交，每个 Phase 独立验收；开发期允许 break change，不保留兼容层。

---

## 一、本轮进展（Round 6）

### 1.1 本轮做了什么

- [x] 新增 4 个模拟测试子节点：Query Source、Simulation（mini harness）、Cleaner、SFT Export；`BlockType` / `BlockConfig` / 默认配置 / 深拷贝 / 校验 / UI 模板全部接线。
- [x] 模拟用户 query：`QuerySourceBlockConfig` 支持同 query 重复（`SameQuery`）与多组 query（`MultipleQueries`），可带 system prompt。
- [x] 模拟 API 多轮轨迹：`SimulationHarness` 记录 system → user → assistant(tool_calls) → tool → final assistant 的完整 OpenAI messages 轨迹。
- [x] mini harness 规则：工具白名单、每工具超时、失败注入（成功 / 失败 / 超时 / 前 N 次失败）、失败重试与重试延迟、失败回填或中止、模型级超时/重试、最大轮数。
- [x] 内置 `SimulatedToolCatalog`：python / web_fetch / powershell / bash 四个确定性模拟工具（只模拟响应，不真实执行进程或网络）。
- [x] 轨迹清洗：`TrajectoryCleaner` 支持正则替换（可忽略大小写）、空白规范化、换行规范化、ANSI 转义清理、移除 `reasoning_content`、丢弃空消息；Validator 节点也可配置 `Cleanup`。
- [x] SFT 导出：Simulation 直接输出 OAI `messages` + `tools`；`SftExportBlockExecutor` 以临时文件 + 原子替换写入 JSONL。
- [x] UI：新增 4 个 Block Sheet 模板、执行器注册、每个节点通用 Config JSON 编辑器（Apply 后即时生效并有错误提示）。
- [x] 新增 54 个测试：harness 14、清洗 9、执行器与端到端流水线 16、配置序列化/深拷贝 5、配置 JSON 编辑器 5、配置校验 5。

### 1.2 验证结果

| 项目 | 命令 | 结果 |
| --- | --- | --- |
| 构建 | `dotnet build LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 成功，0 警告，0 错误 |
| 测试 | `dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 200 通过，0 失败，0 跳过 |

本轮新增/扩展测试：

| 测试类 | 数量 | 覆盖内容 |
| --- | --- | --- |
| `SimulationHarnessTests` | 14 | 单轮/多轮、工具白名单、失败重试、超时、max rounds、abort、默认脚本、取消、模型超时、参数 JSON、OAI 序列化 |
| `TrajectoryCleanerTests` | 9 | 正则、空白/换行、ANSI、reasoning、空消息、数组 content、非样本行透传、非法正则 |
| `SimulationExecutorsTests` | 16 | Query 同/多组、Simulation 输出与 system 覆盖、未完成轨迹丢弃/保留、Cleaner、Validator cleanup、SFT 导出、四节点端到端 |
| `BlockNodeViewModelTests` | 5 | JSON 配置编辑器回写、类型不匹配、非法 JSON、空输入 |
| `BlockConfigSerializationTests` 扩展 | 5 | Query/Simulation/Cleaner/Export 往返、嵌套规则、深拷贝 |
| `RecipeValidationServiceTests` 扩展 | 5 | 新节点必填项、重复工具名、max rounds/timeout 校验 |

### 1.3 本轮改动文件

```text
PLAN/TODO.md
README.md
LMTrainingDataStudio2/LMTrainingDataStudio2/Models/BlockConfig.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Models/RecipeBlock.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Models/DatasetCleanup.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Models/SimulationConfig.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Models/ChatMessage.cs（沿用）
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Simulation/ChatCompletionContracts.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Simulation/ScriptedChatClient.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Simulation/SimulatedToolRuntime.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Simulation/SimulationHarness.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Simulation/TrajectoryCleaner.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Simulation/TrajectorySerializer.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Executors/QuerySourceBlockExecutor.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Executors/SimulationBlockExecutor.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Executors/CleanerBlockExecutor.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Executors/SftExportBlockExecutor.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Executors/ValidatorBlockExecutor.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/RecipeValidationService.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/BlockNodeViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/MainWindowViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/BlockTemplateViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Views/PropertyPanelView.axaml
LMTrainingDataStudio2/LMTrainingDataStudio2/Views/RecipeCanvasView.axaml.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Themes/Colors.axaml
LMTrainingDataStudio2/LMTrainingDataStudio2/Converters/BlockTypeToColorConverter.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/SimulationHarnessTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/TrajectoryCleanerTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/SimulationExecutorsTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/BlockNodeViewModelTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/BlockConfigSerializationTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/RecipeValidationServiceTests.cs
```

### 1.4 技术决策与取舍

1. **模拟 API 默认使用确定性脚本，而不是真实供应商调用。**
   - `ScriptedChatClient` 按 `SimulationBlockConfig.Script` 回放 assistant 回合；未配置脚本时调用每个已暴露工具一次再收尾。
   - 这样轨迹可复现、零网络、零费用，适合先打通“query → 轨迹 → 清洗 → SFT”链路。
2. **工具是声明式模拟，不执行真实进程/网络。**
   - `SimulatedToolRule` 只描述 schema、成功模板、失败消息、延迟和超时；`SimulatedToolRuntime` 不落地任何命令。
   - 避免在数据生成器里引入任意代码执行安全面；真实沙箱执行列为后续项。
3. **未完成轨迹默认丢弃。**
   - 超过 max rounds / abort 的轨迹只有显式开启 `EmitIncompleteTrajectories` 才会输出，避免污染 SFT 数据。
4. **清洗规则同时挂到 Cleaner 节点和 Validator。**
   - 独立 Cleaner 便于单独编排；Validator 可选 `Cleanup` 满足“验证通过后顺手清洗”的用法。
5. **SFT 输出只保留 OAI `messages` + `tools`。**
   - 轮数、工具重试次数、停止原因只写入 run log，不进入训练样本。

### 1.5 本轮未做

- 真实 tool-calling provider（OpenAI function calling / Anthropic tool_use）HTTP 客户端。
- 真实 python / PowerShell / bash 沙箱执行与资源限制。
- Query / Simulation / Cleaner / SFT Export 的专属属性面板（当前使用通用 Config JSON 编辑器）。
- 并发 query 生成、模型成本/延迟统计、失败样本单独落盘。

---

## 二、开发约定与完成定义（DoD）

### 2.1 技术基线

- 目标框架：`net9.0`，全项目统一。
- UI：Avalonia 11.3 + CommunityToolkit.Mvvm。
- 数据：`System.Text.Json`，SQLite 索引缓存，CsvHelper / Parquet.Net 后续接入。
- 测试：NUnit 4 + Microsoft.NET.Test.Sdk，测试项目 `LMTrainingDataStudio2.Tests`。
- 异步：所有 IO、执行、转换使用 `async/await` + `CancellationToken` + `IProgress<T>`；禁止 UI 线程长耗时。
- 时间与随机：可测试逻辑必须允许注入 `TimeProvider` / `Random` 种子。

### 2.2 Break change 政策

开发阶段不考虑兼容，允许以下操作：

- 删除 `RecipeBlock.Properties`，替换为强类型 `BlockConfig`。
- 重写 `AppSettings` 的使用方式，改为通过 `ISettingsStore` 注入。
- 重构 `BlockNodeViewModel`、`MainWindowViewModel` 的公开成员。
- Recipe / Execution / Settings 文件只支持当前 schema 版本，不做旧版本迁移。
- 示例 Recipe 可以直接重写，不保留旧节点 ID/名称。
- 每次 break change 必须在同一提交内更新所有调用点和测试，禁止留下“兼容 shim”。

### 2.3 测试策略

- **本轮（Round 1）**：只覆盖 core 纯逻辑最小模块，已建立 29 个回归测试。
- **Phase 1–4**：每个阶段只为本阶段新增/修改的 core 逻辑补单测，保持测试随代码增长，不把测试全部堆到最后。
- **Phase 5**：做完整测试矩阵和大规模数据测试，包括 UI headless、集成、恶意/畸形输入、1M–10M 行性能与内存。
- 单测要求：
  - 不依赖网络；LLM/HTTP 用 Fake 实现。
  - 不依赖用户目录；统一使用 `Path.GetTempPath()` 下的随机目录。
  - 每个核心方法至少覆盖 1 条成功路径 + 1 条失败/边界路径。
  - 命名：`Method_Condition_ExpectedResult`。
- 覆盖率目标（指导值，不凑数）：
  - `Services/` + `Commands/` 核心逻辑行覆盖 ≥ 70%。
  - 全仓库行覆盖最终 ≥ 60%。

### 2.4 单项任务 DoD（完成定义）

一个功能项只有同时满足以下条件才算完成：

- [ ] 代码可编译，`dotnet build` 0 警告。
- [ ] 对应 core 逻辑有单元测试；修复 bug 必须附带回归测试。
- [ ] UI 入口真实可用，不存在“按钮无反应/面板无内容”的占位。
- [ ] 长耗时操作可取消，UI 线程不阻塞。
- [ ] 错误信息包含上下文：文件路径、行号、节点名/ID、失败原因。
- [ ] 文件写入采用临时文件 + 原子替换，失败不破坏原文件。
- [ ] 没有为未完成功能保留的静默 `catch`、空实现或死代码。
- [ ] 相关文档/本 TODO 勾选状态已更新。

### 2.5 验收方式

每个 Phase 结束时执行三层验收：

1. **自动验收**：`dotnet build` + `dotnet test` 全绿。
2. **手工验收脚本**：按本阶段列出的 UI 操作步骤逐项执行，记录实际结果。
3. **数据验收**：使用固定 fixtures 和大文件样本，核对行数、内容、输出文件、内存与耗时。

### 2.6 性能与内存基线

基线环境：本机 SSD、Release 构建。Phase 5 可用实测数据校准，但不得低于以下目标：

| 场景 | 目标 |
| --- | --- |
| 1M 行 JSONL（平均 1KB/行）索引构建 | ≤ 10s |
| 1M 行 JSONL 语法校验 | ≤ 15s |
| 索引完成后任意页读取（100 行） | ≤ 100ms |
| 1M 行处理峰值内存 | ≤ 300MB |
| 应用冷启动 | ≤ 2s |
| 画布 500 节点拖动/缩放 | 无可感知卡顿（单帧 < 100ms） |

---

## 三、分阶段实施计划

| Phase | 主题 | 主要交付 | 出口标准 |
| --- | --- | --- | --- |
| 0 | 基线 | .NET 9 构建、最小 core 测试 | 已完成，29 测试通过 |
| 1 | Core 基础层 | 强类型节点配置、Recipe/设置持久化、JSONL Reader、校验器补强 | 核心完成；1M 性能与清理日志留待 Phase 5 |
| 2 | 执行引擎 | Runner、执行器、执行记录、Executions UI | 示例 Recipe 真跑通，可取消、可追踪 |
| 3 | 编辑器 UI | 属性配置、网格/吸附/Fit/Mini-map、Tool Schema | 全编辑链路手工验收通过 |
| 4 | 数据能力 | CSV/Parquet、批量转换、校验/修复、数据编辑 | 数据处理闭环可用 |
| 5 | 收尾 | 全量测试、大规模性能、CI、打包、文档 | 发布候选可安装运行 |
| 6 | 模拟 API 测试 | Query Source / Simulation Harness / Cleaner / SFT Export 子节点，确定性多轮轨迹与 OAI 导出 | 已完成 Round 6；真实 provider tool-calling 与沙箱执行留待后续 |

---

### Phase 1 — Core 基础层（下一步，P0）

#### 目标

把“节点配置、Recipe、设置、JSONL 文件”从占位/内存态变成可持久化、可测试的核心能力，为执行引擎和 UI 提供稳定底座。

#### 当前进度（Round 2，2026-11-16）

- [x] 强类型节点配置：`BlockConfig` 多态层级、`RecipeBlock.Config` 必填、ViewModel 配置与错误提示、校验服务按配置类型检查；新增 19 个测试。
- [x] Recipe 持久化：`RecipeStore` 原子写入、camelCase 多态 JSON、`SchemaVersion` 校验、`RecipeFormatException` 带路径与位置。
- [x] 设置持久化：`ISettingsStore`/`SettingsStore`，默认 `%AppData%/LMTrainingDataStudio/settings.json`，范围校验与回退警告、原子保存；`ApplySettings` 已落盘。
- [x] JSONL 数据读取器：`JsonlDatasetReader` 分页/单行读取；索引指纹失效、LRU 容量清理、BOM/CRLF 字节偏移与超长行读取。
- [x] 校验器补强：孤立节点警告、重复边、端口归属/方向、DataType 兼容；示例 Recipe 端口类型已修正。
- [x] Phase 1 核心项全部完成；1M 行性能基准与缓存清理日志留待 Phase 5。

#### 范围与实现方式

1. **强类型节点配置**
   - 新增 `Models/BlockConfig.cs`：
     ```csharp
     [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
     [JsonDerivedType(typeof(SeedBlockConfig), "seed")]
     [JsonDerivedType(typeof(LlmBlockConfig), "llm")]
     [JsonDerivedType(typeof(ExpressionBlockConfig), "expression")]
     [JsonDerivedType(typeof(ValidatorBlockConfig), "validator")]
     [JsonDerivedType(typeof(SamplerBlockConfig), "sampler")]
     [JsonDerivedType(typeof(ToolProfileBlockConfig), "tool_profile")]
     public abstract record BlockConfig;

     public sealed record SeedBlockConfig(string SourcePath, DataFileType FileType, bool Streaming, bool UseIndexCache) : BlockConfig;
     public sealed record LlmBlockConfig(string Provider, string ModelAlias, double Temperature, int MaxTokens, string? Prompt) : BlockConfig;
     public sealed record ExpressionBlockConfig(string Template, Dictionary<string, string> Fields) : BlockConfig;
     public sealed record ValidatorBlockConfig(string RuleKind, string Rule, bool RejectOnFailure) : BlockConfig;
     public sealed record SamplerBlockConfig(long? Count, double? Ratio, int Seed, string[] Splits) : BlockConfig;
     public sealed record ToolProfileBlockConfig(ToolProfile Profile) : BlockConfig;
     ```
   - `RecipeBlock` 删除 `Dictionary<string, object?> Properties`，改为 `public required BlockConfig Config { get; init; }`。
   - `BlockNodeViewModel` 增加 `BlockConfig Config` 与 `string? ConfigError`。
   - `BuildRecipeFromViewModel` 直接复制 `Config`，不再依赖字符串键。
   - `RecipeValidationService` 改为 `switch (block.Config)` 强类型校验。
2. **Recipe 持久化**
   - 新增 `Services/RecipeStore.cs`：`SaveAsync(Recipe, path, ct)` / `LoadAsync(path, ct)`。
   - `Recipe` 增加 `int SchemaVersion { get; set; } = 1`。
   - `JsonSerializerOptions`：枚举写字符串、缩进、多态、忽略 null。
   - 原子写入：写 `*.tmp` → `File.Move(tmp, target, overwrite: true)`。
   - 加载失败抛出带路径和 JSON 位置的 `RecipeFormatException`。
3. **设置持久化**
   - 新增 `Services/ISettingsStore.cs` + `SettingsStore.cs`。
   - 路径：`Environment.GetFolderPath(ApplicationData)/LMTrainingDataStudio/settings.json`。
   - 加载时做范围校验（缓存 ≥ 16MB、吸附 1–256px、目录非空），非法值回退默认并记录日志。
   - `MainWindowViewModel` 通过 `ISettingsStore` 读取/保存；`ApplySettings` 真正落盘。
4. **JSONL 数据读取器**
   - 新增 `Services/JsonlDatasetReader.cs`：
     - `OpenAsync(path, ct)`：构建或复用索引。
     - `ReadPageAsync(startLine, count, ct)`：返回 `IReadOnlyList<DatasetRow>`。
     - `ReadLineAsync(line, ct)`、`TotalLines`、`SourcePath`。
     - 内部保持一个打开的 `FileStream`/`SafeFileHandle`，避免每行重复打开文件。
   - `IndexCacheService` 增加：
     - 源文件指纹（文件大小 + 最后写入时间 + 首尾各 4KB 哈希），不匹配则重建缓存。
     - `EnsureIndexAsync(filePath, ct)` 统一入口。
     - 容量执行：新增缓存前统计总大小，按最近使用时间清理旧缓存；超过上限时拒绝或清理。
     - `OpenCacheAsync` 重新打开前先释放旧连接。
     - 行读取改为循环 `ReadAsync` 直到读满，避免超长行截断。
5. **校验器补强**
   - 保留现有检查，新增：孤立节点警告、重复边、源端口必须是输出、目标端口必须是输入、端口 `DataType` 兼容、每种节点必填配置校验。
   - 所有问题带 `BlockId` 和节点名，便于 UI 定位。

#### 交付物

- `Models/BlockConfig.cs`
- `Services/RecipeStore.cs`、`Services/ISettingsStore.cs`、`Services/SettingsStore.cs`
- `Services/JsonlDatasetReader.cs`
- 修改 `Models/RecipeBlock.cs`、`Models/Recipe.cs`、`ViewModels/BlockNodeViewModel.cs`、`ViewModels/MainWindowViewModel.cs`、`Services/IndexCacheService.cs`、`Services/RecipeValidationService.cs`
- 新增/扩展测试：`BlockConfigSerializationTests`、`RecipeStoreTests`、`SettingsStoreTests`、`JsonlDatasetReaderTests`，并扩展 `IndexCacheServiceTests`、`RecipeValidationServiceTests`

#### 验收标准

- [ ] 示例 Recipe 保存后重新加载，节点、边、配置、坐标、schemaVersion 完全一致。
- [ ] 重新启动应用后，缓存目录、缓存上限、网格吸附、主题设置保持不变。
- [ ] 1M 行 JSONL：首次建索引 ≤ 10s，总行数准确，任意页读取 ≤ 100ms，峰值内存 ≤ 300MB。
- [ ] 修改源文件大小或最后写入时间后，旧索引自动失效并重建，不返回错误行。
- [ ] 缓存总量超过配置上限时，缓存总量最终回落到上限以内；清理/拒绝行为有日志。
- [ ] 校验能拦截：重名、环、悬空边、孤立节点、错误端口方向、类型不兼容、缺失必填配置。
- [ ] 新增测试全部通过，构建 0 警告。

#### 收尾动作

- 更新本文件的 Phase 1 勾选状态。
- 在 README “当前状态”中补充“Recipe/Settings 可持久化、JSONL 分页读取可用”。
- 打一个可回滚的阶段性提交，提交信息注明 break change。

---

### Phase 2 — 执行引擎（P0）

#### 目标

用真正的 DAG 执行替换 `Task.Delay` 模拟，建立可观察、可取消、可复现的执行链路。

#### 当前进度（Round 4，2026-11-16）

- [x] 执行模型：`ExecutionRun`、`NodeExecutionResult`、`ExecutionProgress`、`RunStatus` / `ExecutionStatus`。
- [x] 执行契约：`IBlockExecutor`、`BlockExecutionContext`、`BlockExecutionResult`（输入/输出均为 `IAsyncEnumerable<DatasetRow>`）。
- [x] 执行器：Seed（索引/流式）、Expression（`{{ path }}` 子集）、Validator（最小规则语法）、Sampler（reservoir / ratio，确定性）、LLM（`ILlmClient`，测试用 Fake）。
- [x] `RecipeRunner`：校验门禁、拓扑分层、fail-fast + 下游 Skipped、取消、进度回调、节点日志。
- [x] 执行记录持久化：`ExecutionStore` 保存 `{runId}.json` + `{runId}.log`，支持最近列表与详情读取。
- [x] Executions UI：运行列表、状态过滤、节点状态表、日志面板、取消按钮。
- [x] MainWindow 运行按钮接入 Runner，运行后写入 ExecutionStore 并刷新列表；`Task.Delay` 模拟已删除。
- [x] LLM HTTP 客户端：OpenAI Chat Completions、OpenAI Responses、Anthropic Messages；密钥从环境变量读取；节点级超时与指数退避重试。
- [ ] 节点间输出当前在内存物化，大数据阶段改为流式或溢写磁盘。

#### 范围与实现方式

1. **执行模型**
   - `Models/ExecutionRun.cs`：RunId、RecipeId、Status、StartedAt、FinishedAt、NodeResults、Logs、OutputDirectory。
   - `ExecutionNodeState`：Pending/Running/Succeeded/Failed/Skipped/Cancelled、耗时、输入/输出行数、错误。
   - `ExecutionProgress`：当前节点、完成节点数、总节点数、当前行数。
2. **执行契约**
   ```csharp
   public interface IBlockExecutor
   {
       BlockType Type { get; }
       Task<BlockExecutionResult> ExecuteAsync(BlockExecutionContext context, CancellationToken ct);
   }
   ```
   - `BlockExecutionContext` 提供输入流、配置、日志回调、进度回调。
   - 输入/输出统一为 `IAsyncEnumerable<DatasetRow>`，避免整批载入内存。
3. **Runner**
   - `Services/RecipeRunner.cs`：先调用 `RecipeValidationService`，有 Error 直接拒绝执行。
   - 使用 Kahn 拓扑排序分层；MVP 先按层内顺序执行，后续可并行。
   - 失败策略：默认 fail-fast，当前节点失败后下游标记 Skipped。
   - 进度通过 `IProgress<ExecutionProgress>` 汇报；每处理 N 行或状态变化时上报。
   - 取消：响应 `CancellationToken`，1 秒内停止并清理临时输出。
4. **执行器（MVP 顺序）**
   - Seed：通过 `JsonlDatasetReader` 逐行输出。
   - Expression：先实现 `{{ field }}` 变量替换与字段映射子集，用 `IExpressionEngine` 隔离，后续替换为 Jinja 引擎。
   - Validator：实现表达式/JSON Schema 二选一的最小校验，输出 pass/reject 两条流。
   - Sampler：确定性随机种子；支持按数量或比例采样，输出单流；切分后续扩展。
   - LLM：定义 `ILlmClient`，实现 OpenAI-compatible HTTP 客户端；测试用 Fake。
5. **执行记录**
   - `Services/ExecutionStore.cs`：保存到 `%AppData%/LMTrainingDataStudio/executions/{runId}.json`，日志单独 `.log`。
   - 提供最近运行列表、按 RunId 读取详情。
6. **UI**
   - `IsEditorView` 真正切换 Editor / Executions 两个视图。
   - Executions 视图：运行列表、状态下拉、节点状态表、日志面板、取消按钮。
   - 状态栏展示真实进度和当前节点。

#### 交付物

- `Models/ExecutionRun.cs`
- `Services/RecipeRunner.cs`、`Services/ExecutionStore.cs`、`Services/Executors/*`
- `ViewModels/ExecutionsViewModel.cs`、`Views/ExecutionsView.axaml`
- 修改 `MainWindowViewModel`、`MainWindow.axaml`、状态栏绑定

#### 验收标准

- [ ] 示例 3 节点 Recipe（Seed → Validator → Sampler）在 fixture 上端到端执行，输出行数与预期一致。
- [ ] 任意节点抛异常时，该节点标记 Failed，下游 Skipped，Run 标记 Failed，错误包含节点名和原因。
- [ ] 点击取消后 1 秒内停止，Run 状态为 Cancelled，不产生半成品输出。
- [ ] 同一 Recipe 使用相同 seed 运行两次，Sampler 输出完全一致。
- [ ] 运行记录在重启应用后仍可查看；日志包含节点开始/结束/失败。
- [ ] Runner 单元测试覆盖：拓扑顺序、拒绝环、fail-fast、取消、进度回调。
- [ ] Editor/Executions 切换不再是无响应按钮。

#### 收尾动作

- 删除 `RunRecipeAsync` 中的 `Task.Delay` 模拟代码。
- 更新 README 执行相关说明。
- 阶段提交。

---

### Phase 3 — 编辑器 UI（P0/P1）

#### 目标

把画布和属性面板从“演示”变成“可配置、可撤销、可导航”的编辑器。

#### 范围与实现方式

1. **画布背景与网格**
   - 新增 `Controls/GridBackground.cs`，在 `Render(DrawingContext)` 中按视口和缩放绘制主/次网格线。
   - 低缩放时减少网格密度，避免线条过多。
2. **网格吸附**
   - 统一 `Snap(value)`：`EnableGridSnap ? Math.Round(value / GridSnapSize) * GridSnapSize : value`。
   - 应用到节点拖动、拖放落点、粘贴位置；按住 Alt 临时关闭吸附。
3. **缩放以鼠标为中心**
   - 滚轮缩放时按锚点修正偏移：
     ```csharp
     var ratio = newScale / oldScale;
     _canvasOffsetX = pointer.X - (pointer.X - _canvasOffsetX) * ratio;
     _canvasOffsetY = pointer.Y - (pointer.Y - _canvasOffsetY) * ratio;
     ```
   - 状态栏实时更新缩放百分比。
4. **Fit to View / 自动布局**
   - Fit：计算所有节点包围盒，按可用视口取缩放并居中。
   - 自动布局：按拓扑层级排布，层间距/节点间距可配置；布局结果作为一次 Undo 记录。
5. **Mini-map**
   - 增加当前视口矩形（根据 `_canvasOffset`、`_canvasScale`、画布尺寸反算）。
   - 点击/拖动 Mini-map 移动视口。
6. **交互补齐**
   - 画布双击空白处添加节点（与空状态提示一致）。
   - 复制/粘贴时同时复制两端都被选中的边，并重映射 ID。
   - 右键 Rename 聚焦属性面板名称输入框或就地编辑。
   - 左右 GridSplitter 改为可调整列宽（`ColumnDefinition` 固定初始宽度，中心 `*`）。
   - Undo/Redo 增加菜单项/快捷键提示，并支持 `Ctrl+Shift+Z`。
7. **属性面板**
   - 按 BlockType 提供专属编辑器（Seed/LLM/Expression/Validator/Sampler/ToolProfile）。
   - 文本编辑在失焦/Enter 时提交，并作为一次 Undo 记录。
   - 属性面板删除按钮改走 `DeleteBlockCommand`，可撤销。
   - 显示该节点校验问题列表，点击问题定位端口/字段。
8. **Tool Schema 编辑器**
   - 使用 `TreeView` 编辑 `ToolSchemaNode`：增删、重命名、类型、必填、枚举、默认值、描述。
   - 支持 JSON 导入/导出，错误提示到具体节点。
9. **Prompt / Jinja 编辑器**
   - 多行编辑 + 变量插入 + 渲染预览（依赖 Expression 引擎接口）。
10. **端口坐标**
    - 端口命中测试与连线端点改为读取实际控件位置（`TranslatePoint` / `TransformToVisual`），删除 `55 + index * 18` 估算。

#### 验收标准（手工脚本）

- [ ] 双击空白处能添加节点；提示文案与实际行为一致。
- [ ] 拖动/拖放/粘贴节点均按网格吸附；Alt 可临时关闭。
- [ ] 滚轮缩放时鼠标指向的节点位置保持不动。
- [ ] Fit to View 后所有节点可见且居中。
- [ ] Mini-map 显示视口矩形；点击 Mini-map 可跳转视口。
- [ ] 复制两个相连节点，粘贴后节点和它们之间的边都存在。
- [ ] 每个节点类型的配置项可编辑，保存/加载 Recipe 后值不丢。
- [ ] 属性面板修改名称/配置后 `Ctrl+Z` 可撤销；删除节点可撤销。
- [ ] Tool Schema 增删改后导出 JSON 可被 OpenAI 工具格式消费。
- [ ] 拖动左右分隔条能改变面板宽度。
- [ ] 画布/属性面板修改不影响其他节点选中状态。

#### 测试

- 单测：吸附计算、缩放锚点计算、Fit 缩放计算、拓扑布局层号、Tool Schema JSON 往返。
- Headless UI：添加节点、连线、删除、撤销、复制粘贴（Phase 5 补齐）。

---

### Phase 4 — 数据能力（P1）

#### 目标

完成“打开 → 预览 → 校验 → 修复 → 转换 → 编辑 → 保存”的数据闭环。

#### 范围与实现方式

1. **数据集读写抽象**
   - `IDatasetReader` / `IDatasetWriter`：`OpenAsync`、`ReadPageAsync`、`WriteAsync`、`TotalRows`。
   - 实现：`JsonlDatasetReader/Writer`、`CsvDatasetReader/Writer`、`ParquetDatasetReader/Writer`。
   - CSV 使用 CsvHelper；Parquet 使用 Parquet.Net；统一映射到 `DatasetRow`。
2. **格式转换**
   - `DatasetFormat` 增加 `Dpo`、`Pretrain`。
   - 新增规范模型：`DpoSample { Chosen, Rejected }`、`PretrainSample { Text }`。
   - 批量转换服务：流式读取 → 转换 → 写目标文件 → 错误行写入 `.errors.jsonl`；进度和取消。
   - 转换矩阵：OpenAI/ShareGPT/Alpaca/DPO/Pretrain 之间明确支持与不支持的关系；不支持时报错，不静默降级。
3. **校验与修复**
   - 校验规则分层：语法 → 结构（messages/roles/content/tools）→ 业务（空样本、重复、长度）。
   - 问题列表 UI：级别、行号、路径、消息、跳转。
   - 自动修复：预览修复前后差异，确认后写新文件；原文件不动。
4. **数据编辑**
   - 分页虚拟化表格（DataGrid/ItemsRepeater），只加载当前页。
   - 行编辑、插入、删除；保存时流式重写目标文件。
   - 搜索/正则替换：预览匹配数，支持当前页/全文件。
5. **去重 / 混洗 / 切分**
   - 去重：哈希键 + SQLite 临时表（复用缓存能力）或外部分区，避免全量进内存。
   - 混洗：随机 key + 外排，保证可复现种子。
   - 切分：按比例/份数流式写多文件，输出行数总和等于输入。
6. **统计**
   - 角色分布、长度分布、空样本、重复率、工具调用数。

#### 验收标准

- [ ] 1M 行 JSONL 打开、分页预览、校验、转换全程不一次性加载全文件，内存 ≤ 300MB。
- [ ] 任意两种支持格式批量转换后行数一致；失败行有行号和原因。
- [ ] DPO/Pretrain 往返转换内容语义一致。
- [ ] 自动修复只改目标文件，原文件字节不变；修复预览与结果一致。
- [ ] CSV/Parquet 打开、预览、保存回读内容一致。
- [ ] 去重/混洗/切分输出行数守恒；相同种子结果可复现。
- [ ] 搜索/替换有匹配预览和取消入口。

---

### Phase 5 — 收尾与大规模测试（P1/P2）

#### 目标

达到发布候选质量：全量回归、性能达标、可打包、可安装、文档完整。

#### 范围

1. **完整测试矩阵**
   - 全部 core 单测。
   - Avalonia Headless UI 测试：画布添加/连线/删除/撤销、设置窗口、Executions 切换。
   - 集成测试：1M 行 JSONL 全链路（打开→校验→转换→保存）。
   - 畸形输入：截断 JSON、超长行、非法 UTF-8、空文件、单行无换行、重复边、超大节点数。
   - 跨平台 smoke（如目标包含 Linux/macOS）。
2. **性能与内存**
   - 按 2.6 基准确认；使用 `dotnet-counters` / BenchmarkDotNet 记录结果。
   - 大数据测试样本生成脚本纳入仓库。
3. **工程化**
   - CI：build + test + 格式检查。
   - 发布：self-contained/单文件发布配置、版本号、安装包。
   - LICENSE、应用图标、Assets、README 截图/示例。
   - 日志：文件滚动日志、全局异常处理、崩溃报告入口。
   - 主题资源化：亮/暗主题切换真正生效。
4. **收尾清单**
   - 清理所有未接线字段、未使用转换器、过时注释。
   - 将本文件中已完成项全部勾选；未完成项明确延期原因。
   - 输出 release notes 与已知问题列表。

#### 验收标准

- [ ] `dotnet build` 0 警告；`dotnet test` 全部通过。
- [ ] 1M 行基准满足 2.6；10M 行不崩溃（允许更长时间）。
- [ ] 发布产物在干净环境可启动，能打开示例数据并完成一次转换。
- [ ] CI 在干净环境通过。
- [ ] 无 P0/P1 已知缺陷；P2 缺陷有记录。
- [ ] LICENSE、图标、README、示例 Recipe 齐全。
- [ ] 亮/暗主题切换后无不可读文本或硬编码颜色残留。

---

### Phase 6 — 模拟 API 测试与 SFT 轨迹生成（P0，Round 6）

#### 目标

在不调用真实模型、不执行真实工具的前提下，模拟“用户 query → 多轮 LLM/tool 交互 → 清洗 → OAI messages 导出”的完整数据生成链路，并让工具行为、失败、超时、重试规则可配置、可复现。

#### 当前进度（Round 6，2026-11-16）

- [x] Query Source 节点：同 query 重复（`SameQuery`）、多组 query（`MultipleQueries`）、可配置 system prompt。
- [x] Simulation 节点：`ScriptedChatClient` + `SimulationHarness` 多轮轨迹；记录 assistant `tool_calls` 与 `tool` 消息。
- [x] mini harness 规则：`ToolHarnessRules`（工具白名单、默认超时、重试次数、重试延迟、失败中止）+ 单工具规则（成功/失败/超时/前 N 次失败/延迟）。
- [x] 模拟工具目录：python / web_fetch / powershell / bash；只返回确定性模拟响应。
- [x] 轨迹清洗：正则替换、空白/换行规范化、ANSI 清理、移除 reasoning、丢弃空消息；Validator 可选 `Cleanup`。
- [x] SFT 导出：Simulation 输出 OAI `messages` + `tools`；SftExport 节点原子写 JSONL。
- [x] 4 个新 BlockType 接入 `BlockConfig`、默认配置、深拷贝、Recipe 校验、Block Sheet、执行器注册。
- [x] 通用 Block Config JSON 编辑器（带类型校验与错误提示）。
- [x] 端到端测试：Query → Simulation → Cleaner → SftExport 由 `RecipeRunner` 真跑并产出 JSONL。
- [ ] 真实 tool-calling provider 客户端（OpenAI Chat function calling / Anthropic tool_use）。
- [ ] 真实 python / PowerShell / bash 沙箱执行（进程隔离、超时、资源限制）。
- [ ] 新节点专属属性面板（当前用 JSON 编辑器）。
- [ ] 并发 query、模型成本/延迟统计、失败样本单独输出。

#### 范围与实现方式

1. **数据模型（`Models/SimulationConfig.cs`、`Models/DatasetCleanup.cs`）**
   - `QuerySourceBlockConfig(string[] Queries, QuerySetMode Mode, int Repeat, string? SystemPrompt)`。
   - `SimulationBlockConfig(ModelAlias, MaxRounds, SystemPrompt, Tools, Rules, Script, FinalContent, Provider, Temperature, MaxTokens, TimeoutSeconds, ModelMaxRetries, EmitIncompleteTrajectories)`。
   - `SimulatedToolRule`：name / description / OpenAI parameters JSON / behavior / response template / error / delay / failures-before-success / timeout。
   - `ToolHarnessRules`：allowed tools、default timeout、max retries、retry delay、abort-on-tool-failure。
   - `SimulatedTurn` / `SimulatedToolCallSpec`：确定性脚本回合。
   - `DatasetCleanupRules`：regex replacements、whitespace、newline、ANSI、reasoning、empty-message 规则。
2. **Mini harness（`Services/Simulation/`）**
   - `IChatCompletionClient` + `ScriptedChatClient`：请求携带过滤后的 tool definitions 与轮次。
   - `IToolRuntime` + `SimulatedToolRuntime`：单次工具尝试；失败计数按轨迹重置。
   - `SimulationHarness`：system/user 初始化、逐轮模型调用、工具白名单、每工具超时、重试与延迟、错误回填/中止、max rounds、取消、诊断日志。
   - `TrajectoryResult`：messages、tools、status（Completed / MaxRoundsExceeded / Aborted）、轮数、工具调用/重试次数。
3. **清洗与导出**
   - `TrajectoryCleaner`：对 `messages[].content`（字符串或 text 数组）与 `tools[].function.description` 应用规则；未知字段保留。
   - `TrajectorySerializer`：仅输出 `messages` + `tools`，使用宽松 JSON 编码保留中文。
   - `SftExportBlockExecutor`：流式写临时文件，完整枚举后 `File.Move(overwrite: true)`；取消/异常删除临时文件。
4. **执行器**
   - `QuerySourceBlockExecutor`、`SimulationBlockExecutor`、`CleanerBlockExecutor`、`SftExportBlockExecutor`。
   - `SimulationBlockExecutor` 每行独立 harness/runtime；未完成轨迹默认跳过并写日志。
   - `ValidatorBlockExecutor` 在规则通过后可选应用 `DatasetCleanupRules`。
5. **UI**
   - Block Sheet 新增 Query Source / Simulation / Cleaner / SFT Export。
   - 属性面板新增通用 Config JSON 编辑器：反序列化为 `BlockConfig`，校验 kind 与 BlockType 一致后替换配置。
   - 新节点颜色与图标资源。

#### 交付物

- `Models/SimulationConfig.cs`、`Models/DatasetCleanup.cs`
- `Services/Simulation/ChatCompletionContracts.cs`、`ScriptedChatClient.cs`、`SimulatedToolRuntime.cs`、`SimulationHarness.cs`、`TrajectoryCleaner.cs`、`TrajectorySerializer.cs`
- `Services/Executors/QuerySourceBlockExecutor.cs`、`SimulationBlockExecutor.cs`、`CleanerBlockExecutor.cs`、`SftExportBlockExecutor.cs`
- 修改 `Models/BlockConfig.cs`、`Models/RecipeBlock.cs`、`Services/RecipeValidationService.cs`、`Services/Executors/ValidatorBlockExecutor.cs`、`ViewModels/*`、`Views/PropertyPanelView.axaml`
- 测试：`SimulationHarnessTests`、`TrajectoryCleanerTests`、`SimulationExecutorsTests`、`BlockNodeViewModelTests`，扩展 `BlockConfigSerializationTests`、`RecipeValidationServiceTests`

#### 验收标准

- [x] `SameQuery` 输出固定重复次数；`MultipleQueries` 按列表与重复次数展开。
- [x] 脚本化多轮轨迹包含 assistant `tool_calls`、tool 结果与最终 assistant 消息，`tool_call_id` 对应。
- [x] 工具白名单外调用被拒绝且不执行，错误作为 tool 消息回填。
- [x] 工具失败按 `MaxRetries` 重试；超过后错误回填并继续，或按 `AbortOnToolFailure` 中止。
- [x] 工具超时被记录为失败并进入重试策略；模型调用受 `TimeoutSeconds` 约束。
- [x] 超过 `MaxRounds` 的轨迹标记 `MaxRoundsExceeded`，默认不输出。
- [x] 取消能中止 harness；取消不产生 SFT 输出文件。
- [x] 清洗后的行仍是合法 OAI messages JSON，未知字段保留。
- [x] SFT 导出文件为逐行 JSONL，原子替换，不残留 `.tmp`。
- [x] Query → Simulation → Cleaner → SftExport 端到端测试通过。
- [ ] 真实 provider function calling 往返（留待后续）。
- [ ] 真实沙箱工具执行与资源限制（留待后续）。

#### 收尾动作

- [x] 更新本文件 Round 6 进展与 Phase 6 状态。
- [x] 更新 README 当前状态、节点列表与路线图。
- [x] 运行 `dotnet build` / `dotnet test`，记录 0 警告与 200 通过。
- [ ] 真实 provider / 沙箱执行 / 专属面板完成后再次验收。

---

## 四、详细缺口清单（按领域）

> 以下是当前代码的全部已知未完成项，作为 Phase 1–5 的工作池。完成后在对应项打勾。

### 4.1 文件与数据集 IO

- [ ] 文件打开 / 保存 / 另存为（无任何 FilePicker 调用）。
- [ ] JSONL 流式分页预览与数据表格。
- [ ] 大文件随机行跳转 UI。
- [ ] CSV 读写（CsvHelper 已引用未使用）。
- [ ] Parquet 读写（Parquet.Net 已引用未使用）。
- [ ] `DataFileType` 枚举接线（当前无引用）。
- [ ] 数据增删查改、保存回原文件。
- [ ] 去重、混洗、切分。
- [ ] 正则搜索与替换。
- [ ] 输出预览弹窗 / 样本查看器。
- [ ] 最近文件、脏标记、关闭确认。
- [ ] 文件编码、CRLF、超长行、无结尾换行专项处理。
- [ ] 数据集统计（角色、长度、空样本、重复率、工具调用）。

### 4.2 格式转换与校验

- [ ] `DatasetFormatConverter`、`DatasetValidationService` 接线到 UI（当前仅实例化未调用）。
- [ ] 批量转换 UI / 服务、进度、失败行报告。
- [ ] DPO 格式（枚举与转换器均缺失）。
- [ ] Pretrain 格式（枚举与转换器均缺失）。
- [ ] 未知格式不再静默 fallback，给出明确错误。
- [ ] Alpaca 多轮转换不丢轮次、不丢 tools。
- [ ] 转换保留额外字段 / 元数据，或明确声明丢弃策略。
- [ ] ShareGPT tool_call 解析失败上报，不静默吞异常。
- [ ] `AttachTools` 不插入空 system 消息。
- [ ] JSONL 结构校验（messages/roles/content/tools）。
- [ ] 自动修复入口、预览、另存。
- [ ] 校验问题列表、行号跳转、级别筛选。
- [ ] 校验错误上限/采样，避免超大坏文件吃满内存。
- [ ] HuggingFace 数据源加载。

### 4.3 Recipe 节点模块

- [x] 节点配置模型：`BlockConfig` 多态层级 + `RecipeBlock.Config`，校验按配置类型检查（Round 2）。
- [ ] 节点端口类型化（当前固定 input/output + any）。
- [ ] 端口增删、改名、改类型（属性面板只读展示）。
- [x] Recipe 序列化 / 反序列化 / schema 版本（`RecipeStore`，Round 2）。
- [ ] 节点状态与错误徽标。
- [x] Seed 数据源文件选择、文件类型、Index Cache 编辑（Round 5）。
- [ ] Seed Streaming 开关、数据预览、过滤。
- [x] LLM Provider（OpenAI Chat Completions / Responses / Anthropic Messages）、ModelAlias、Endpoint、温度、max tokens、Prompt（Round 5）。
- [x] LLM 节点级超时与指数退避重试（Round 5）。
- [x] LLM 密钥设置界面：settings.json 本机保存，环境变量兜底（Round 5）。
- [ ] LLM 结构化输出、并发控制。
- [ ] Expression：Jinja2 模板、变量映射、渲染预览、语法校验、沙箱。
- [ ] Validator：代码/Schema 编辑、pass/reject 双输出、失败样本、失败率、超时/沙箱。
- [ ] Sampler：数量/比例、随机种子、切分、多输出、分层采样。
- [ ] Tool Profile：`ToolSchemaNode`/`ToolProfile` 全文件未被引用；树形编辑器、JSON 导入导出、MCP。
- [ ] 节点配置校验覆盖所有类型。
- [x] 环、孤立节点、重复边、端口方向/类型兼容校验（Round 3）。
- [x] 模拟 API 测试子节点：Query Source / Simulation / Cleaner / SFT Export（Round 6）。
- [x] 多轮 tool-calling 轨迹记录与工具白名单 / 失败 / 超时 / 重试规则（Round 6）。
- [x] 轨迹清洗（正则替换、格式规范化、ANSI、reasoning、空消息）与 Validator 可选清洗（Round 6）。
- [x] OAI messages JSONL 导出（Round 6）。
- [ ] 真实 tool-calling provider 客户端（OpenAI function calling / Anthropic tool_use）。
- [ ] 真实 python / PowerShell / bash 沙箱执行与资源限制。
- [ ] Query / Simulation / Cleaner / SFT Export 专属属性面板（当前通用 JSON 编辑器）。
- [ ] 并发 query、模型成本/延迟统计、失败样本单独输出。

### 4.4 编辑器 UI / Canvas

- [ ] 网格背景绘制（XAML 注释存在，实际未绘制）。
- [ ] 网格吸附生效（设置当前无效）。
- [ ] 缩放以鼠标为中心（当前向原点漂移）。
- [ ] Fit to View。
- [ ] 自动布局。
- [ ] Mini-map 视口矩形、点击/拖动导航。
- [ ] 双击空白添加节点（提示与行为不一致）。
- [ ] 复制粘贴携带内部连线。
- [ ] 系统剪贴板 / 跨实例粘贴。
- [ ] 右键 Rename 真正重命名。
- [ ] 左右 GridSplitter 可调整宽度（当前固定 200/320）。
- [ ] Undo/Redo 菜单/按钮入口与 `Ctrl+Shift+Z`。
- [ ] 属性面板删除可撤销。
- [ ] 属性修改进入 Undo 历史。
- [ ] 快捷键全局化，避免焦点冲突。
- [ ] 端口坐标读取真实控件布局（当前 `55 + index * 18` 硬编码）。
- [ ] 节点尺寸调整手柄。
- [ ] 边 hover 高亮、动画、标签避让。
- [ ] 样本/输出预览弹窗。
- [x] 属性面板 Seed 专属配置（数据源/文件类型/Index Cache）。
- [x] LLM 专属配置（Provider/Model/Endpoint/温度/Max tokens/Prompt）。
- [ ] 其余节点（Expression/Validator/Sampler/ToolProfile）专属配置项。
- [ ] Prompt / Jinja 编辑器。
- [ ] Tool Schema 树形编辑器。
- [x] 通用 Block Config JSON 编辑器（Round 6）。
- [ ] Query / Simulation / Cleaner / SFT Export 专属属性面板。
- [ ] 节点校验摘要与画布错误标记。
- [x] Executions 视图与运行详情（Round 4）。
- [ ] 状态栏 `TotalLines`、`CurrentFilePath`、缩放真实数据。
- [ ] 内存占用定时刷新（当前只在构造时取一次）。
- [ ] 未保存更改指示与关闭确认。
- [ ] 画布内容持久化（当前重启回到示例 Recipe）。

### 4.5 运行时与执行

- [x] 真实 Recipe 执行引擎（服务层，Round 4）。
- [x] 节点执行器与执行上下文。
- [x] 拓扑排序与依赖传递（层内串行；并行执行未做）。
- [x] Execution / NodeState / 日志模型（内存态）。
- [x] 取消（运行级与节点级）。
- [ ] 暂停 / 重试。
- [x] MainWindow 运行按钮接入 Runner，删除 `Task.Delay` 模拟。
- [x] 异常处理与 `IsRunning` 恢复。
- [x] Runner 按节点/行数上报进度。
- [x] 状态栏展示当前节点与进度；Executions UI 展示节点结果。
- [ ] 状态栏 `CurrentFilePath`、缩放等其余占位数据。
- [x] 运行日志持久化与历史记录（`ExecutionStore`，UI 待接）。
- [ ] 输出目录、命名、结果摘要。
- [ ] 中断恢复与临时文件清理。
- [ ] 节点间输出当前在内存物化，需改为流式或溢写磁盘。
- [x] 模拟 API 多轮执行（确定性模型 + 模拟工具）与四节点 SFT 轨迹流水线（Round 6）。
- [ ] 真实模型 tool-calling 与真实工具执行。

### 4.6 设置、主题与生命周期

- [x] 设置持久化：`ISettingsStore`/`SettingsStore`（Round 2）。
- [ ] 暗/亮主题切换（`App.axaml` 固定 Dark）。
- [ ] 硬编码颜色资源化。
- [ ] 网格吸附设置生效。
- [ ] 数值输入校验与范围提示。
- [ ] 缓存目录浏览按钮与可写性检查。
- [ ] API 密钥加密存储（当前 settings.json 明文，仅限本机使用）。
- [ ] 设置变更通知已创建的服务。
- [ ] 窗口/应用图标（`Assets/` 为空）。
- [ ] 单实例 / 文件关联 / 命令行打开。
- [ ] 全局异常处理与崩溃日志。

### 4.7 代码质量与已知缺陷

- [ ] `_datasetValidation`、`_formatConverter` 未使用字段。
- [ ] `BlockTypeToColorConverter`、`InverseBoolConverter` 未使用。
- [ ] `MainWindowViewModel.AddBlock` RelayCommand 未绑定。
- [ ] `BlockTemplateViewModel.ColorKey` 未使用。
- [ ] `ToolSchemaNode`、`ToolProfile` 未使用（`DataFileType` 已接入 Seed 配置与校验）。
- [ ] `RecipeValidationService` 校验缺口（见 4.3）。
- [ ] `ValidationIssue` 缺少错误码、列号、分组信息。
- [ ] `Task.Run` + `Dispatcher.Post` 模式异常不可传播。
- [ ] 日志无级别、时间戳、来源。
- [ ] Recipe / 设置 / 缓存无 schema 版本与迁移策略。
- [ ] 大文件内存策略无基准验证。

### 4.8 测试

- [x] 测试项目建立，加入解决方案。
- [x] core 最小单测：格式转换、JSONL 校验、Recipe 校验、索引缓存、Undo 历史（29 个）。
- [x] `BlockConfig` 多态序列化、默认工厂、深拷贝、类型/配置匹配校验（19 个）。
- [x] `RecipeStore`：保存/加载往返、JSON 格式、覆盖写与临时文件清理、坏 JSON/缺失配置/版本校验、取消（9 个）。
- [x] `SettingsStore`：默认路径/默认值、保存加载往返、非法值回退与警告、坏 JSON、覆盖写、取消（9 个）。
- [x] JSONL 读取与索引：分页/单行、末行无换行、CRLF/BOM、超长行、源文件变更重建、容量 LRU 清理、空文件与边界（14 个）。
- [x] 校验器补强：孤立节点、重复边、端口归属/方向、DataType 兼容（6 个）。
- [ ] 格式转换扩展测试：DPO、Pretrain、多轮 Alpaca、extra fields。
- [ ] 索引缓存边界：缓存失效、容量清理、并发打开、超长行。
- [x] Recipe 执行引擎单测：端到端、fail-fast、取消、进度、拒绝执行（5 个）。
- [x] 节点执行器单测：Seed / Expression / Validator / Sampler / LLM（12 个）。
- [x] `ExecutionStore`：往返、json/log 分离、最近排序、坏 JSON、非法 runId、取消、覆盖写、缺失日志（10 个）。
- [x] `ExecutionsViewModel`：刷新排序、状态过滤、详情/日志加载、刷新并选中（4 个）。
- [x] Seed 配置编辑：字段回写、编辑器保持、Config 替换重建、非 Seed 无编辑器、错误清除、非 JSONL 校验警告（6 个）。
- [x] LLM 接入：三种 Provider 请求/解析、密钥与错误、别名路由、重试/超时（12 个）；LLM 配置编辑器（4 个）；未知 Provider/超时/重试校验（3 个）。
- [x] `LlmCredentials`：设置优先、环境兜底、未知 Provider、无环境变量（4 个）；设置往返包含密钥。
- [x] 模拟 harness：多轮、白名单、失败重试、超时、max rounds、abort、取消、OAI 序列化、非 ASCII 保留（14 个）。
- [x] 轨迹清洗：正则、空白/换行、ANSI、reasoning、空消息、数组 content（9 个）。
- [x] 模拟执行器与端到端流水线：Query / Simulation / Cleaner / SftExport、取消清理（16 个）。
- [x] 新配置序列化/深拷贝、Config JSON 编辑器、配置校验（15 个）。
- [ ] ViewModel 单测：加载/保存、运行状态、异常恢复。
- [ ] Avalonia Headless UI 测试。
- [ ] 集成测试：1M 行全链路。
- [ ] 畸形输入测试集与 fixtures。
- [ ] 性能基准（BenchmarkDotNet 或等价）。
- [ ] CI 自动化。

### 4.9 工程化与发布

- [ ] LICENSE。
- [ ] Assets：图标、截图、示例 Recipe。
- [ ] 发布/打包配置（self-contained、单文件、安装包、版本号）。
- [ ] CI/CD 流水线。
- [ ] `.editorconfig` / 分析器 / 警告即错误。
- [ ] 日志框架（文件滚动、导出）。
- [ ] 中英文文案统一 / 本地化。
- [ ] README 路线图与实现状态同步。
- [x] 构建环境：`net9.0` + `global.json` 已落地，本机构建通过。

---

## 五、优先级与建议顺序

### P0（先让产品“能用”）

1. Phase 1：强类型节点配置、Recipe/设置持久化、JSONL Reader。
2. Phase 2：执行引擎、执行器、执行记录、Executions UI。
3. 文件打开/保存/另存为 + 状态栏真实数据。
4. 属性面板节点配置 + 删除/修改接入 Undo。
5. 设置持久化、网格吸附、主题切换真正生效。
6. 格式转换/校验服务接线。
7. 每个 Phase 随代码补最小单测；Phase 5 做完整矩阵。
8. 模拟 API 测试与 SFT 轨迹生成（Round 6 已完成核心；真实 provider/沙箱为 P1）。

### P1（补齐数据工作台能力）

1. CSV / Parquet 读写与互转。
2. DPO / Pretrain 格式。
3. 索引缓存容量、失效、随机读取完整接线。
4. 数据 CRUD、去重、混洗、切分、正则替换。
5. 校验问题列表、行号跳转、自动修复流程。
6. 大数据分页预览与统计。
7. Canvas 网格/吸附/Fit/Mini-map/复制连线。
8. 取消/重试、日志持久化、输出摘要。

### P2（体验与工程化）

1. 自动布局、边动画、样本预览。
2. Tool Schema 树形编辑器、MCP 配置。
3. 主题资源化、图标、Assets。
4. 安装包/文件关联/自动更新。
5. CI/CD、分析器、日志、本地化。
6. LICENSE 与文档同步。

---

## 六、每轮结束的收尾模板

每个 Phase/每轮开发结束时按以下顺序收尾：

1. 运行 `dotnet build`，确认 0 警告。
2. 运行 `dotnet test`，确认全绿；新增逻辑必须有测试。
3. 按本阶段“验收标准”逐条手工验证，记录未通过项。
4. 删除本阶段产生的临时代码、死字段、调试输出。
5. 更新本文件勾选状态与“本轮进展”。
6. 更新 README 中受影响的说明。
7. 提交：提交信息写明阶段、break change、验证命令与结果。