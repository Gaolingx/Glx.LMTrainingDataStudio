# STATUS — 功能清单、节点能力矩阵与下一阶段规划

> 最后核对：2026-11-17（Round 16 完成）
> 验证基线：`dotnet build` 0 警告 0 错误（分析器开启）；`dotnet test` 556 通过 + 7 个 gated 测试（2 个真实 provider 冒烟 + 5 个性能 gated，默认跳过），另有 1 个 `[Explicit]` 示例生成；`--list-tests` 563 个用例；`dotnet format --verify-no-changes` 通过；覆盖率总行 74.64%、Services/Commands 84.32%；Headless UI 测试通过；1M 行 JSONL、1M 行质量操作与 1M 行编辑保存基线通过；10M 行链路与 CSV/Parquet 大规模基线入口已提供；win-x64 单文件发布与 Debug 启动冒烟通过。
>
> 图例：`[x]` 已完成并有测试/验证；`[部分]` 可用但有明确缺口；`[ ]` 未实现。
>
> 说明：本文件是“已做/未做”的主清单；与 README 冲突时以本文件为准。本轮文档同步修正了几处旧描述：LLM Prompt 已使用 Fluid 模板；SFT Export 节点按行透传，不主动裁剪字段；README 画布“未实现”段落已过期。

---

## 一、总体结论

### 1.1 已形成的可用闭环

- Recipe 内存模型、强类型节点配置、多态 JSON 序列化、深拷贝、Recipe/Settings 持久化服务。
- Recipe 文件生命周期闭环：新建、从示例新建、打开、保存、另存为、最近文件、脏标记、关闭确认、重启恢复上次文件。
- 按 BlockType 生成端口 schema；连接前校验方向/类型/单输入/重复/环；校验问题列表与画布错误/警告徽标。
- Expression 使用 Fluid 模板引擎（条件/循环/过滤器），带专属面板、语法校验与样本预览；输出 JSON 强校验。
- Validator 支持表达式与 JSON Schema 规则、reject 文件、失败率日志；Sampler 支持确定性多文件切分。
- Tool Profile 支持参数树编辑与 JSON 导入导出，并被 Simulation/LLM 复用。
- 统一数据集 IO：JSONL / CSV / Parquet 随机分页读取与原子写出；Seed 节点支持三种格式。
- 数据工作台（Data 视图）：分页预览、行号跳转、页面大小切换，JSONL 走 SQLite 索引，CSV 走记录边界索引，Parquet 按 row group 读取。
- 数据逐行编辑：插入/复制/删除/恢复、字段级单元格编辑与整行 JSON 编辑、跨页待保存编辑、撤销/重做、保存前差异摘要、另存或确认后覆盖源文件；流式重写与原子替换。
- 搜索命中行内高亮（字面量/正则）；校验问题按级别与错误码筛选，并支持按级别/错误码的分组树。
- 统一样本预览弹窗：SFT Export 输出、Validator 拒绝行、Simulation 失败样本、Cleaner 前后 diff；可跳转数据工作台。
- 批量格式转换：OpenAI / ShareGPT / Alpaca / DPO / Pretrain，支持进度、取消、失败行 `.errors.jsonl` 与原子目标写入。
- 数据治理：校验问题列表（级别/行号/JSON 路径/错误码）与跳行；JSONL 自动修复预览与“写修复副本”；搜索/正则替换（当前页/全文件/预览/取消）；去重、固定 seed 混洗、比例/份数切分；角色/长度/空样本/重复率/工具调用统计。
- 1M 行 JSONL 验收通过：索引 8.8s（≤10s）、100 行分页 10.9ms（≤100ms）、转换 66.9s、内存增量 14MB（≤300MB）。
- 1M 行质量操作验收通过：去重+混洗+切分 124.2s、统计 63.6s、托管内存增量 2MB（≤300MB）。
- JSONL 流式读取、SQLite 行索引缓存、分页读取。
- DAG 校验、拓扑分层、层内受控并行、顺序执行、fail-fast、下游 Skipped、取消、暂停/恢复、节点重试、进度、日志、执行记录持久化与 Executions UI。
- 每次运行的输出目录与 `run-summary.json`；Executions UI 显示输出目录。
- 节点间输出溢写磁盘：`SpillThresholdRows` 与运行临时目录可配置，下游流式读取，成功/失败/取消统一清理临时目录。
- 节点级检查点与中断恢复：失败/取消运行保留已完成节点输出，Executions “恢复执行”从首个未完成节点继续；成功运行后清理检查点。
- SFT Export：文件选择器、运行前覆盖确认、可选字段白名单、写出行数与首行预览日志。
- 1M 行 Seed → Expression → SFT Export 基线：17.2s，托管内存增量 1MB；取消后 1s 内停止的测试通过。
- 10M 行 Seed → Expression → SFT Export 基线：生成 5.0s、运行 154.0s、托管内存增量 1MB、输出 10,000,000 行。
- CSV/Parquet 大规模基线：20 万行 @256B，JSONL→CSV 14.3s、JSONL→Parquet 13.1s、最慢 100 行分页 18.2ms、托管内存增量 17MB。
- 10 种节点类型全部接入 Block Sheet、画布、默认配置与 Recipe JSON。
- Query Source → Simulation → Cleaner → SFT Export 端到端 SFT 轨迹生成可用（脚本/真实 provider、模拟/真实工具均可切换）。
- 真实 provider tool-calling（OpenAI Chat/Responses、Anthropic）与真实沙箱工具（python/PowerShell/bash/web_fetch）。
- 画布交互：网格/吸附（Alt 关闭）、鼠标中心缩放、Fit、拓扑自动布局、Mini-map 视口与点击导航、双击添加、复制携带连线、内联重命名、真实端口坐标、边 hover、增量拖拽、可拖动分栏。
- 工作流预设：6 个基础流水线可从 File → New from Preset 或节点库一键加载，加载遵守脏文档确认。
- 自定义预设：当前 Recipe 可保存为预设；支持导入外部预设（分配新本地 ID）与删除；单文件存于 `%APPDATA%\LMTrainingDataStudio\presets`，坏文件跳过并告警。
- 节点说明：10 类节点具备本地化名称、简介、详细说明与端口摘要；属性面板 Node Guide 与画布悬停提示复用同一份目录。
- 界面本地化：zh-CN / en-US 资源化静态 UI（约 260 个键），设置面板切换语言后即时刷新；运行日志/执行状态仍为英文技术文本。
- 多输出端口：Validator `pass`/`reject`、Sampler 按 splits 动态端口；Runner 按端口缓冲、检查点与恢复，旧单端口 Recipe 回落主端口。
- LLM 请求侧 tools、多行 `messages`、`response_format` JSON Schema、并发与保序、token/成本日志、失败样本 JSONL。
- Validator 多规则组合、expression/schema/regex/script、规则超时、取反与 pass/reject 双端口。
- Sampler 多端口切分、分层字段、切分统计预览。
- Simulation Tools/Rules/Script 可视化编辑器、OpenAI Chat 流式 delta、RequestsPerMinute 限速与价格预设。
- Query Source Fluid 模板变量与外部 JSONL/CSV/Parquet query 源。
- 画布 polish：节点尺寸手柄（持久化 + 撤销）、边 hover 流动动画、边标签避让；Undo/Redo 菜单与 `Ctrl+Shift+Z`；属性名称/配置修改进入撤销历史并自动合并。
- 完整 Dark/Light 主题：所有 Views 使用主题资源，无硬编码颜色字面量；主题切换即时刷新画布与窗口背景。
- 状态栏内存占用定时刷新（2s）。
- 单实例与命令行打开（命名互斥体 + 命名管道转发）；`.recipe` 文件关联清单与安装脚本。
- 诊断与崩溃报告：Help → Export Diagnostics（脱敏设置 + 日志 + 最近运行摘要）；未处理异常自动写崩溃 zip。
- 发布：MSIX 清单/打包脚本、Inno Setup 安装脚本；`examples/` 6 个示例 Recipe；README Dark/Light 截图。
- 工程质量：`.editorconfig`、.NET 分析器 `latest-recommended`、警告即错误；构建 0 警告。
- 工程化：滚动日志与全局异常、GitHub Actions CI、win-x64 单文件发布脚本、Headless UI 测试、MIT LICENSE、应用图标。
- 563 个自动化测试：556 个常规通过 + 7 个 gated（2 个真实 provider 冒烟 + 5 个性能 gated）+ 1 个 Explicit 示例生成。

