# LLM Training Data Studio

LLM Training Data Studio 是一个基于 **C# / Avalonia** 的桌面端训练数据处理工作台，面向 LLM 预训练、SFT、DPO 和工具调用数据集的可视化编辑、验证、转换与流水线编排。

项目目标是为 JSONL / CSV / Parquet 等常见训练数据格式提供专业级 GUI：支持大文件流式处理、数据格式互转、DAG Recipe 编排、可视化 Tool Schema 编辑、异步执行、实时进度与结果预览。

## 当前状态

当前版本已完成应用骨架、核心服务与可执行 Recipe 引擎：

- Avalonia 桌面应用与三栏式 Studio UI（Block Sheet / Canvas / Property Panel / Status Bar）
- Recipe / 设置持久化：原子写入、camelCase 多态 JSON、schema 版本校验
- JSONL SQLite 行索引缓存与分页读取
- OpenAI / ShareGPT / Alpaca 数据格式转换服务初版
- 真实 RecipeRunner：Seed / Expression / Validator / Sampler / LLM 执行器，执行记录与 Executions UI
- OpenAI Chat Completions / Responses、Anthropic Messages 客户端；密钥从设置或环境变量读取，支持节点级超时与指数退避重试
- 模拟 API 测试：Query Source → Simulation（mini harness）→ Cleaner → SFT Export，多轮 tool 轨迹与 OpenAI messages JSONL 导出
- 真实 provider tool-calling：OpenAI Chat function calling、OpenAI Responses `function_call`、Anthropic Messages `tool_use`，含 token usage / 延迟 / 成本统计；已通过真实 OpenAI 兼容端点冒烟（tool_calls + usage + 两轮 harness 往返）
- 真实沙箱工具：python / PowerShell / bash / web_fetch；环境白名单、临时目录、超时杀进程树、输出上限，Windows 额外限制内存与进程数
- Simulation 支持并发 query（输出保序）与失败轨迹独立 JSONL 输出
- Query Source / Simulation / Cleaner / SFT Export 专属属性面板
- 通用 Block Config JSON 编辑器，可直接配置所有节点类型

> 注意：当前仍是早期开发版。**Recipe 新建/打开/保存、数据工作台 UI、自动布局和输出预览弹窗尚未实现**；当前启动会加载内存中的示例 Recipe。
>
> - 完整功能清单与节点能力矩阵：[PLAN/STATUS.md](PLAN/STATUS.md)
> - 分阶段实施计划、阶段目标与验收标准：[PLAN/TODO.md](PLAN/TODO.md)

## 技术栈

- **语言**：C#
- **框架**：Avalonia UI
- **运行时**：.NET 9（项目当前 `TargetFramework` 为 `net9.0`）
- **数据缓存**：SQLite / `Microsoft.Data.Sqlite`
- **CSV 支持**：`CsvHelper`
- **Parquet 支持**：`Parquet.Net`
- **UI 主题**：Avalonia Fluent Theme + Inter Font
- **架构模式**：MVVM

## 主要功能规划

### 0. 模拟 API 测试与 SFT 轨迹生成

默认使用确定性脚本与模拟工具，无需真实 API Key 即可生成可复现的 agent SFT 轨迹；也可以切换到真实 provider 与真实沙箱工具。

- `QuerySourceBlockConfig`：`SameQuery`（同 query 重复）或 `MultipleQueries`（多组 query）。
- `SimulationBlockConfig`：
  - `ModelMode`：`Scripted`（离线回放）或 `Provider`（真实 HTTP tool-calling）。
  - `Script` 确定性回放 assistant 回合；未配置脚本时依次调用所有已暴露工具再收尾。
  - `Tools` 声明工具 schema 与行为；`SimulatedToolCatalog` 内置 python / web_fetch / powershell / bash。
  - `ToolMode`：`Simulated`（确定性响应）或 `Real`（真实沙箱执行）。
  - `Rules` 控制工具白名单、单次超时、重试次数、重试延迟、失败是否中止。
  - `Sandbox` 控制真实工具的超时、输出上限、内存/进程数上限、网络开关、私网开关与解释器路径。
  - `MaxRounds` / `TimeoutSeconds` / `ModelMaxRetries` 控制整体循环与模型调用；`Endpoint` 覆盖 base URL。
  - `MaxConcurrency` 控制 query 并发（输出保持输入顺序）。
  - `FailureOutputPath` / `MaxFailureSamples` 将未完成轨迹单独原子写入 JSONL。
  - `InputCostPerMillionTokens` / `OutputCostPerMillionTokens` 用于估算成本，只写入运行日志。
