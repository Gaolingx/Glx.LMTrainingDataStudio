# LLM Training Data Studio

LLM Training Data Studio 是一个基于 **C# / Avalonia** 的桌面端训练数据处理工作台，面向 LLM 预训练、SFT、DPO 和工具调用数据集的可视化编辑、验证、转换与流水线编排。

项目目标是为 JSONL / CSV / Parquet 等常见训练数据格式提供专业级 GUI：支持大文件流式处理、数据格式互转、DAG Recipe 编排、可视化 Tool Schema 编辑、异步执行、实时进度与结果预览。

## 当前状态

当前版本已完成应用骨架、核心服务与可执行 Recipe 引擎：

- Avalonia 桌面应用与三栏式 Studio UI（Block Sheet / Canvas / Property Panel / Status Bar）
- Recipe / 设置持久化：原子写入、camelCase 多态 JSON、schema 版本校验
- Recipe 文件生命周期：新建（空白/示例）、打开、保存、另存为、最近文件、脏标记、关闭确认与重启恢复
- 按 BlockType 的端口契约与连接守卫：方向/类型/单输入/重复/环校验；校验问题列表与画布错误/警告徽标
- Expression：Fluid 模板引擎（条件/循环/过滤器）、输出 JSON 校验、专属面板与样本预览
- Validator：表达式 / JSON Schema / 正则 / Fluid 脚本多规则、超时、pass/reject 双端口、reject JSONL 与失败率日志
- Sampler：确定性多文件切分、按 splits 生成多输出端口、分层采样与统计预览
- Tool Profile：参数树编辑、JSON 导入导出，并被 Simulation/LLM 引用复用
- 统一数据集 IO：JSONL / CSV / Parquet 分页读取与原子写出；Seed 支持三种格式
- 数据工作台（Data 视图）：分页预览、行号跳转、页面大小切换、逐行 CRUD（插入/复制/删除/恢复）、字段级单元格编辑与整行 JSON 编辑、撤销/重做、编辑另存或覆盖源文件
- 搜索命中行内高亮；校验问题按级别/错误码筛选，并支持按级别/错误码分组树
- 统一样本预览弹窗：SFT Export 输出、Validator 拒绝行、Simulation 失败样本、Cleaner 前后 diff，可一键在数据工作台打开
- 数据质量与治理：校验问题列表（级别/行号/JSON 路径/错误码）与跳行、JSONL 自动修复预览与写副本、搜索/正则替换、去重、固定 seed 混洗、比例/份数切分、数据集统计
- 画布：网格/吸附（Alt 关闭）、鼠标中心缩放、Fit、拓扑自动布局、Mini-map 视口与点击导航、双击添加、复制携带连线、内联重命名、真实端口坐标、边 hover
- 执行：层内受控并行、暂停/恢复、节点重试、运行输出目录与 `run-summary.json`
- 执行韧性：节点间溢写磁盘（阈值与临时目录可配置）、节点级检查点与中断恢复、取消 1s 内停止与临时目录清理
- SFT Export：文件选择器、运行前覆盖确认、可选字段白名单、写出行数与首行预览日志
- 工程化：滚动日志与全局异常、Dark/Light 主题变体、GitHub Actions CI、win-x64 单文件发布、Headless UI 测试、MIT LICENSE、应用图标
- 批量格式转换：OpenAI / ShareGPT / Alpaca / DPO / Pretrain，进度、取消、失败行 `.errors.jsonl`
- JSONL SQLite 行索引缓存与分页读取；1M 行 1KB/行基线通过（索引 8.8s、分页 10.9ms、内存增量 14MB）
- 1M 行质量操作基线通过（去重+混洗+切分 124.2s、统计 63.6s、托管内存增量 2MB）
- OpenAI / ShareGPT / Alpaca 数据格式转换服务
- 真实 RecipeRunner：Seed / Expression / Validator / Sampler / LLM 执行器，执行记录与 Executions UI
- OpenAI Chat Completions / Responses、Anthropic Messages 客户端；密钥从设置或环境变量读取，支持节点级超时与指数退避重试
- LLM 请求侧 tools（Tool Profile）、多轮行内 messages、`response_format` JSON Schema、并发与输出保序、token/成本日志、失败样本 JSONL
- Simulation Tools / Harness Rules / Script 回合可视化编辑器；OpenAI Chat SSE 流式 delta、requests-per-minute 限速与价格预设
- Query Source 支持 Fluid 模板变量（含 `index`）与外部 JSONL/CSV/Parquet query 源、查询/系统字段映射
- 模拟 API 测试：Query Source → Simulation（mini harness）→ Cleaner → SFT Export，多轮 tool 轨迹与 OpenAI messages JSONL 导出
- 真实 provider tool-calling：OpenAI Chat function calling、OpenAI Responses `function_call`、Anthropic Messages `tool_use`，含 token usage / 延迟 / 成本统计；已通过真实 OpenAI 兼容端点冒烟（tool_calls + usage + 两轮 harness 往返）
- 真实沙箱工具：python / PowerShell / bash / web_fetch；环境白名单、临时目录、超时杀进程树、输出上限，Windows 额外限制内存与进程数
- Simulation 支持并发 query（输出保序）与失败轨迹独立 JSONL 输出
- Query Source / Simulation / Cleaner / SFT Export / Expression / Validator / Sampler / Tool Profile 专属属性面板
- 内置 6 个基础工作流预设，可从 File → New from Preset 或节点库一键加载
- 自定义预设：保存当前 Recipe 为预设、导入/删除预设文件；预设保存在本机 `%APPDATA%\LMTrainingDataStudio\presets`
- 中英文界面本地化（zh-CN / en-US，可在设置中切换），节点库、属性面板与画布悬停提示包含完整节点说明
- 通用 Block Config JSON 编辑器，可直接配置所有节点类型