### 1.2 当前最大缺口（按优先级）

1. **执行韧性残余**：已补输出 SHA256 与 Recipe 指纹校验；溢写阈值仍按行数计；检查点无磁盘配额、无启动清理。
2. **数据编辑残余**：已支持跨格式保存；字段编辑覆盖顶层字段，`messages` 等嵌套结构仍按 JSON 文本编辑；匹配项导航与预览分页未做。
3. **节点能力残余**：LLM 不执行 tool_calls 回填；Anthropic 无结构化输出约束；Validator script 为 Fluid 布尔表达式而非任意脚本；Sampler 分层仅对 ratio/splits 生效；Tool Profile 无 MCP。
4. **发布残余**：MSIX/Inno 安装包未在本机实际安装验证；单实例管道未做双进程自动化测试；10M 行结果需按本机实测记录。

### 1.3 一页总览

**已做（可交付）**

- Recipe 生命周期、端口契约、DAG 校验、顺序/并行执行、暂停/重试/取消、执行记录与输出摘要。
- 节点间溢写缓冲、节点级检查点与中断恢复、取消/临时文件清理的定量测试。
- 10 个节点的执行器、默认配置、Recipe 序列化与画布接线。
- Expression / Validator / Sampler / Tool Profile / Query Source / Simulation / Cleaner / SFT Export 专属属性面板。
- JSONL / CSV / Parquet 读取、索引、分页、转换、校验、修复、搜索替换、去重、混洗、切分、统计。
- Simulation 离线脚本与真实 provider、模拟/真实工具、并发、成本、失败样本。
- 画布核心交互、主题资源、日志、CI、单文件发布、Headless UI 测试。

**部分完成（可用但有缺口）**

- Seed：三种格式可读；Streaming UI 已暴露，仅 JSONL + 关闭索引缓存时生效；无节点内过滤/列映射。
- LLM：三种 provider、tools/多轮/结构化输出/并发/成本/失败样本可用；tool_calls 不执行回填，Anthropic 无结构化输出约束。
- Validator：表达式/JSON Schema/正则/Fluid 脚本、多规则、超时与 pass/reject 双端口可用；非任意代码脚本。
- Sampler：count/ratio/splits、多输出端口、分层（ratio/splits）与统计预览可用；count 模式不分层。
- Cleaner：5 个布尔规则 + 正则规则可用；正则规则 UI 与清洗前后 diff 预览已接入；无坏行报告。
- SFT Export：流式 + 原子替换 + 行透传 + 文件选择器 + 覆盖确认 + 字段白名单可用；已有输出预览弹窗；无拆分/DPO-Pretrain 直出。
- 执行：层内并行/暂停/重试、输出摘要、溢写/流式、检查点恢复可用；恢复已加 SHA256/Recipe 指纹校验，检查点无配额。
- 画布：核心交互、尺寸手柄、边动画、标签避让、Undo/Redo 菜单与属性撤销可用；逐帧性能未做 UI 级计时。
- 设置：完整 Dark/Light 主题、网格吸附与内存定时刷新生效；其他设置变更仍不通知已创建的服务。

**未实现（下一阶段工作池）**

