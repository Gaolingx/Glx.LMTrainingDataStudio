# STATUS — 功能清单、节点能力矩阵与下一阶段规划

> 最后核对：2026-11-16，Round 9 完成后（工作区）
> 验证基线：`dotnet build` 0 警告 0 错误；`dotnet test` 311 通过 + 2 个需环境变量的真实 provider 冒烟（gated，默认跳过）；真实 provider 冒烟此前已跑通。
>
> 图例：`[x]` 已完成并有测试/验证；`[部分]` 可用但有明确缺口；`[ ]` 未实现。

---

## 一、总体结论

### 1.1 已形成的可用闭环

- Recipe 内存模型、强类型节点配置、多态 JSON 序列化、深拷贝、Recipe/Settings 持久化服务。
- Recipe 文件生命周期闭环：新建、从示例新建、打开、保存、另存为、最近文件、脏标记、关闭确认、重启恢复上次文件。
- 按 BlockType 生成端口 schema；连接前校验方向/类型/单输入/重复/环；校验问题列表与画布错误/警告徽标。
- Expression 使用 Fluid 模板引擎（条件/循环/过滤器），带专属面板、语法校验与样本预览；输出 JSON 强校验。
- Validator 支持表达式与 JSON Schema 规则、reject 文件、失败率日志；Sampler 支持确定性多文件切分。
- Tool Profile 支持参数树编辑与 JSON 导入导出，并被 Simulation/LLM 复用。
- JSONL 流式读取、SQLite 行索引缓存、分页读取。
- DAG 校验、拓扑分层、顺序执行、fail-fast、下游 Skipped、取消、进度、日志、执行记录持久化与 Executions UI。
- 10 种节点类型全部接入 Block Sheet、画布、默认配置与 Recipe JSON。
- Query Source → Simulation → Cleaner → SFT Export 端到端 SFT 轨迹生成可用（脚本/真实 provider、模拟/真实工具均可切换）。
- 真实 provider tool-calling（OpenAI Chat/Responses、Anthropic）与真实沙箱工具（python/PowerShell/bash/web_fetch）。
- 311 个自动化测试 + 2 个 gated 真实 provider 冒烟测试。

### 1.2 当前最大缺口

1. **节点能力仍有边界**：Validator 暂无代码规则/超时；Sampler 有 Splits 多文件但无多输出端口与分层采样；Tool Profile 无 MCP 适配；LLM 仍不发送 tool definitions 给 provider。
2. **数据工作台未接 UI**：CSV/Parquet、格式转换、校验修复、分页预览、数据编辑、去重/混洗/切分、搜索替换都还没有界面。
3. **画布体验缺口**：网格背景/吸附、缩放锚点、Fit、自动布局、Mini-map 视口/点击、复制携带连线、可调分栏等。
4. **发布工程化缺口**：Headless UI 测试、1M/10M 性能验收、CI、打包、主题切换、日志/崩溃处理、LICENSE/Assets。

---

## 二、节点能力矩阵（全部 10 个节点）

### 2.1 Seed（数据输入）

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| JSONL 流式读取 | [x] | `SeedBlockExecutor` + `JsonlDatasetReader` |
| SQLite 索引缓存 | [x] | `UseIndexCache=true` 时走索引；可配置容量并清理 |
| 行数进度 | [x] | `RowsProcessed` 上报 |
| 专属属性面板 | [x] | 路径选择、文件类型、Index Cache 开关 |
| 端口 schema 按类型固定 | [x] | 仅输出 `rows`；连接前校验方向/类型 |
| CSV / Parquet 读取 | [ ] | UI 可选，执行时抛 `NotSupportedException`；仅 JSONL 可用 |
| `Streaming` 开关生效 | [ ] | 配置存在，但执行分支只由 `UseIndexCache` 决定 |
| 数据预览 / 过滤 / 列映射 | [ ] | 无 |
| 大文件 1M 行性能验收 | [ ] | Phase 5 未做 |

### 2.2 LLM / Model（单轮模型调用）

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| OpenAI Chat Completions | [x] | `LlmApiClient` |
| OpenAI Responses | [x] | `LlmApiClient` |
| Anthropic Messages | [x] | `LlmApiClient` |
| 模型、Endpoint、温度、Max tokens | [x] | 专属面板 |
| Prompt 模板 | [部分] | 仅 `{{ field.path }}`，空 Prompt 时发送整行 JSON |
| 节点级超时 / 指数退避重试 | [x] | `TimeoutSeconds` / `MaxRetries` |
| 密钥设置 | [x] | settings.json 本机保存，环境变量兜底 |
| 输出格式 | [x] | 每行输出 `messages[{role:assistant,content}]` |
| 多轮上下文 / tools / 结构化输出 | [ ] | LLM 节点只有单条 user 内容；tool-calling 只在 Simulation |
| 并发调用 / 成本统计 | [ ] | 无 |
| Prompt 预览 / Jinja / 语法校验 | [ ] | 无 |
| 失败样本单独输出 | [ ] | 失败即节点失败 |