> 注意：当前仍是开发版。**10M 实测、节点尺寸手柄/边过渡动画、MSIX 安装包、README 截图、完整浅色主题、LLM tool_calls 回填、Anthropic 结构化输出、Sampler count 模式分层、MCP 适配尚未实现**；应用默认启动空白 Recipe，可通过 File → New from Sample 加载 4 节点示例，或恢复上次打开的文件。
>
> - 完整功能清单与节点能力矩阵：[PLAN/STATUS.md](PLAN/STATUS.md)
> - 分阶段实施计划、阶段目标与验收标准：[PLAN/TODO.md](PLAN/TODO.md)

## 工作流预设与节点说明

### 内置工作流预设

预设会创建一份全新的 Recipe 并标记为未保存；如果当前文档有改动，会先询问是否保存。每个预设都可以直接作为起点，随后在画布上修改节点配置。

| 预设 | 流水线 | 适用场景 |
| --- | --- | --- |
| LLM 批量生成 | Seed → LLM → SFT Export | 读取 JSONL/CSV/Parquet，逐行调用模型并导出 assistant 消息 |
| OAI 轨迹清洗 | Seed → Cleaner → SFT Export | 清理已有的 OpenAI messages 数据：空白、ANSI、reasoning、空消息 |
| 字段映射转换 | Seed → Expression → SFT Export | 用 Fluid 模板把原始字段映射为 `messages` 结构 |
| 校验与切分 | Seed → Validator → Sampler → SFT Export | 过滤无效行、输出 reject JSONL，并按比例切分 train/valid/test |
| 模拟 Agent 轨迹 | Query Source → Simulation → Cleaner → SFT Export | 离线、可复现的多轮工具调用轨迹 |
| 真实工具调用 | Query Source → Simulation（真实模型 + 沙箱）→ Cleaner → SFT Export | 生成真实 provider + python 沙箱轨迹；需要 API Key |

访问方式：

- 菜单 **File → New from Preset**。
- 左侧节点库顶部的 **Workflow Presets** 列表，点击即可加载。

### 自定义预设

- **保存**：**File → Save as Preset…**，填写名称与可选描述；当前 Recipe 会保存为一份独立快照。
- **导入**：**File → Import Preset…**，选择导出的预设 JSON；导入会生成新的本地 ID，不会覆盖已有预设。
- **删除**：自定义预设卡片右上角的 **×**，确认后仅从本机预设库移除，不影响当前画布。
- 预设文件为单个 JSON（包含名称、描述与完整 Recipe），存放在 `%APPDATA%\LMTrainingDataStudio\presets`，可直接复制给他人后通过 Import 导入。

### 节点说明

节点库卡片显示一句话简介；选中画布节点后，属性面板顶部会显示 **Node Guide**，其中包含该节点的用途、关键行为、输入/输出端口与使用建议。鼠标悬停在画布节点上也会显示同样的简介。