- `ProviderChatCompletionClient`：OpenAI Chat Completions、OpenAI Responses、Anthropic Messages 的请求构造与 tool call 解析；密钥从应用设置或环境变量读取。
- `RealToolRuntime` + `SandboxProcessRunner`：python（`-I`）、PowerShell（`-NoProfile -NonInteractive -ExecutionPolicy Bypass -File`）、bash（`--noprofile --norc -c`）、web_fetch（默认关闭，阻止私网/回环，禁跟随重定向）。
- `DatasetCleanupRules`：正则替换、空白/换行规范化、ANSI 清理、移除 reasoning、丢弃空消息；Cleaner 节点与 Validator 均可使用。
- `SftExportBlockExecutor`：流式写临时文件，成功后原子替换目标 JSONL；只输出 `messages` 与 `tools`。

> 沙箱是协作式隔离，不是内核级安全边界：进程以当前用户身份运行，未使用容器或命名空间。Windows 通过 Job Object 限制内存与活动进程；其他平台只有超时、进程树终止、环境白名单与输出上限。
>
> 真实 provider / web_fetch 需要配置 API Key 或显式开启网络，且会产生费用；请先在测试数据上小规模验证。

示例配置（Simulation 节点 Config JSON，可粘贴到属性面板的 Config JSON 编辑器）：

```json
{
  "kind": "simulation",
  "modelAlias": "sim-model",
  "maxRounds": 4,
  "systemPrompt": "You are a tool-using agent.",
  "modelMode": "scripted",
  "toolMode": "simulated",
  "maxConcurrency": 4,
  "failureOutputPath": "out/failed.jsonl",
  "maxFailureSamples": 100,
  "inputCostPerMillionTokens": 0,
  "outputCostPerMillionTokens": 0,
  "tools": [
    {
      "name": "python",
      "description": "Execute Python source code and return its output.",
      "parametersJson": "{\"type\":\"object\",\"properties\":{\"code\":{\"type\":\"string\"}},\"required\":[\"code\"]}",
      "responseTemplate": "[simulated python] executed: {{arguments}}"
    }
  ],
  "rules": { "allowedTools": ["python"], "defaultTimeoutMs": 30000, "maxRetries": 1 },
  "script": [
    { "content": "I will run the code.", "toolCalls": [{ "name": "python", "argumentsJson": "{\"code\":\"print(1)\"}" }] }
  ],
  "finalContent": "Done.",
  "timeoutSeconds": 60
}
```

切换到真实调用时，将 `modelMode` 改为 `provider`、`toolMode` 改为 `real`，并配置 `sandbox`：

```json
{
  "modelMode": "provider",
  "toolMode": "real",
  "provider": "openai_chat",
  "endpoint": "https://api.openai.com/v1",
  "sandbox": {
    "timeoutMs": 30000,
    "maxOutputBytes": 65536,
    "maxMemoryMb": 512,
    "maxProcesses": 8,
    "allowNetwork": false,
    "allowPrivateNetwork": false,
    "maxFetchBytes": 262144
  }
}
```

相关实现：

- `LMTrainingDataStudio2/Services/Simulation/`
- `LMTrainingDataStudio2/Services/Sandbox/`
- `LMTrainingDataStudio2/Models/SimulationConfig.cs`
- `LMTrainingDataStudio2/Models/SandboxConfig.cs`
- `LMTrainingDataStudio2/Models/DatasetCleanup.cs`
- `LMTrainingDataStudio2/Services/Executors/SimulationBlockExecutor.cs`