### 2.3 Expression（字段变换）

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| `{{ field.path }}` 替换 | [x] | `TemplateRenderer`，支持点路径 |
| 数组 `length` | [x] | `{{ messages.length }}` |
| 字段别名映射 | [x] | `ExpressionBlockConfig.Fields` |
| 无模板时字段映射 | [x] | 输出只含映射字段 |
| 真实 Jinja2 / Liquid 引擎 | [x] | Fluid.Core；支持条件、循环、过滤器与 `json` 过滤器 |
| 输出 JSON 合法性校验 | [x] | 渲染后立即解析；失败带行号与 JSON 位置 |
| 专属属性面板 | [x] | 模板、字段别名、样本 JSON、预览输出 |
| 渲染预览 / 语法校验 | [x] | 编辑期模板语法校验 + 样本渲染预览 |
| 沙箱 / 超时 | [ ] | 纯模板渲染，无外部执行 |

### 2.4 Validator（规则校验与过滤）

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| 表达式规则 | [x] | `<path> <op> <value>` 或 `<path> exists`；支持 `== != contains > >= < <=` |
| 非法行处理 | [x] | 解析失败视为 reject |
| 通过行可选清洗 | [x] | `ValidatorBlockConfig.Cleanup` |
| 通过/拒绝计数日志 | [x] | 节点日志输出 |
| reject 输出端口 / 失败文件 | [x] | `RejectOutputPath` 原子写 `{ lineNumber, reason, data }` JSONL |
| JSON Schema / 代码规则 | [部分] | JSON Schema（JsonSchema.Net）；代码规则未实现 |
| 专属属性面板 | [x] | 规则类型、规则/Schema、reject 开关与路径 |
| 失败样本查看 / 失败率 / 超时 | [部分] | 失败样本落盘 + 失败率日志；无超时 |
| 端口 schema 固定为输入/输出 | [x] | 输入 + 输出 `rows`；必需输入连接校验 |

### 2.5 Sampler（确定性采样）

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| 按数量采样 | [x] | reservoir sampling |
| 按比例采样 | [x] | Bernoulli + 固定 seed |
| 确定性种子 | [x] | `Seed` |
| `Splits` 切分 | [x] | `name:ratio` / `name:count`；确定性、单行只进一个文件 |
| 多输出端口 / 多文件 | [部分] | 多文件已实现；多输出端口未实现 |
| 分层采样 | [ ] | 无 |
| 专属属性面板 | [x] | Count / Ratio / Seed / Splits / 输出目录 |
| 采样结果预览 / 统计 | [ ] | 仅日志汇总各 split 行数 |

### 2.6 Tool Profile（工具配置）

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| 配置模型 | [x] | `ToolProfileBlockConfig` + `ToolProfile` + `ToolDefinition` |
| 默认配置与 JSON 编辑 | [x] | 专属面板 + 通用 Config JSON |
| 执行器 | [x] | `ToolProfileBlockExecutor` 配置型节点，无数据输出 |
| 被 Simulation / LLM 复用 | [x] | Simulation 合并 profile tools；LLM 将 tools 写入输出元数据 |
| Tool Schema 树形编辑器 | [x] | 参数树可增删/改名/改类型/必填；与 JSON Schema 双向同步 |
| JSON 导入 / 导出 | [x] | 支持完整 profile、tool 数组或单个 tool |
| MCP 工具 | [ ] | 无 |

### 2.7 Query Source（模拟用户 query）

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| 同 query 重复 | [x] | `SameQuery` + `Repeat` |
| 多组 query 展开 | [x] | `MultipleQueries` |
| System Prompt | [x] | 写入行 `system` 字段 |
| 专属属性面板 | [x] | 多行 query、模式、Repeat、System Prompt |
| query 模板 / 变量替换 | [ ] | 无 |
| CSV / 外部 query 源 | [ ] | 无 |
| 随机 / 模型生成 query | [ ] | 无 |

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
| Tools / Rules / Script UI 编辑器 | [ ] | 仍用 Config JSON |
| 流式 delta / 速率限制 / 价格预设 | [ ] | 无 |
| 容器级隔离 / 非 Windows 资源限制 | [ ] | 当前为协作式沙箱 |