| 节点 | 输入 → 输出 | 说明 |
| --- | --- | --- |
| Seed Source | 无 → rows | 读取 JSONL / CSV / Parquet，作为流水线入口 |
| LLM Model Call | input → output | 逐行调用模型，Fluid 提示词，输出 `messages` |
| Expression Transform | input → output | Fluid 模板或字段映射，重塑每行 JSON |
| Validator | input → output | 表达式 / JSON Schema 校验，可输出 reject JSONL |
| Sampler | input → output | 确定性采样或写出多份切分文件 |
| Tool Profile | 无数据端口 | 可复用的 OpenAI 兼容工具 Schema 配置 |
| Query Source | 无 → queries | 生成模拟用户 query，驱动轨迹生成 |
| Simulation | input → output | mini harness，多轮模型 / 工具循环 |
| Cleaner | input → output | 清洗 OpenAI messages 的格式与噪声 |
| SFT Export | input → 无 | 原子写出最终 SFT JSONL |

### 界面语言

设置面板中的 **Language / 界面语言** 支持：

- **跟随系统 / System**：Windows 中文环境使用 zh-CN，其余使用 en-US。
- **简体中文**。
- **English**。

切换后菜单、按钮、属性面板、节点库、预设名称、文件对话框等静态 UI 会即时刷新；运行日志、执行状态与数据集统计摘要仍保留英文技术文本，便于检索和跨语言排障。

## 技术栈

- **语言**：C#
- **框架**：Avalonia UI
- **运行时**：.NET 9（项目当前 `TargetFramework` 为 `net9.0`）
- **数据缓存**：SQLite / `Microsoft.Data.Sqlite`
- **CSV 支持**：`CsvHelper`
- **Parquet 支持**：`Parquet.Net`
- **模板引擎**：`Fluid.Core`（Liquid 语法）
- **JSON Schema**：`JsonSchema.Net`
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
- `SftExportBlockExecutor`：流式写临时文件，成功后原子替换目标 JSONL；按行透传并做 JSON 规范化（不主动裁剪字段，上游 Simulation 输出 `messages` 与 `tools`）。

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
| Seed | JSONL / CSV / Parquet 可用 | 有 | Streaming 开关、节点内过滤/列映射 |
| LLM | 三种 provider + tools/多轮/结构化输出/并发/成本 | 有 | tool_calls 回填、Anthropic structured output |
| Expression | Fluid 模板 + JSON 校验 | 有 | 沙箱/超时、外部函数 |
| Validator | 多规则（表达式/Schema/正则/脚本）+ 超时 + pass/reject | 有 | 独立失败摘要 UI、任意代码沙箱 |
| Sampler | 数量/比例 + 多文件 splits + 多端口 + 分层 + 统计预览 | 有 | count 模式分层、再混洗 |
| Tool Profile | 参数树 + JSON 导入导出 + 复用 | 有 | MCP 适配 |
| Query Source | 单组/多组 + 模板变量 + 外部 JSONL/CSV/Parquet | 有 | 随机/模型生成 query、去重/权重 |
| Simulation | 脚本/真实 provider + 模拟/真实工具 + 可视化 Tools/Rules/Script | 有 | 非 OpenAI 流式、严格限速、容器隔离 |
| Cleaner | 正则/格式/ANSI/reasoning/空消息 | 部分 | 正则规则 UI、预览、坏行报告 |
| SFT Export | 流式 + 原子替换 + 行透传 | 有 | 文件选择器、字段裁剪、预览、拆分、DPO/Pretrain |

完整矩阵与逐项缺口见 [PLAN/STATUS.md](PLAN/STATUS.md)。

### 1. 大文件流式处理