- 数据编辑残余：消息/角色级表单编辑、匹配项导航、预览分页。
- LLM 工具调用回填、Anthropic 结构化输出、模型列表。
- Validator 失败样本独立 UI、任意代码沙箱规则。
- Sampler count 模式分层、切分内再混洗选项。
- Simulation 非 OpenAI 流式 delta、严格令牌桶限速、容器级隔离。
- Query Source 随机/模型生成 query、query 去重与权重。
- Tool Profile MCP 适配、共享工具库、LLM 请求侧复用。
- Seed 多文件/目录、过滤/列映射、编码选项、HuggingFace/HTTP 数据源。
- 10M 行实测与 CSV/Parquet 大规模基线按需运行（入口已提供）。
- 单实例、命令行打开、文件关联、MSIX/Inno 脚本与诊断导出已完成；安装包未做本机安装验证。
- API Key 加密存储、设置变更通知、运行日志/执行状态本地化、Tool Profile MCP。

---

## 二、节点能力矩阵（全部 10 个节点）

### 2.0 端口契约与职责总表

| 节点 | 输入端口 | 输出端口 | 责任 |
| --- | --- | --- | --- |
| Seed | 无 | `rows` | 从 JSONL/CSV/Parquet 读取数据 |
| LLM / Model | `input` | `output` | 逐行调用模型，输出 assistant 消息 |
| Expression | `input` | `output` | Fluid 模板/字段映射变换 |
| Validator | `input` | `pass` / `reject` | 多规则过滤，可写 reject 文件；拒绝行从 reject 端口输出 |
| Sampler | `input` | `output` 或 splits 端口 | 数量/比例采样，或按 splits 输出多端口并写多文件 |
| Tool Profile | 无 | 无 | 工具 schema 配置，被 Simulation/LLM 引用 |
| Query Source | 无 | `queries` | 生成模拟用户 query |
| Simulation | `input` | `output` | 多轮 tool-calling 轨迹生成 |
| Cleaner | `input` | `output` | 正则/格式/ANSI/reasoning/空消息清洗 + 正则规则 UI |
| SFT Export | `input` | 无 | 流式写 JSONL（可选字段白名单）并透传给下游（若有） |

> 所有数据端口当前统一为 `rows` 类型；端口数量、名称、类型暂不可在 UI 中增删改。

### 2.1 Seed（数据输入）

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| JSONL / CSV / Parquet 读取 | [x] | `DatasetIO` 统一读取；CSV 记录边界索引、Parquet row group |
| SQLite 索引缓存 | [x] | `UseIndexCache=true` 时走索引；可配置容量并清理 |
| JSONL 纯流式 | [x] | `UseIndexCache=false` 时逐行读取 |
| 行数进度 | [x] | `RowsProcessed` 上报 |
| 专属属性面板 | [x] | 路径选择、文件类型、Index Cache 开关 |
| 端口 schema | [x] | 仅输出 `rows`；连接前校验方向/类型 |
| 1M 行性能验收 | [x] | JSONL 1KB/行：索引 8.8s、分页 10.9ms、内存增量 14MB |
| `Streaming` 开关 | [x] | UI 已暴露；仅 JSONL + 关闭索引缓存时生效，CSV/Parquet 由读取器决定 |
| 数据预览 / 过滤 / 列映射 | [部分] | Data 视图可分页预览；节点内过滤/列映射未做 |
| 多文件 / 目录 / HuggingFace / HTTP 源 | [ ] | 仅单文件路径 |
| 编码/分隔符/类型推断选项 | [ ] | CSV 全部按字符串暴露 |
| 节点内 Limit/Offset/采样 | [ ] | 无 |

### 2.2 LLM / Model（单轮模型调用）

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| OpenAI Chat Completions | [x] | `LlmApiClient` |
| OpenAI Responses | [x] | `LlmApiClient` |
| Anthropic Messages | [x] | `LlmApiClient` |
| 模型、Endpoint、温度、Max tokens | [x] | 专属面板 |
| Prompt 模板 | [x] | 与 Expression 相同的 Fluid 引擎；空 Prompt 时发送整行 JSON |
| 节点级超时 / 指数退避重试 | [x] | `TimeoutSeconds` / `MaxRetries` |
| 密钥设置 | [x] | settings.json 本机保存，环境变量兜底 |
| 输出格式 | [x] | 每行输出 `messages[{role:assistant,content}]` |
| Tool Profile 引用 | [x] | 工具定义写入请求 tools，同时保留在输出行 |
| Prompt 预览 / 语法校验 | [部分] | 执行期校验模板；编辑期无预览 |
| 多轮上下文 / 请求侧 tools | [x] | `UseRowMessages` 读取行内 messages 并追加 Prompt |
| 结构化输出（JSON Schema） | [x] | OpenAI Chat/Responses `response_format`；非法 JSON 计为失败 |
| 并发调用 / token/成本统计 | [x] | `MaxConcurrency` 保序并发；token 与估算成本写日志 |
| 失败样本单独输出 | [x] | `ContinueOnError` + `FailureOutputPath` 原子写 `{lineNumber,error,data}` |
| 流式输出 / 模型列表 | [ ] | LLM 节点无流式；Simulation 支持 OpenAI Chat SSE |

### 2.3 Expression（字段变换）

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| `{{ field.path }}` 替换 | [x] | `TemplateRenderer`，支持点路径 |
| 数组 `length` | [x] | `{{ messages.length }}` |
| 字段别名映射 | [x] | `ExpressionBlockConfig.Fields` |
| 无模板时字段映射 | [x] | 输出只含映射字段 |
| Fluid 模板引擎 | [x] | 条件、循环、过滤器与 `json` 过滤器 |
| 输出 JSON 合法性校验 | [x] | 渲染后立即解析；失败带行号与 JSON 位置 |
| 专属属性面板 | [x] | 模板、字段别名、样本 JSON、预览输出 |
| 渲染预览 / 语法校验 | [x] | 编辑期模板语法校验 + 样本渲染预览 |
| 自定义函数 / 类型转换 | [ ] | 仅 Fluid 内置能力 |
| 错误行策略（跳过/写失败文件） | [ ] | 模板错误即节点失败 |
| 沙箱 / 超时 | [ ] | 纯模板渲染，无外部执行；暂不需要 |