### 2.9 Cleaner（轨迹清洗）

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| 正则替换 | [x] | `RegexReplacementRule`，可忽略大小写 |
| 空白 / 换行规范化 | [x] | |
| ANSI 清理 | [x] | |
| 移除 reasoning_content | [x] | |
| 丢弃空消息 | [x] | |
| 清洗 `messages` 与 `tools[].description` | [x] | 未知字段保留 |
| 专属属性面板 | [部分] | 布尔开关已接入；正则规则仍用 Config JSON |
| 清洗预览 / diff | [ ] | 无 |
| 坏行跳过与报告 | [ ] | 遇到坏 JSON 直接失败 |
| 规则列表 UI | [ ] | 无 |

### 2.10 SFT Export（训练样本导出）

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| OAI messages + tools JSONL | [x] | 只输出 `messages` + `tools` |
| 流式写临时文件 + 原子替换 | [x] | 取消/异常删除临时文件 |
| 专属属性面板 | [x] | 输出路径 |
| 文件选择器 | [ ] | 手输路径 |
| 覆盖确认 / 追加模式 | [ ] | 总是覆盖目标 |
| 输出预览 / 样本查看 | [ ] | 无 |
| 按数量/比例拆分多文件 | [ ] | 无 |
| DPO / Pretrain 目标格式 | [ ] | 仅 OAI messages |

---

## 三、跨节点与引擎能力

### 3.1 已完成

- [x] 强类型 `BlockConfig` 多态层级、`kind` 判别、默认工厂、深拷贝、类型匹配校验。
- [x] Recipe / Settings 原子持久化服务与 schema 版本。
- [x] Recipe 文件生命周期：新建（空白/示例）、打开、保存、另存为、最近文件、脏标记、关闭确认与重启恢复。
- [x] 端口契约：按 BlockType 生成端口；连接前校验方向、DataType、重复边、单输入与环；非法连接被拦截。
- [x] 校验问题列表（属性面板）与画布节点错误/警告徽标，问题可定位到节点。
- [x] Expression：Fluid 模板引擎（条件/循环/过滤器）、`json` 过滤器、输出 JSON 校验、专属面板与样本预览。
- [x] Validator：JSON Schema 规则、reject 文件（lineNumber/reason/data）、失败率日志、专属面板。
- [x] Sampler：ratio/count 多文件切分、确定性复现、原子写入与专属面板。
- [x] Tool Profile：参数树编辑、JSON 导入导出、配置型执行器、Simulation/LLM 引用复用。
- [x] JSONL 读取、索引缓存、LRU 容量清理、源文件指纹失效。
- [x] DAG 校验：空 Recipe、重名、环、悬空边、孤立节点、重复边、端口归属/方向、DataType 兼容、节点配置告警。
- [x] 执行引擎：拓扑分层、层内串行、fail-fast、下游 Skipped、取消、进度、日志。
- [x] 执行记录 `ExecutionStore` 与 Executions UI（列表、状态过滤、节点表、日志、取消）。
- [x] 真实 provider 客户端、密钥设置/环境变量、超时与指数退避重试。
- [x] 真实沙箱与 Windows 资源限制。
- [x] 模拟四节点流水线、并发、成本、失败样本。
- [x] 通用 Config JSON 编辑器（kind/BlockType 一致性校验）。
- [x] 撤销/重做基础能力：添加、删除、移动、连线、删除边、粘贴（块）。
- [x] 画布基础交互：Block Sheet 拖放、可搜索添加菜单（`~` / 右键）、节点拖动、多选拖动、框选、端口连线、边选择/删除、中键平移、滚轮缩放、右键菜单、Mini-map 渲染、缩放百分比。
- [x] Settings 持久化：缓存目录/上限、网格设置、主题选择、API Key 本机保存。

### 3.2 部分完成

- [部分] 端口坐标仍是 `55 + index * 18` 估算，未按实际渲染位置计算。
- [部分] 校验：已覆盖端口契约、必需输入、单输入、方向/类型、环与配置；缺少错误码、列号与问题分组。
- [部分] 执行：层内串行、节点间输出全量内存物化；无并行、暂停、重试、中断恢复、输出目录摘要。
- [部分] 画布：缩放不以鼠标为中心；Undo/Redo 无菜单/按钮与 `Ctrl+Shift+Z`；复制不携带边；Mini-map 无视口矩形/点击导航。
- [部分] 设置：网格吸附设置未生效；主题设置未应用；内存占用只在构造时读取。

### 3.3 未实现