- 面向数百 GB JSONL 文件设计，避免整体加载导致 OOM。
- 在 `tmp/index-cache` 中生成轻量 SQLite Index Cache。
- 缓存内容保存行号与字节偏移，支持快速跳转指定行。
- 支持配置最大缓存容量，默认目标为 512 MB。
- 支持手动清理缓存。
- CSV 使用 RFC 4180 记录边界索引，Parquet 按 row group 随机读取；两者都只加载当前页。
- 1M 行、平均 1KB/行实测：索引 8.8s，任意 100 行分页 ≤10.9ms，转换 66.9s，内存增量 14MB。
- 1M 行、256B/行质量操作实测：去重+混洗+切分 124.2s（50 万保留/50 万重复），统计 63.6s，托管内存增量 2MB；10M 可通过 `LMTDS_PERF_ROWS=10000000` 运行。
- 1M 行编辑+保存实测：1000 替换 / 100 删除 / 100 插入，流式重写 55.6s，托管内存增量 11MB；未修改 JSONL 行文本原样保留。
- 逐行编辑按页进行，编辑计划跨页保留；保存时按块读取源文件并流式写入临时文件，最后原子替换目标，取消/失败不破坏原文件。

相关实现：

- `LMTrainingDataStudio2/Services/IndexCacheService.cs`
- `LMTrainingDataStudio2/Services/Datasets/`

### 2. 多格式支持与互转

支持以下主流 LLM 数据格式：

- OpenAI-compatible：`messages[]`
- ShareGPT / LLaMA-Factory：`conversations[]`
- Alpaca：`instruction` / `input` / `output`
- DPO：`prompt` / `chosen` / `rejected`
- Pretrain：`text`
- Auto：自动检测输入格式

数据工作台支持 OpenAI ↔ ShareGPT ↔ Alpaca、chat → Pretrain、DPO → DPO、Pretrain → Pretrain 的批量转换；不支持的组合会明确报错，失败行写入 `<target>.errors.jsonl`。

相关实现：

- `LMTrainingDataStudio2/Models/DatasetFormat.cs`
- `LMTrainingDataStudio2/Services/DatasetFormatConverter.cs`
- `LMTrainingDataStudio2/Services/Datasets/DatasetConversionService.cs`
- `LMTrainingDataStudio2/Services/Datasets/DatasetBatchConverter.cs`

### 3. 数据验证与治理

- JSONL 字节级扫描：严格 UTF-8、BOM、超长行（默认 2MB，可配置）与错误上限（默认 1000；达到上限后继续计数）。
- 问题列表包含级别、行号、JSON 路径与错误码；支持双击跳转到对应行。
- 覆盖语法、非对象、空行、聊天消息结构、未知格式；CSV/Parquet 复用统一读取器做行级 JSON/结构校验。
- 自动修复：预览可修复/需人工行，写入独立修复副本；原文件字节不变，非法 UTF-8 与超长行原样保留。
- 搜索 / 正则替换：当前页或全文件、大小写、分组替换、匹配预览、取消与原子写目标文件；表格中命中文本行内高亮。
- 逐行编辑：插入（上方/下方）、复制、删除/恢复、字段级单元格编辑（可增删顶层字段）与整行 JSON 编辑；待保存编辑跨页保留，支持撤销/重做与保存前差异摘要；可另存或确认后覆盖源文件。
- 校验问题可按级别（错误/警告）与错误码筛选，并支持按级别/错误码分组的树形视图与组内计数。
- 统一样本预览：任意 JSONL/CSV/Parquet 文件前 N 行，以及 Cleaner 规则的清洗前后 diff；预览窗口可跳转到数据工作台。
- 去重（SHA256 + 临时 SQLite）、固定 seed 混洗（SQLite 外排）、`name:ratio` / `name:count` 多文件切分；行数守恒、同 seed 可复现。
- 数据集统计：角色分布、长度分桶、空样本、空内容消息、重复率、工具调用/工具消息、格式分布、坏行数。
- 批量转换按行隔离错误，坏行带行号/原因/原始数据落盘，不中断整批。
- Recipe 执行前验证节点、连接和基础配置。

相关实现：

- `LMTrainingDataStudio2/Services/DatasetValidationService.cs`
- `LMTrainingDataStudio2/Services/Datasets/DatasetSearchReplaceService.cs`
- `LMTrainingDataStudio2/Services/Datasets/DatasetTransformService.cs`
- `LMTrainingDataStudio2/Services/Datasets/DatasetStatisticsService.cs`
- `LMTrainingDataStudio2/Services/RecipeValidationService.cs`

### 4. DAG Recipe 编辑器

UI 采用 Recipe Graph / DAG 的设计：