### 节点能力摘要

| 节点 | 执行状态 | 专属面板 | 主要缺口 |
| --- | --- | --- | --- |
| Seed | JSONL 可用 | 有 | CSV/Parquet、数据预览、Streaming 开关 |
| LLM | 三种 provider 可用 | 有 | 多轮/tools/结构化输出、并发、成本统计 |
| Expression | `{{ path }}` 占位引擎 | 无 | Jinja、预览、语法校验 |
| Validator | 表达式规则 | 无 | reject 分支、Schema、失败样本 |
| Sampler | 数量/比例采样 | 无 | Splits、多输出、分层采样 |
| Tool Profile | 仅配置模型 | 无 | 执行器、树形编辑器、被其他节点复用、MCP |
| Query Source | 单组/多组 query | 有 | 模板、外部来源 |
| Simulation | 脚本/真实 provider + 模拟/真实工具 | 有 | Tools/Rules/Script UI、流式、容器隔离 |
| Cleaner | 正则/格式/ANSI/reasoning/空消息 | 部分 | 正则规则 UI、预览、坏行报告 |
| SFT Export | 原子 JSONL | 有 | 文件选择器、预览、拆分、DPO/Pretrain |

完整矩阵与逐项缺口见 [PLAN/STATUS.md](PLAN/STATUS.md)。

### 1. 大文件流式处理

- 面向数百 GB JSONL 文件设计，避免整体加载导致 OOM。
- 在 `tmp/index-cache` 中生成轻量 SQLite Index Cache。
- 缓存内容保存行号与字节偏移，支持快速跳转指定行。
- 支持配置最大缓存容量，默认目标为 512 MB。
- 支持手动清理缓存。

相关实现：

- `LMTrainingDataStudio2/Services/IndexCacheService.cs`

### 2. 多格式支持与互转

计划支持以下主流 LLM 数据格式：

- OpenAI-compatible：`messages[]`
- ShareGPT / LLaMA-Factory：`conversations[]`
- Alpaca：`instruction` / `input` / `output`
- Auto：自动检测输入格式

相关实现：

- `LMTrainingDataStudio2/Models/DatasetFormat.cs`
- `LMTrainingDataStudio2/Services/DatasetFormatConverter.cs`

### 3. 数据验证与修复

- JSONL 逐行异步验证。
- 收集语法错误并返回行号。
- 对简单问题保留自动修复入口。
- Recipe 执行前验证节点、连接和基础配置。

相关实现：

- `LMTrainingDataStudio2/Services/DatasetValidationService.cs`
- `LMTrainingDataStudio2/Services/RecipeValidationService.cs`

### 4. DAG Recipe 编辑器

UI 采用 Recipe Graph / DAG 的设计：

- Seed Block：数据输入源
- LLM / Model Block：模型调用
- Expression Block：Jinja 风格字段变换
- Validator Block：Schema / 代码 / 数据质量校验（可选轨迹清洗）
- Sampler Block：确定性采样与数据切分
- Tool Profile Block：OpenAI-compatible Tool Schema / MCP 工具配置
- Query Source Block：模拟用户 query（同 query 重复 / 多组 query）
- Simulation Block：模拟 API + mini harness（多轮工具调用、白名单、失败、超时、重试）
- Cleaner Block：正则替换与格式规范化
- SFT Export Block：OAI messages JSONL 原子导出

当前 UI 已包含：

- 左侧 Block Sheet
- 中央 Canvas 区域
- 示例节点和连接曲线
- 右侧属性编辑面板
- 悬浮 Run / Validate 控件
- Mini-map 占位区域
- 底部 Status Bar

相关实现：

- `LMTrainingDataStudio2/Views/MainWindow.axaml`
- `LMTrainingDataStudio2/ViewModels/MainWindowViewModel.cs`
- `LMTrainingDataStudio2/Models/RecipeBlock.cs`

## 项目结构