- [ ] 数据集分页预览、行跳转、数据编辑、搜索/正则替换。
- [ ] CSV / Parquet 读写与格式转换 UI。
- [ ] DPO / Pretrain 格式。
- [ ] 数据校验自动修复、失败行报告。
- [ ] 去重 / 混洗 / 切分。
- [ ] 大文件 1M/10M 性能与内存验收。
- [ ] Headless UI 测试、CI、打包、安装包、LICENSE/Assets。
- [ ] 主题切换真正生效、硬编码颜色资源化。
- [ ] 全局异常处理、滚动日志、崩溃报告。
- [ ] 单实例 / 文件关联 / 命令行打开。
- [ ] 节点尺寸调整、边 hover 动画、标签避让。
- [ ] 应用图标、README 截图。

---

## 四、节点 UI 编辑器覆盖

| 节点 | 专属面板 | 已覆盖字段 | 仍依赖 Config JSON |
| --- | --- | --- | --- |
| Seed | 是 | 路径、文件类型、Index Cache | 无（Streaming 未暴露） |
| LLM | 是 | Provider、Model、温度、Max tokens、超时、重试、Endpoint、Prompt | 无 |
| Expression | 是 | 模板、字段别名、样本 JSON、预览输出 | 无 |
| Validator | 是 | 规则类型、规则/Schema、reject 开关与路径 | Cleanup 正则规则 |
| Sampler | 是 | Count、Ratio、Seed、Splits、输出目录 | 无 |
| Tool Profile | 是 | 工具列表、参数 JSON、参数树、profile JSON 导入导出 | 无 |
| Query Source | 是 | Queries、Mode、Repeat、System Prompt | 无 |
| Simulation | 是 | Provider、模式、模型、轮数、并发、温度、tokens、超时、重试、Endpoint、System/Final、失败样本、成本、沙箱 | Tools、Rules、Script |
| Cleaner | 是（部分） | 5 个布尔规则 | RegexReplacements |
| SFT Export | 是 | 输出路径 | 无 |

---

## 五、下一阶段规划（Round 8–12）

### Round 8 — Recipe 生命周期与节点契约（P0，已完成 2026-11-16）

**目标**：让“编辑中的 Recipe”真正可保存、可恢复、可交付；修正节点端口契约。

**交付**
1. Recipe 新建 / 打开 / 保存 / 另存为：Avalonia `StorageProvider`、`RecipeStore` 复用、错误提示带路径与 JSON 位置。
2. 最近文件、脏标记、关闭确认、重启后恢复上次打开文件（或明确回到空白）。
3. 把硬编码示例 Recipe 改为“从示例新建”，不再作为唯一启动内容。
4. 按 BlockType 生成端口 schema：
   - Seed / Query Source：仅输出；
   - SftExport：仅输入；
   - Tool Profile：无数据端口；
   - 其余：输入 + 输出。
5. 校验补强：每种节点的必需输入/输出、单输入约束、端口类型与节点类型匹配；修正示例 Recipe 端口。
6. 画布节点显示校验错误徽标；属性面板问题列表可定位。
7. 测试：RecipeStore/ViewModel 文件生命周期、端口工厂、端口校验、坏文件加载。

> 完成情况：File 菜单 + 快捷键 + StorageProvider 已接线；最近文件/上次路径写入设置；脏标记与关闭确认可用；启动默认空白并可恢复上次文件；“New from Sample”加载 4 节点示例；端口 schema 与连接拦截已生效；属性面板问题列表与画布徽标已接入；新增端口/连接/文档生命周期测试，测试总数 285（+2 gated）。

**验收**
- 保存后重新加载，节点/边/配置/坐标/schemaVersion 完全一致。
- 关闭有未保存修改时弹确认；取消不丢数据。
- 从 Block Sheet 添加的节点端口正确；错误连线被校验拦截。
- `dotnet build` 0 警告，`dotnet test` 全绿。

### Round 9 — 节点能力补齐（P0/P1，已完成 2026-11-16）

**交付**
1. Expression：替换为真实模板引擎（Fluid），支持条件/循环/过滤器；专属面板 + 变量说明 + 渲染预览 + 语法错误定位；输出必须是合法 JSON。
2. Validator：专属面板；JSON Schema 规则；reject 分支（`RejectOutputPath`）；失败率与失败样本；`RejectOnFailure=false` 时透传。
3. Sampler：`Splits` 真正生效（ratio/count 多文件）；专属面板；确定性测试。
4. Tool Profile：Tool Schema 树形编辑器、JSON 导入/导出、被 Simulation/LLM 复用；MCP 适配留到后续。
5. 每个节点至少覆盖成功/失败/边界路径测试。