### 2.4 Validator（规则校验与过滤）

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| 表达式规则 | [x] | `<path> <op> <value>` 或 `<path> exists`；支持 `== != contains > >= < <=` |
| JSON Schema 规则 | [x] | JsonSchema.Net |
| 非法行处理 | [x] | 解析失败视为 reject |
| `RejectOnFailure=false` 透传 | [x] | 不匹配行也放行 |
| 通过行可选清洗 | [x] | `ValidatorBlockConfig.Cleanup` |
| 通过/拒绝计数日志 | [x] | 节点日志输出 |
| reject 输出端口 / 失败文件 | [x] | `RejectOutputPath` 原子写 `{ lineNumber, reason, data }` JSONL |
| 专属属性面板 | [x] | 规则类型、规则/Schema、reject 开关与路径 |
| 失败样本查看 / 失败率 / 超时 | [x] | reject 文件 + 失败率日志 + 单行规则超时 |
| 代码规则 | [x] | `script` 规则用 Fluid 渲染 true/false（受限表达式，不是任意代码） |
| 多规则组合 / 正则规则 | [x] | `Rules` 数组 + MatchAll/Any + `regex` |
| 独立 pass/reject 输出端口 | [x] | reject 端口输出原始行；reject 文件保留原因 |
| 问题码 / 校验摘要 UI | [部分] | 失败率与规则原因在日志/reject 文件；无独立摘要面板 |

### 2.5 Sampler（确定性采样）

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| 按数量采样 | [x] | reservoir sampling |
| 按比例采样 | [x] | Bernoulli + 固定 seed |
| 确定性种子 | [x] | `Seed` |
| `Splits` 切分 | [x] | `name:ratio` / `name:count`；确定性、单行只进一个文件 |
| 多输出端口 / 多文件 | [x] | splits 每项一个输出端口；无 splits 时保留 `output` 端口 |
| 专属属性面板 | [x] | Count / Ratio / Seed / Splits / 输出目录 / 分层字段 / 统计预览 |
| 采样结果预览 / 统计 | [x] | 面板显示比例与预计行数；运行日志汇总各 split 实际行数 |
| 分层 / 分组采样 | [部分] | ratio 与 splits 模式按顶层字段独立随机；count 模式不分层 |
| 切分内再混洗 / 保序选项 | [ ] | 无 |

### 2.6 Tool Profile（工具配置）

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| 配置模型 | [x] | `ToolProfileBlockConfig` + `ToolProfile` + `ToolDefinition` |
| 默认配置与 JSON 编辑 | [x] | 专属面板 + 通用 Config JSON |
| 执行器 | [x] | `ToolProfileBlockExecutor` 配置型节点，无数据端口 |
| 被 Simulation / LLM 复用 | [x] | Simulation 合并 profile tools；LLM 将 tools 写入输出元数据 |
| Tool Schema 树形编辑器 | [x] | 参数树可增删/改名/改类型/必填；与 JSON Schema 双向同步 |
| JSON 导入 / 导出 | [x] | 支持完整 profile、tool 数组或单个 tool |
| 端口 / 校验 | [x] | 无数据端口；工具名/重复/引用校验在 RecipeValidationService |
| Enum / Default 值 UI | [ ] | 模型支持，面板未暴露 |
| 共享工具库 / 版本管理 | [ ] | profile 存在 Recipe 内 |
| MCP 工具 | [ ] | 无 stdio/HTTP MCP 客户端与工具发现 |
| 鉴权 / 工具调用请求侧复用 | [ ] | 仅 schema 复用 |

### 2.7 Query Source（模拟用户 query）

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| 同 query 重复 | [x] | `SameQuery` + `Repeat` |
| 多组 query 展开 | [x] | `MultipleQueries` |
| System Prompt | [x] | 写入行 `system` 字段 |
| 专属属性面板 | [x] | 多行 query、模式、Repeat、System Prompt |
| query 模板 / 变量替换 | [x] | Fluid 模板变量 + `index` |
| CSV / JSONL / 外部 query 源 | [x] | JSONL/CSV/Parquet + QueryField/SystemField |
| 随机 / 模型生成 query | [ ] | 无 |
| 去重 / 元数据 / 权重 | [ ] | 外部源保留原字段；无去重/权重 |

### 2.8 Simulation（多轮 tool-calling 轨迹）

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| 确定性脚本模型 | [x] | `ScriptedChatClient` |
| 真实 provider tool-calling | [x] | OpenAI Chat/Responses、Anthropic `tool_use`；已真实冒烟 |
| 模拟工具 | [x] | python / web_fetch / powershell / bash |
| 真实沙箱工具 | [x] | python/PowerShell/bash/web_fetch；环境白名单、超时、输出上限、Windows Job Object |
| 工具白名单 / 重试 / 超时 / 中止 | [x] | `ToolHarnessRules` + 单工具规则 |
| 最大轮数 / 模型重试 / 模型超时 | [x] | `MaxRounds` / `ModelMaxRetries` / `TimeoutSeconds` |
| 多 query 并发 + 输出保序 | [x] | `MaxConcurrency` |
| tokens / 延迟 / 成本统计 | [x] | 逐行与汇总写 run log，不进 SFT 样本 |
| 未完成轨迹独立输出 | [x] | `FailureOutputPath` 原子写，`MaxFailureSamples` 限制 |
| 专属属性面板 | [x] | provider/模式/并发/成本/失败样本/沙箱参数 |
| Tool Profile 合并 | [x] | profile tools 与节点 tools 合并，节点优先 |
| Tools / Rules / Script UI 编辑器 | [x] | 工具行、Harness 规则、脚本回合增删与编辑 |
| 流式 delta / 速率限制 / 价格预设 | [部分] | OpenAI Chat SSE；RequestsPerMinute 近似限速；内置价格预设 |
| 容器级隔离 / 非 Windows 资源限制 | [ ] | 当前为协作式沙箱 |
| MCP 工具接入 | [ ] | 依赖 Tool Profile MCP |
| 工具结果裁剪/摘要策略 UI | [ ] | 仅输出上限与超时 |