- Seed Block：数据输入源
- LLM / Model Block：模型调用
- Expression Block：Fluid（Liquid）风格字段变换
- Validator Block：表达式 / JSON Schema 校验与过滤（代码规则、超时待后续）
- Sampler Block：确定性采样与数据切分
- Tool Profile Block：OpenAI-compatible Tool Schema 配置（MCP 适配待后续）
- Query Source Block：模拟用户 query（同 query 重复 / 多组 query）
- Simulation Block：模拟 API + mini harness（多轮工具调用、白名单、失败、超时、重试）
- Cleaner Block：正则替换与格式规范化
- SFT Export Block：OAI messages JSONL 原子导出

当前 UI 已包含：

- File 菜单：New / New from Sample / Open / Recent / Save / Save As，以及 Ctrl+N/O/S、Ctrl+Shift+S
- 左侧 Block Sheet
- 中央 Canvas 区域（节点、贝塞尔连线、错误/警告徽标）
- 画布交互：网格背景与吸附（Alt 临时关闭）、鼠标中心缩放、Fit、拓扑自动布局、Mini-map 视口/点击导航、双击空白添加、双击节点内联重命名、复制携带内部连线、边 hover
- 执行控制：Pause / Resume、节点重试设置、运行输出目录（`run-summary.json`）、节点间溢写阈值与运行临时目录
- 执行恢复：Executions 视图对失败/取消的运行提供“恢复执行”，复用已完成节点输出；成功运行后自动清理检查点
- SFT Export 面板：输出文件选择器、字段白名单（逗号分隔顶层字段）、运行前覆盖确认
- 右侧属性编辑面板（含校验问题列表，可点击定位节点）
- 悬浮 Run / Validate 控件
- Mini-map 渲染
- 底部 Status Bar（文件路径、脏标记、进度、内存与缩放）
- Data 视图：数据集分页预览、首/上/下/末页、按行号跳转、页面大小切换；右侧 Convert / Search / Transform / Stats / Validate 五个工具页签
- Data 视图：格式转换与失败行列表；搜索/正则替换预览与写目标文件；去重/混洗/切分；角色/长度/重复率/工具调用统计；校验问题列表与自动修复
- Data 视图：逐行编辑（插入/复制/删除/恢复、字段级单元格编辑与整行 JSON、撤销/重做、另存/覆盖确认）、搜索命中高亮、校验问题级别/错误码筛选与分组树
- 样本预览弹窗：SFT Export、Validator reject、Simulation 失败样本与 Cleaner diff 共用同一窗口

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
│   ├── AppSettings.cs                      # 全局设置（缓存容量、主题、网格吸附、最近文件等）
│   ├── BlockConfig.cs                      # 强类型节点配置（多态 kind）
│   ├── BlockPortSchema.cs                  # 按 BlockType 的固定端口契约
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
│   ├── Executors/                          # 节点执行器（Fluid 模板、JSON Schema、Sampler splits、Tool Profile 等）
│   ├── Sandbox/                            # 真实沙箱：进程限制器、python/powershell/bash/web_fetch 运行时
│   ├── Simulation/                         # 模拟 API：scripted/provider client、tool runtime、harness、清洗、OAI 序列化
│   ├── ConnectionRules.cs                  # 入图前连接守卫（方向/类型/单输入/重复/环）
│   ├── DatasetFormatConverter.cs           # OpenAI / ShareGPT / Alpaca 格式互转
│   ├── DatasetValidationService.cs         # JSONL 逐行语法验证 + 自动修复
│   ├── ExecutionStore.cs                   # 执行记录持久化
│   ├── IndexCacheService.cs                # SQLite 行偏移索引缓存（大文件随机访问）
│   ├── JsonlDatasetReader.cs               # JSONL 分页读取
│   ├── RecipeRunner.cs                     # DAG 执行器
│   ├── RecipeStore.cs                      # Recipe 原子持久化
│   ├── RecipeValidationService.cs          # Recipe DAG 验证（环检测、端口、Schema、引用检查）
│   ├── ToolProfileResolver.cs              # Tool Profile 名称解析（Simulation/LLM 复用）
│   └── SettingsStore.cs                    # 设置持久化
│
├── ViewModels/                             # MVVM ViewModel 层
│   ├── MainWindowViewModel.cs              # 主窗口 VM（文档生命周期、Run/Validate、最近文件）
│   ├── ValidationIssueViewModel.cs         # 校验问题列表项（可定位节点）
│   ├── BlockNodeViewModel.cs               # 画布节点 VM + PortViewModel
│   ├── BlockTemplateViewModel.cs           # Block Sheet 模板项 VM
│   ├── QuerySourceConfigEditorViewModel.cs # Query Source 专属编辑器
│   ├── SimulationConfigEditorViewModel.cs  # Simulation 专属编辑器（含沙箱参数）
│   ├── ExpressionConfigEditorViewModel.cs  # Expression 模板 / 预览编辑器
│   ├── ValidatorConfigEditorViewModel.cs   # Validator 规则 / reject 编辑器
│   ├── SamplerConfigEditorViewModel.cs     # Sampler 采样 / splits 编辑器
│   ├── ToolProfileEditorViewModel.cs       # Tool Profile 工具列表 / 导入导出
│   ├── ToolEditorViewModel.cs              # 单个工具与参数树同步
│   ├── ToolParameterNodeViewModel.cs       # 参数树节点
│   ├── CleanerConfigEditorViewModel.cs     # Cleaner 专属编辑器
│   ├── SftExportConfigEditorViewModel.cs   # SFT Export 专属编辑器
│   └── EdgeViewModel.cs                    # 连线 VM
│
├── Views/                                  # Avalonia 视图层
│   ├── MainWindow.axaml / .axaml.cs        # 三栏式主窗口布局、File 菜单、快捷键与关闭确认
│   ├── MessageDialog.axaml / .axaml.cs     # 保存确认与错误提示对话框
│   ├── RecipeCanvasView.axaml / .axaml.cs  # DAG 画布（节点渲染、贝塞尔曲线连线、错误徽标）
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
└── Assets/                                 # 应用图标等静态资源
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
dotnet test .\LMTrainingDataStudio2.slnx --filter HeadlessUiTests
```

### 1M 行编辑保存基线（gated）

```powershell
$env:LMTDS_PERF_TESTS = "1"
dotnet test .\LMTrainingDataStudio2.slnx --filter EditSave_LargeInput
```

### 发布（win-x64 单文件）

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-win-x64.ps1
```