```text
LMTrainingDataStudio2.slnx                  # 解决方案文件
README.md
PLAN/
├── TODO.md                                 # 分阶段实施计划与验收标准
└── STATUS.md                               # 功能清单、节点矩阵与下一阶段规划
LMTrainingDataStudio2/                      # 主项目目录
├── Program.cs                              # 应用入口，配置 Avalonia AppBuilder
├── App.axaml / App.axaml.cs                # Application 定义，主题与资源加载
├── app.manifest                            # Windows 应用清单
├── LMTrainingDataStudio2.csproj            # 项目文件（Avalonia 11.3 / .NET 9）
│
├── Models/                                 # 数据模型层
│   ├── AppSettings.cs                      # 全局设置（缓存容量、主题、网格吸附等）
│   ├── BlockConfig.cs                      # 强类型节点配置（多态 kind）
│   ├── ChatMessage.cs                      # 规范化聊天消息、ToolDefinition、ToolCall、SftSample
│   ├── DatasetCleanup.cs                   # 轨迹清洗规则（正则/格式规范化）
│   ├── DatasetFormat.cs                    # DatasetFormat / DataFileType 枚举
│   ├── ExecutionRun.cs                     # 执行记录模型
│   ├── RecipeBlock.cs                      # Recipe DAG 节点、端口、边、Recipe 容器
│   ├── SandboxConfig.cs                    # 真实工具沙箱限制与执行模式
│   ├── SimulationConfig.cs                 # Query/Simulation/Cleaner/SFT Export 配置与模拟工具目录
│   ├── ToolSchema.cs                       # Tool Schema 树形节点模型、ToolProfile
│   └── ValidationResult.cs                 # 验证结果与 Issue 模型
│
├── Services/                               # 业务服务层（无 UI 依赖）
│   ├── Executors/                          # 节点执行器（Seed/Expression/Validator/Sampler/LLM/Query/Simulation/Cleaner/SftExport）
│   ├── Sandbox/                            # 真实沙箱：进程限制器、python/powershell/bash/web_fetch 运行时
│   ├── Simulation/                         # 模拟 API：scripted/provider client、tool runtime、harness、清洗、OAI 序列化
│   ├── DatasetFormatConverter.cs           # OpenAI / ShareGPT / Alpaca 格式互转
│   ├── DatasetValidationService.cs         # JSONL 逐行语法验证 + 自动修复
│   ├── ExecutionStore.cs                   # 执行记录持久化
│   ├── IndexCacheService.cs                # SQLite 行偏移索引缓存（大文件随机访问）
│   ├── JsonlDatasetReader.cs               # JSONL 分页读取
│   ├── RecipeRunner.cs                     # DAG 执行器
│   ├── RecipeStore.cs                      # Recipe 原子持久化
│   ├── RecipeValidationService.cs          # Recipe DAG 验证（环检测、配置检查）
│   └── SettingsStore.cs                    # 设置持久化
│
├── ViewModels/                             # MVVM ViewModel 层
│   ├── MainWindowViewModel.cs              # 主窗口 VM（Recipe 管理、Run/Validate 命令）
│   ├── BlockNodeViewModel.cs               # 画布节点 VM + PortViewModel
│   ├── BlockTemplateViewModel.cs           # Block Sheet 模板项 VM
│   ├── QuerySourceConfigEditorViewModel.cs # Query Source 专属编辑器
│   ├── SimulationConfigEditorViewModel.cs  # Simulation 专属编辑器（含沙箱参数）
│   ├── CleanerConfigEditorViewModel.cs     # Cleaner 专属编辑器
│   ├── SftExportConfigEditorViewModel.cs   # SFT Export 专属编辑器
│   └── EdgeViewModel.cs                    # 连线 VM
│
├── Views/                                  # Avalonia 视图层
│   ├── MainWindow.axaml / .axaml.cs        # 三栏式主窗口布局
│   ├── RecipeCanvasView.axaml / .axaml.cs  # DAG 画布（节点渲染、贝塞尔曲线连线）
│   └── PropertyPanelView.axaml / .axaml.cs # 右侧属性编辑面板
│
├── Commands/                               # 撤销/重做命令层（Command Pattern）
│   ├── CommandHistory.cs                   # Undo/Redo 栈管理器
│   └── RecipeCommands.cs                   # Move/Add/Delete Block、Add/Delete Edge
│
├── Converters/                             # XAML 值转换器
│   └── BlockTypeToColorConverter.cs        # BlockType → 颜色画刷
│
├── Themes/                                 # 主题资源
│   └── Colors.axaml                        # Block 类型色彩、面板/画布/边颜色定义
│
└── Assets/                                 # 静态资源（图标、字体等，待补充）
```