### 2.9 Cleaner（轨迹清洗）

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| 正则替换 | [x] | `RegexReplacementRule`，可忽略大小写 |
| 空白 / 换行规范化 | [x] | |
| ANSI 清理 | [x] | |
| 移除 reasoning_content | [x] | |
| 丢弃空消息 | [x] | |
| 清洗 `messages` 与 `tools[].description` | [x] | 未知字段保留；无 `messages` 的行原样返回 |
| 专属属性面板 | [x] | 5 个布尔开关 + 正则规则列表已接入 |
| 清洗预览 / diff | [x] | 统一样本预览支持 Cleaner 前后 diff |
| 坏行跳过与报告 | [ ] | 遇到坏 JSON 直接失败 |
| 规则列表 UI | [x] | 增删、上下移动、忽略大小写与正则校验 |
| 自定义字段/JSONPath 清洗 | [ ] | 无 |

### 2.10 SFT Export（训练样本导出）

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| JSONL 流式写出 | [x] | 临时文件 + 原子替换；取消/异常删除临时文件 |
| JSON 规范化 | [x] | 每行解析后重写，保留原有字段 |
| 按行透传给下游 | [x] | 节点有输出端口，下游可继续处理 |
| 专属属性面板 | [x] | 输出路径、Browse 文件选择器、字段白名单 |
| “只输出 messages + tools” | [x] | 字段白名单可只保留指定顶层字段；留空则整行透传 |
| 文件选择器 | [x] | SaveFilePicker，默认 `.jsonl` |
| 覆盖确认 / 追加模式 | [部分] | 运行前确认覆盖；无追加模式 |
| 输出预览 / 样本查看 | [x] | 统一样本预览弹窗与运行日志摘要 |
| 字段白名单 / 目标格式选择 | [部分] | 顶层字段白名单已接入并校验；无目标格式选择 |
| 按数量/比例拆分多文件 | [ ] | 可用 Sampler 前置切分 |
| DPO / Pretrain 直出 | [ ] | 数据工作台批量转换支持，节点本身只写 JSONL |

### 2.11 工作流预设与界面本地化

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| 内置工作流预设 | [x] | 6 个：LLM 批量生成、OAI 轨迹清洗、字段映射、校验与切分、模拟 Agent 轨迹、真实工具调用 |
| 预设入口 | [x] | File → New from Preset；节点库 Workflow Presets 列表 |
| 自定义预设保存/导入/删除 | [x] | `RecipePresetStore` 单文件存储；保存当前 Recipe、导入外部预设、删除自定义预设 |
| 预设持久化位置 | [x] | `%APPDATA%\LMTrainingDataStudio\presets`；原子写入，坏文件跳过并告警 |
| 预设合法性 | [x] | 每个内置/导入预设均通过 schema 与 Recipe 校验；有自动化测试 |
| 自定义预设导出到任意路径 | [ ] | 当前保存到本机预设目录，可直接复制 JSON 分享 |
| 节点说明目录 | [x] | `BlockCatalog` 统一提供名称/简介/详细说明/端口摘要 |
| 属性面板 Node Guide | [x] | 选中节点后展示说明与输入/输出端口 |
| 画布节点悬停提示 | [x] | 显示同一份节点简介 |
| zh-CN / en-US 资源化 | [x] | 约 230 个静态 UI 键；`{loc:Loc key}` 绑定切换即时刷新 |
| 语言设置持久化 | [x] | `system` / `zh-CN` / `en-US`；非法值回退并告警 |
| 运行日志/执行状态本地化 | [ ] | 保留英文技术文本 |

---

## 三、跨节点与引擎能力

### 3.1 已完成

- [x] 强类型 `BlockConfig` 多态层级、`kind` 判别、默认工厂、深拷贝、类型匹配校验。
- [x] Recipe / Settings 原子持久化服务与 Recipe schema 版本。
- [x] Recipe 文件生命周期：新建（空白/示例）、打开、保存、另存为、最近文件、脏标记、关闭确认与重启恢复。
- [x] 端口契约：按 BlockType 生成端口；连接前校验方向、DataType、重复边、单输入与环；非法连接被拦截。
- [x] 校验问题列表（属性面板）与画布节点错误/警告徽标，问题可定位到节点。
- [x] Expression：Fluid 模板引擎（条件/循环/过滤器）、`json` 过滤器、输出 JSON 校验、专属面板与样本预览。
- [x] Validator：JSON Schema 规则、reject 文件（lineNumber/reason/data）、失败率日志、专属面板。
- [x] Sampler：ratio/count 多文件切分、确定性复现、原子写入与专属面板。
- [x] Tool Profile：参数树编辑、JSON 导入导出、配置型执行器、Simulation/LLM 引用复用。
- [x] 统一数据集读写（JSONL / CSV / Parquet）与临时文件 + 原子替换写出。
- [x] 数据工作台：分页预览、行号跳转、页面大小切换、批量格式转换与失败行报告。
- [x] 数据工作台编辑：插入/复制/删除/恢复、字段级单元格编辑与整行 JSON、跨页待保存编辑、撤销/重做、保存前摘要、另存/覆盖确认、流式原子重写。
- [x] 搜索命中行内高亮与校验问题级别/错误码筛选，支持按级别/错误码分组树。
- [x] 统一样本预览窗口：任意数据集文件与 Cleaner 前后 diff，可跳转数据工作台。
- [x] 数据质量与治理：校验问题列表（级别/行号/路径/错误码）与跳行、JSONL 自动修复预览与写副本、搜索/正则替换、去重、确定性混洗、比例/份数切分、数据集统计。
- [x] DPO / Pretrain 格式检测、规范化与转换矩阵；不支持组合显式失败。
- [x] 1M 行 JSONL 性能与内存基线；1M 行质量操作基线（去重+混洗+切分 124.2s、统计 63.6s、托管内存增量 2MB）。
- [x] JSONL 读取、索引缓存、LRU 容量清理、源文件指纹失效。
- [x] DAG 校验：空 Recipe、重名、环、悬空边、孤立节点、重复边、端口归属/方向、DataType 兼容、节点配置告警。
- [x] 执行引擎：拓扑分层、层内受控并行、fail-fast、下游 Skipped、取消、进度、日志。
- [x] 执行控制：暂停/恢复、节点重试、运行输出目录与 `run-summary.json`。
- [x] 执行记录 `ExecutionStore` 与 Executions UI（列表、状态过滤、节点表、日志、取消、输出目录）。
- [x] 真实 provider 客户端、密钥设置/环境变量、超时与指数退避重试。
- [x] 真实沙箱与 Windows 资源限制。
- [x] 模拟四节点流水线、并发、成本、失败样本。
- [x] 通用 Config JSON 编辑器（kind/BlockType 一致性校验）。
- [x] 撤销/重做基础能力：添加、删除、移动、连线、删除边、粘贴（块）、重命名。
- [x] 画布基础交互：Block Sheet 拖放、可搜索添加菜单（`~` / 右键）、节点拖动、多选拖动、框选、端口连线、边选择/删除、中键平移、滚轮缩放、右键菜单、Mini-map 渲染、缩放百分比、可拖动分栏。
- [x] 画布增强：网格背景与吸附（Alt 关闭）、鼠标中心缩放、Fit、拓扑自动布局、Mini-map 视口/点击、双击添加、复制携带连线、内联重命名、真实端口坐标、边 hover、增量拖拽。
- [x] Settings 持久化：缓存目录/上限、网格设置、执行并行/重试/输出目录、主题选择、API Key 本机保存。
- [x] 工程化：滚动日志与全局异常、CI、win-x64 单文件发布、Headless UI 测试、MIT LICENSE、应用图标、主题变体。
- [x] 最近文件路径与 `CurrentFilePath` 状态栏接线；`TotalLines` 随执行进度更新；缩放百分比实时显示。