输出在 `artifacts/publish/win-x64`，并生成同名 zip。CI 配置见 `.github/workflows/ci.yml`。

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

包含 File 菜单（新建/打开/最近文件/保存/另存为）、可编辑的 Recipe 名称、脏标记、Editor / Executions 切换与全局设置入口。关闭或切换文档时若有未保存修改，会弹出保存确认。

### Block Sheet

展示可添加的 Block 模板，包括 Seed、LLM、Expression、Validator、Sampler、Tool Profile、Query Source、Simulation、Cleaner 和 SFT Export。

### Canvas Area

用于展示 Recipe DAG。当前已支持：

- Block Sheet 拖放添加、可搜索添加菜单（`~` / 右键）、双击空白添加
- 节点拖动、多选拖动、框选、端口连线与边删除、边 hover
- 中键平移、滚轮缩放、鼠标中心缩放、缩放百分比、Fit to View
- 网格背景与网格吸附（Alt 临时关闭）、拓扑自动布局
- Mini-map 渲染、视口矩形与点击导航
- Undo / Redo（添加/删除/移动/连线/粘贴块/重命名）、`Ctrl+Z` / `Ctrl+Y`
- 复制携带内部连线、双击节点内联重命名、右键菜单
- 可拖动分栏、真实端口坐标

尚未实现：

- 节点尺寸调整手柄、边过渡动画、标签避让
- Undo/Redo 菜单与按钮、`Ctrl+Shift+Z`、属性修改进入撤销历史
- 系统剪贴板 / 跨实例粘贴
- 完整浅色主题（部分视图仍硬编码深色）

### Property Panel

根据当前选中的 Block 显示配置项，包括：

- Block 引用名
- 节点专属配置（Seed / LLM / Expression / Validator / Sampler / Tool Profile / Query Source / Simulation / Cleaner / SFT Export）
- Cleaner 正则规则、Simulation 的 Tools/Rules/Script 目前仍通过 Config JSON 编辑
- 通用 Config JSON 编辑器（适用于所有节点，包含 kind 与 BlockType 一致性校验）
- Validation 摘要与问题列表（可定位节点）
- Progress / Logs

### Status Bar

显示当前状态、行数、文件路径、进度条、内存占用和缩放百分比。