**验收**
- Expression 条件/循环/过滤器示例通过；非法模板在编辑期报错。
- Validator 通过/拒绝行数守恒，拒绝样本可查看。
- Sampler 输出行数守恒、同 seed 可复现、切分文件数正确。
- Tool Profile 导出的 JSON 可被 OpenAI 工具格式消费。

> 完成情况：新增 Fluid.Core 与 JsonSchema.Net；四个节点均接入专属面板；新增 Expression/Validator/Sampler/Tool Profile 测试，测试总数 311（+2 gated）。多输出端口、Validator 代码规则/超时、LLM 请求侧 tool definitions 与 MCP 留到后续。

### Round 10 — 数据工作台 A：预览与格式（P1）

**交付**
1. JSONL 分页预览表格（虚拟化），行号跳转、只加载当前页。
2. Seed 支持 CSV / Parquet 读取；DatasetWriter 支持 CSV / Parquet 写出；统一 `IDatasetReader/Writer`。
3. 格式转换服务接线到 UI：OpenAI / ShareGPT / Alpaca，批量转换、进度、失败行 `.errors.jsonl`。
4. DPO / Pretrain 格式模型与转换。
5. 1M 行 JSONL 打开/预览/转换性能验收，内存 ≤ 300MB。

**验收**
- 1M 行文件打开与分页跳转满足基线；不一次性加载全文件。
- 任意支持格式转换后行数一致，失败行带行号与原因。
- CSV/Parquet 往返内容一致。

### Round 11 — 数据工作台 B：质量与治理（P1）

**交付**
1. 校验问题列表（级别/行号/路径/消息/跳转）与自动修复预览。
2. 搜索 / 正则替换（当前页、全文件、匹配预览、取消）。
3. 去重（哈希 + SQLite/外排）、混洗（固定 seed）、按比例/份数切分。
4. 数据集统计：角色分布、长度分布、空样本、重复率、工具调用数。
5. 大数据坏行/超长行/非法 UTF-8 fixtures 与错误上限。

**验收**
- 去重/混洗/切分行数守恒，同 seed 可复现。
- 自动修复只改目标文件，原文件不变。
- 1M/10M 行处理不崩溃，内存与耗时记录在案。

### Round 12 — 画布、执行体验与发布工程化（P1/P2）

**交付**
1. 画布：网格背景、网格吸附（含 Alt 临时关闭）、鼠标中心缩放、Fit to View、自动布局、Mini-map 视口矩形与点击导航、双击空白添加、复制携带内部连线、内联重命名、可拖动分栏、真实端口坐标、节点尺寸手柄、边 hover/动画。
2. 执行：层内并行、暂停/重试、节点间流式或溢写磁盘、输出目录/命名/摘要、中断恢复与临时文件清理。
3. 工程化：Headless UI 测试、1M/10M 基准、CI、打包/单文件/安装包、主题切换、滚动日志/全局异常、LICENSE/Assets/README 截图。

**验收**
- 画布操作满足手工脚本；500 节点拖动/缩放单帧 < 100ms。
- 执行取消 1s 内停止且无半成品输出；并行层行为可测试。
- 干净环境可安装启动，CI 全绿。

---

## 六、建议的下一步

Round 9 已完成，建议进入 **Round 10（数据工作台 A）**，按以下顺序落地：

1. **JSONL 分页预览**：虚拟化表格、行号跳转、只加载当前页，避免一次性读入大文件。
2. **CSV / Parquet**：统一 `IDatasetReader/Writer`，Seed 读取与 SftExport/DatasetWriter 写出。
3. **格式转换 UI**：OpenAI / ShareGPT / Alpaca 批量转换、进度、失败行 `.errors.jsonl`。
4. **DPO / Pretrain** 格式模型与转换。
5. **1M 行验收**：打开/预览/转换的内存与耗时基线（≤ 300MB）。

理由：节点语义与端口契约已稳定；数据工作台是当前最大的用户可见缺口，且会复用已有的 JSONL/index cache 与格式转换服务。

---

## 七、验证命令

```powershell
dotnet build LMTrainingDataStudio2/LMTrainingDataStudio2.slnx
dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx
```

真实 provider 冒烟（可选，Key 只放环境变量）：

```powershell
$env:LMTDS_PROVIDER_SMOKE_KEY = "<your-api-key>"
$env:LMTDS_PROVIDER_SMOKE_BASE_URL = "https://api.openai.com/v1"
$env:LMTDS_PROVIDER_SMOKE_MODEL = "gpt-4o-mini"
dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx --filter ProviderSmokeTests
```