### 3.2 部分完成

- [部分] 校验：已覆盖端口契约、必需输入、单输入、方向/类型、环与配置；缺少列号与问题分组（数据集校验已有错误码/路径）。
- [x] 执行：层内并行/暂停/重试、输出摘要、节点间溢写/流式、节点级恢复、输出 SHA256/Recipe 指纹校验；检查点仍无配额。
- [x] 画布：端口坐标、缩放锚点/Fit/自动布局/Mini-map 点击/复制连线、Undo/Redo 菜单、节点尺寸手柄、边动画、标签避让与属性撤销已完成；逐帧性能未做 UI 级计时。
- [x] 设置：网格吸附、完整 Dark/Light 主题、语言切换与内存定时刷新生效；其他设置变更仍不通知已创建的服务。
- [x] 主题：Dark/Light 资源完整，视图无 `#RRGGBB` 硬编码颜色残留。

### 3.3 未实现（按领域）

**数据与数据工作台**

- [x] 数据逐行 CRUD：插入、删除、复制、字段级单元格编辑与整行 JSON 编辑、另存或确认覆盖源文件。
- [x] 数据编辑撤销/重做与保存前差异摘要。
- [x] 搜索命中行内高亮；校验问题按级别/错误码筛选与分组树。
- [x] 统一样本/输出预览弹窗（SFT Export、Validator reject、Simulation 失败样本、Cleaner 前后 diff）。
- [x] 跨格式编辑保存。
- [ ] 消息/角色级表单编辑、匹配项导航、预览窗口分页。
- [ ] CSV 类型推断与列类型展示；CSV/Parquet 1M 行质量基线；10M 行实测。
- [ ] Seed 节点内过滤/列映射/多文件/目录/HuggingFace/HTTP 源。
- [x] 字段白名单与输入校验。
- [ ] 输出文件追加模式、目标格式选择。

**执行与性能**

- [x] 节点间流式读取与溢写磁盘（内存阈值、临时目录、成功后清理）。
- [x] 节点级中断恢复：记录已完成节点输出，从上次失败/中断节点继续。
- [x] 取消 1s 内停止的定量验收；全链路临时文件清理验收。
- [x] 恢复输出 SHA256 与 Recipe 指纹校验。
- [ ] 执行竞态/并发写同一目标文件的检测与提示。
- [ ] 10M 行执行/质量/转换基线。
- [ ] 执行历史批量清理与保留策略。

**节点能力**

- [x] LLM 请求侧 tools、结构化输出、多轮上下文、并发、token/成本统计、失败样本。
- [x] Validator 受限代码规则、超时、多规则组合、失败样本预览、独立 pass/reject 端口。
- [x] Sampler 多输出端口、分层/分组采样、统计预览。
- [部分] Simulation Tools/Rules/Script 可视化编辑器、流式 delta、速率限制/价格预设已完成；容器级隔离未做。
- [部分] Query Source 模板与外部数据源已完成；随机/模型生成 query 未做。
- [ ] Tool Profile MCP（stdio/HTTP）、共享工具库、鉴权。
- [部分] Validator/LLM/SFT Export 的文件选择器与路径校验统一：SFT Export 已接入，其余仍手输路径。

**画布与 UI**

- [部分] 节点尺寸手柄、边过渡动画、标签避让已完成；自动布局间距/分组选项未做。
- [x] Undo/Redo 菜单与按钮、`Ctrl+Shift+Z`、属性面板删除/配置修改进入撤销历史。
- [ ] 系统剪贴板/跨实例粘贴；全局快捷键避免焦点冲突。
- [x] 完整浅色主题（移除硬编码深色）。
- [部分] 状态栏内存定时刷新已完成；可拖动分栏列宽持久化未做。

**发布与工程化**

- [ ] 10M 性能报告与 CSV/Parquet 基线。
- [部分] MSIX/Inno 脚本、文件关联、单实例、命令行打开已完成；安装包未在干净机器实际安装验证。
- [部分] README 截图与 6 个示例 Recipe 已完成；发布说明由 [CHANGELOG.md](CHANGELOG.md) 承担。
- [ ] 崩溃报告导出；日志级别/来源筛选。
- [ ] API Key 加密存储、设置变更通知。
- [部分] 运行日志/执行状态/统计摘要本地化（静态 UI、节点说明与预设已支持 zh-CN / en-US）。
- [x] `.editorconfig`、分析器、警告即错误。