> `CurrentFilePath`、`TotalLines` 和缩放百分比已接入真实数据；内存占用仍只在启动时采样一次，定时刷新列在 Round 16。

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
- [x] 专属属性面板（Query / Simulation / Cleaner / SFT / Expression / Validator / Sampler / Tool Profile）
- [x] 并发 query、tokens/延迟/成本统计、失败样本独立输出
- [ ] 容器级沙箱隔离与非 Windows 平台资源限制
- [x] Recipe 新建 / 打开 / 保存 / 另存为、最近文件与脏标记
- [x] JSONL / CSV / Parquet 流式读取与分页预览
- [x] 大文件随机行跳转
- [x] 数据增删查改（逐行插入/复制/删除/恢复、行 JSON 编辑、撤销/重做、另存/覆盖）
- [ ] 10M 行与 CSV/Parquet 大规模基线
- [x] 搜索命中高亮、校验问题级别/错误码筛选
- [x] 统一样本/输出预览弹窗（SFT / Validator reject / Simulation 失败样本 / Cleaner diff）
- [x] 去重、混洗、切分文件
- [x] 正则搜索与替换
- [x] OpenAI / ShareGPT / Alpaca 完整批量互转
- [x] DPO / Pretrain 格式支持
- [x] 校验问题列表与自动修复预览/写副本
- [x] Tool Schema 可视化增删改与 JSON 导入导出
- [x] Canvas 节点拖拽、Port 连线、多选/框选、平移与基础缩放
- [x] 网格背景/吸附、鼠标中心缩放、Fit、自动布局
- [x] Mini-map 视口矩形与点击导航
- [x] 双击添加、复制携带内部连线、内联重命名、真实端口坐标、边 hover
- [ ] Node 尺寸手柄、Edge 过渡动画与标签避让
- [x] Command Pattern 撤销 / 重做
- [部分] 亮色 / 暗色主题切换（主面板/画布已主题化，部分视图仍硬编码深色）
- [ ] LiveCharts2 或 OxyPlot 进度图表
- [x] 执行层内并行、暂停/恢复、节点重试、output 目录摘要
- [x] 节点间溢写/流式读取、节点级检查点与中断恢复、取消/清理定量验收
- [x] SFT Export 文件选择器、覆盖确认、字段白名单、行数与预览日志
- [x] 1M 行 Seed → Expression → SFT Export 基线（17.2s，托管内存增量 1MB）
- [x] 滚动日志、全局异常、CI、win-x64 单文件发布、Headless UI 测试、MIT LICENSE、应用图标
- [x] 单元测试与集成测试（501 个：495 自动化通过 + 6 个 gated：2 个真实 provider 冒烟 + 4 个性能）
- [x] Round 8：Recipe 新建/打开/保存、脏标记、最近文件、按类型端口契约
- [x] Round 9：Expression/Validator/Sampler/Tool Profile 节点能力补齐
- [x] Round 10：数据预览、CSV/Parquet、格式转换、DPO/Pretrain
- [x] Round 11：校验修复、去重/混洗/切分、搜索替换、统计
- [x] Round 12：画布体验、并行/暂停/重试、CI/打包/主题/日志
- [x] Round 13：执行与 IO 韧性（节点间溢写/流式、中断恢复、取消/清理硬化、SFT Export 收尾）
- [x] Round 14：数据工作台逐行编辑（CRUD/字段级单元格/撤销/另存/覆盖确认）、搜索高亮、校验筛选与分组树、统一样本预览
- [x] Round 15：节点能力收紧（多输出端口、LLM tools/结构化输出/并发/失败样本、Validator 多规则/超时/双端口、Sampler 分层与多端口、Simulation 可视化编辑器/流式/限速/价格、Query Source 模板与外部源）
- [ ] Round 16：发布与体验（画布 polish、完整浅色主题、MSIX/文件关联、10M 基线、README 截图）

## 编码约定

- 使用 MVVM 分层，避免在 View 中写业务逻辑。
- 所有 IO / 解析 / 写入操作使用 `Task`、`async` / `await` 和 `IProgress<T>`。
- 禁止在 UI 线程执行长耗时操作。
- 大文件处理必须采用流式读取，不允许一次性加载完整文件。
- 服务类放在 `Services/`，数据模型放在 `Models/`，视图模型放在 `ViewModels/`。
- 命名遵循 C# 标准 PascalCase / camelCase 规范。

## License

MIT License. See [LICENSE](LICENSE).