## 快速开始

### 环境要求

- Windows / Linux / macOS 桌面环境
- .NET SDK 9 或与项目 `TargetFramework` 匹配的 SDK

检查 SDK：

```powershell
dotnet --list-sdks
```

### 还原依赖

```powershell
dotnet restore .\LMTrainingDataStudio2.slnx
```

### 构建项目

```powershell
dotnet build .\LMTrainingDataStudio2.slnx
```

### 运行测试

```powershell
dotnet test .\LMTrainingDataStudio2.slnx
```

### 真实 provider 冒烟测试（可选）

默认测试不会联网；`ProviderSmokeTests` 在没有环境变量时自动跳过。设置以下变量后会真实调用 OpenAI Chat Completions，并验证 tool calling 与两轮 harness 往返。API Key 只放在环境变量中，不要写入仓库：

```powershell
$env:LMTDS_PROVIDER_SMOKE_KEY = "<your-api-key>"
$env:LMTDS_PROVIDER_SMOKE_BASE_URL = "https://api.openai.com/v1"
$env:LMTDS_PROVIDER_SMOKE_MODEL = "gpt-4o-mini"
dotnet test .\LMTrainingDataStudio2.slnx --filter ProviderSmokeTests
```

### 运行项目

```powershell
dotnet run --project .\LMTrainingDataStudio2\LMTrainingDataStudio2.csproj
```

## UI 说明

### Recipe Header

用于编辑 Recipe 名称、切换 Editor / Executions 视图，以及访问全局设置或缓存清理入口。

### Block Sheet

展示可添加的 Block 模板，包括 Seed、LLM、Expression、Validator、Sampler、Tool Profile、Query Source、Simulation、Cleaner 和 SFT Export。

### Canvas Area

用于展示 Recipe DAG。当前已支持：

- Block Sheet 拖放添加、可搜索添加菜单（`~` / 右键）
- 节点拖动、多选拖动、框选、端口连线与边删除
- 中键平移、滚轮缩放、缩放百分比显示
- 基础 Undo / Redo（添加/删除/移动/连线/粘贴块）
- 右键菜单（复制/重命名入口/删除）
- Mini-map 渲染

尚未实现：

- 网格背景与网格吸附
- 鼠标中心缩放、Fit to View、自动布局
- Mini-map 视口矩形与点击导航
- 双击空白添加、复制携带内部连线、内联重命名
- 可拖动分栏、真实端口坐标、节点尺寸手柄

### Property Panel

根据当前选中的 Block 显示配置项，包括：

- Jinja 引用名
- 节点专属配置（Seed / LLM / Query Source / Simulation / Cleaner / SFT Export）
- 通用 Config JSON 编辑器（适用于所有节点，包含 kind 与 BlockType 一致性校验）
- Validation 摘要
- Progress / Logs

### Status Bar

显示当前状态、行数、文件路径、进度条、内存占用和缩放百分比。

> 当前 `CurrentFilePath` 尚未赋值，内存占用只在启动时采样一次；这两项会在 Round 8/12 补齐。

## 数据格式示例

### OpenAI-compatible

```json
{"messages":[{"role":"system","content":"You are helpful."},{"role":"user","content":"Hello"},{"role":"assistant","content":"Hi!"}]}
```

### OpenAI tool-call 轨迹（scripted 模式示例）