---

## 四、节点 UI 编辑器覆盖

| 节点 | 专属面板 | 已覆盖字段 | 仍依赖 Config JSON |
| --- | --- | --- | --- |
| Seed | 是 | 路径、文件类型、Index Cache、Streaming | 无 |
| LLM | 是 | Provider、Model、温度、Max tokens、超时、重试、Endpoint、Prompt、Tool Profile | 无 |
| Expression | 是 | 模板、字段别名、样本 JSON、预览输出 | 无 |
| Validator | 是 | 规则类型、规则/Schema、reject 开关与路径、Cleanup 规则 | 无（脚本仍为受限 Fluid） |
| Sampler | 是 | Count、Ratio、Seed、Splits、输出目录 | 无 |
| Tool Profile | 是 | 工具列表、参数 JSON、参数树、profile JSON 导入导出 | Enum/Default 未暴露 |
| Query Source | 是 | Queries、Mode、Repeat、System Prompt | 无 |
| Simulation | 是 | Provider、模式、模型、轮数、并发、温度、tokens、超时、重试、Endpoint、System/Final、失败样本、成本、沙箱、Tool Profile、Tools/Rules/Script | 无（容器隔离未做） |
| Cleaner | 是 | 5 个布尔规则 + 正则规则 | 无（高级 JSONPath 未做） |
| SFT Export | 是 | 输出路径、Browse、字段白名单与校验 | 目标格式选择、追加模式 |

---

## 五、已完成轮次（Round 8–19）

| Round | 主题 | 关键交付 | 结果 |
| --- | --- | --- | --- |
| 8 | Recipe 生命周期与节点契约 | File 菜单/快捷键、最近文件、脏标记、关闭确认、上次文件恢复、按类型端口契约、错误徽标 | 285 测试 |
| 9 | 节点能力补齐 | Fluid 模板、JSON Schema + reject、Sampler Splits、Tool Profile 树形编辑与复用 | 311 测试 |
| 10 | 数据工作台 A | JSONL 分页/跳转、CSV/Parquet、批量转换、DPO/Pretrain、1M 基线 | 331 测试 |
| 11 | 数据工作台 B | 校验/修复、搜索替换、去重/混洗/切分、统计 | 368 测试 |
| 12 | 画布、执行与发布 | 网格/吸附/锚点缩放/Fit/自动布局/Mini-map、并行/暂停/重试/输出摘要、CI/打包/主题/日志 | 386 测试 + 4 gated |
| 13 | 执行与 IO 韧性 | 溢写缓冲、检查点/恢复、取消清理硬化、SFT 收尾 | 437 测试 + 5 gated |
| 14 | 数据工作台编辑与预览 | 逐行 CRUD/字段级单元格编辑/撤销重做/另存与覆盖、搜索高亮、校验筛选与分组树、统一样本预览 | 480 测试 + 6 gated |
| 15 | 节点能力收紧 | 多输出端口、LLM tools/结构化输出/并发/失败样本、Validator 多规则/超时/双端口、Sampler 分层与端口、Simulation 可视化编辑器/流式/限速/价格、Query Source 模板与外部源 | 495 测试 + 6 gated |
| 16 | 发布与体验 | 画布尺寸/边动画/标签避让、Undo/Redo 菜单与属性撤销、完整浅色主题、内存定时刷新、单实例/命令行/诊断、MSIX/Inno、示例与截图、10M/CSV/Parquet 基线入口、分析器 | 505 测试 + 7 gated |
| 17 | Bugfix1 一致性 | `.recipe` 全链路、版本单一来源与图标、打包参数、Seed/Cleaner/Validator/LLM/SFT 编辑缺口、跨格式保存、恢复哈希/指纹、文档同步 | 531 测试 + 7 gated |
| 18 | 测试与 CI 门禁 | 单实例/命令行/打包一致性/本地化键测试、设置面板有界输入与目录选择、格式门禁、Debug/Release 矩阵、覆盖率门禁、示例漂移与发布冒烟 | 550 测试 + 7 gated |
| 19 | 数据工作台与入口打磨 | 搜索匹配上一处/下一处、样本预览分页、最近文件清理与失效路径移除、执行输出目录打开 | 556 测试 + 7 gated |

---

## 六、下一阶段规划（Round 13–16）

> 排序原则：先解决会限制后续功能的数据规模与执行可靠性，再补用户直接可见的数据编辑与节点能力，最后做发布体验。

### Round 13 — 执行与 IO 韧性（P0）

> 状态：已完成（2026-11-17）。新增 `RowSpillBuffer`、节点检查点/恢复、Executions 恢复入口、SFT Export 文件选择/覆盖确认/字段白名单；新增 17 个测试，1M 行链路 17.2s、托管内存增量 1MB。

**目标**：让 DAG 在大数据量下不依赖全量内存，并具备可恢复、可验证的取消/清理行为。

**交付**

1. 节点间输出溢写：为每条边建立临时 JSONL 缓冲，达到内存阈值后落盘；下游按流读取；运行结束/取消/失败统一清理。
2. 执行恢复：在运行输出目录保存节点完成状态与输出引用；Executions UI 提供“从上次失败/中断处继续”。
3. 取消与超时硬化：取消后 1s 内停止新行处理；所有目标文件保持原子替换；清理 `.tmp` 与溢写目录。
4. SFT Export 收尾：输出路径文件选择器、覆盖确认、可选字段白名单（`messages`/`tools`）、写出行数与预览摘要。
5. 测试：1M 行多节点链路内存上限、取消时序、恢复一致性、临时文件清理。

**验收**

- 1M 行 Seed → Expression → SFT Export 链路托管内存峰值 ≤ 300MB（可配置阈值），不一次性物化全部行。
- 取消后 1s 内停止，目标文件不存在半成品，溢写目录为空。
- 中断后恢复跳过已完成节点，最终输出与不中断运行逐字节一致（或哈希一致）。
- `dotnet build` 0 警告，测试全绿。