```json
{"messages":[{"role":"system","content":"You are a tool-using agent."},{"role":"user","content":"运行 print(1)"},{"role":"assistant","content":"","tool_calls":[{"id":"call_0_0","type":"function","function":{"name":"python","arguments":"{\"code\":\"print(1)\"}"}}]},{"role":"tool","tool_call_id":"call_0_0","content":"[simulated python] executed: {\"code\":\"print(1)\"}"},{"role":"assistant","content":"Done."}],"tools":[{"type":"function","function":{"name":"python","description":"Execute Python source code and return its output.","parameters":{"type":"object","properties":{"code":{"type":"string"}},"required":["code"]}}}]}
```

### ShareGPT

```json
{"conversations":[{"from":"human","value":"Hello"},{"from":"gpt","value":"Hi!"}]}
```

### Alpaca

```json
{"instruction":"Translate to English","input":"你好世界","output":"Hello world"}
```

### Pretrain

```json
{"text":"Transformer 通过自注意力机制建模上下文关系。"}
```

### DPO

```json
{"chosen":[{"role":"user","content":"Q"},{"role":"assistant","content":"good answer"}],"rejected":[{"role":"user","content":"Q"},{"role":"assistant","content":"bad answer"}]}
```

## 开发路线图

- [x] Recipe 执行引擎、执行记录与 Executions UI
- [x] 模拟 API 测试子节点（Query / Simulation / Cleaner / SFT Export）与 OAI 轨迹导出
- [x] Recipe / 设置持久化与 schema 版本
- [x] OpenAI Chat / Responses、Anthropic Messages 客户端与节点级重试
- [x] 真实 tool-calling provider（OpenAI Chat/Responses、Anthropic）与 python / PowerShell / bash 沙箱执行
- [x] 新节点专属属性面板（Query Source / Simulation / Cleaner / SFT Export）
- [x] 并发 query、tokens/延迟/成本统计、失败样本独立输出
- [ ] 容器级沙箱隔离与非 Windows 平台资源限制
- [ ] Recipe 新建 / 打开 / 保存 / 另存为（当前只有 Seed 数据源文件选择）
- [ ] JSONL / CSV / Parquet 流式读取与分页预览
- [ ] 大文件随机行跳转
- [ ] 数据增删查改
- [ ] 去重、混洗、切分文件
- [ ] 正则搜索与替换
- [ ] OpenAI / ShareGPT / Alpaca 完整批量互转
- [ ] DPO / Pretrain 格式支持
- [ ] 语法错误弹窗与一键修复交互
- [ ] Tool Schema 可视化增删改
- [x] Canvas 节点拖拽、Port 连线、多选/框选、平移与基础缩放
- [ ] 网格背景/吸附、鼠标中心缩放、Fit、自动布局
- [ ] Edge 动画与样本预览弹窗
- [ ] Mini-map 视口矩形与点击导航
- [x] Command Pattern 撤销 / 重做
- [ ] 亮色 / 暗色主题切换
- [ ] LiveCharts2 或 OxyPlot 进度图表
- [ ] 执行完成后的 output 目录摘要预览
- [x] 单元测试与集成测试（248 个：246 自动化通过 + 2 个需环境变量的真实 provider 冒烟）
- [ ] Round 8：Recipe 新建/打开/保存、脏标记、最近文件、按类型端口契约
- [ ] Round 9：Expression/Validator/Sampler/Tool Profile 节点能力补齐
- [ ] Round 10：数据预览、CSV/Parquet、格式转换、DPO/Pretrain
- [ ] Round 11：校验修复、去重/混洗/切分、搜索替换、统计
- [ ] Round 12：画布体验、并行/暂停/重试、CI/打包/主题/日志

## 编码约定

- 使用 MVVM 分层，避免在 View 中写业务逻辑。
- 所有 IO / 解析 / 写入操作使用 `Task`、`async` / `await` 和 `IProgress<T>`。
- 禁止在 UI 线程执行长耗时操作。
- 大文件处理必须采用流式读取，不允许一次性加载完整文件。
- 服务类放在 `Services/`，数据模型放在 `Models/`，视图模型放在 `ViewModels/`。
- 命名遵循 C# 标准 PascalCase / camelCase 规范。

## License

当前项目尚未声明许可证。发布前请根据实际用途添加 LICENSE 文件。