### Round 14 — 数据工作台编辑与预览（P1）

> 状态：已完成（2026-11-17）。新增 `DatasetEditSession`/`DatasetEditService`、字段级单元格编辑器、工作台编辑工具栏、`HighlightMatcher`、校验问题筛选与分组树、`SamplePreviewService` 与统一预览窗口；新增 43 个测试与 1 个 gated 1M 行编辑保存基线（55.6s，托管内存增量 11MB）。

**目标**：把 Data 视图从“只读预览 + 批处理”升级为可编辑的数据工作台。

**交付**

1. 逐行 CRUD：插入/删除/复制行，单元格 JSON 编辑，保存到新文件或覆盖原文件（覆盖需确认）。
2. 编辑撤销/重做与保存前差异摘要；大文件按页编辑 + 流式重写，不整文件载入。
3. 搜索命中高亮；校验问题按级别/错误码分组与筛选。
4. 统一样本/输出预览：SFT Export 输出、Validator reject、Simulation 失败样本、Cleaner 前后 diff。
5. 测试：编辑行数守恒、撤销恢复、覆盖保护、1M 行编辑保存内存上限。

**验收**

- 任意页可编辑并保存，未修改行字节不变；取消保存不破坏原文件。
- 撤销/重做恢复内容与行号；搜索命中在表格中可见。
- 1M 行编辑/保存托管内存增量 ≤ 300MB。
- 预览弹窗与节点/文件路径可双向定位。

### Round 15 — 节点能力收紧（P1）

> 状态：已完成（2026-11-17）。新增多输出端口执行契约、LLM 请求侧 tools/结构化输出/并发/失败样本、Validator 多规则与双端口、Sampler 分层与多端口、Simulation 可视化编辑器/流式 delta/限速/价格、Query Source 模板与外部源；新增 14 个测试，495 通过 + 6 gated。

**目标**：补齐影响数据质量与工具调用工作流的关键节点能力。

**交付**

1. LLM：请求侧 tool definitions、结构化输出（JSON Schema/`response_format`）、多轮上下文、并发、token/成本统计、失败样本。
2. Validator：代码规则（受限表达式/脚本）+ 超时 + 多规则组合 + 失败样本 UI + 独立 pass/reject 端口。
3. Sampler：多输出端口（train/valid/test）、分层/分组采样、切分统计预览。
4. Simulation：Tools / Rules / Script 可视化编辑器；流式 delta；速率限制与价格预设。
5. Query Source：模板变量、外部 CSV/JSONL query 源。
6. 测试：请求体断言、结构化输出校验、规则超时、端口行数守恒、分层分布偏差、编辑器回写。

**验收**

- LLM 请求中能看到 tools 与 structured output 配置，非法输出有明确错误。
- Validator 超时可中断，pass/reject 端口行数守恒。
- Sampler 多端口输出行数守恒，分层比例在容差内。
- Simulation 不编辑 Config JSON 也能完成 Tools/Rules/Script 配置。
- 每个节点至少覆盖成功/失败/边界路径。

### Round 16 — 发布与体验（P2）

> 状态：已完成（2026-11-17）。节点尺寸手柄/边动画/标签避让、Undo/Redo 菜单与属性撤销、完整浅色主题、内存定时刷新、单实例/命令行/文件关联、诊断与崩溃报告、MSIX/Inno 脚本、示例 Recipe 与截图、分析器与警告即错误落地；新增 10 个测试，505 通过 + 7 gated。

**目标**：达到可对外演示/分发的完成度。

**交付**

1. 画布 polish：节点尺寸手柄、边过渡动画、标签避让、自动布局间距/分组选项、Undo/Redo 菜单与属性修改撤销。
2. 完整浅色主题，移除剩余硬编码颜色；状态栏内存定时刷新。
3. 发布：MSIX/安装包、文件关联、单实例、命令行打开 Recipe、崩溃报告导出。
4. 性能与文档：10M 行基线、CSV/Parquet 大规模基线、README 截图、示例 Recipe、发布说明。
5. 工程质量：`.editorconfig`、分析器、警告即错误、文案统一。

**验收**

- 500 节点拖动/缩放/尺寸调整单帧 < 100ms（逐帧计时）。
- 浅色主题下所有主视图无深色残留。
- 干净环境安装启动，双击 `.recipe.json` 可打开，重复启动复用单实例。
- 10M 行基线数据记录在文档中，README 截图与示例可直接使用。

### 6.1 后续 backlog（暂不排期）
- Tool Profile MCP（stdio/HTTP）、共享工具库与鉴权。
- 容器级沙箱隔离、非 Windows 资源限制。
- HuggingFace/HTTP 数据源。
- 多语言 UI、插件化节点、云端执行。
- 实时图表（LiveCharts2/OxyPlot）与执行指标面板。

---

## 七、验证命令

```powershell
dotnet build LMTrainingDataStudio2/LMTrainingDataStudio2.slnx
dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx
dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx --filter HeadlessUiTests
dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx --filter Round15NodeCapabilityTests
```

win-x64 单文件发布：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-win-x64.ps1
```

1M 行质量操作基线（可把 `LMTDS_PERF_ROWS` 改为 `10000000` 跑 10M）：

```powershell
$env:LMTDS_PERF_TESTS = "1"
$env:LMTDS_PERF_ROWS = "1000000"
dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx --filter QualityOperations_LargeInput
```

1M 行编辑保存基线：

```powershell
$env:LMTDS_PERF_TESTS = "1"
dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx --filter EditSave_LargeInput
```

真实 provider 冒烟（可选，Key 只放环境变量）：

```powershell
$env:LMTDS_PROVIDER_SMOKE_KEY = "<your-api-key>"
$env:LMTDS_PROVIDER_SMOKE_BASE_URL = "https://api.openai.com/v1"
$env:LMTDS_PROVIDER_SMOKE_MODEL = "gpt-4o-mini"
dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx --filter ProviderSmokeTests
```