# PLAN / TODO — 实施计划与验收标准

> 最后更新：2026-11-17（Round 16 发布与体验完成）
> 当前基线：.NET 9 / Avalonia 11.3 / NUnit 4；505 常规测试 + 7 gated；构建 0 警告（分析器开启）
> 工作方式：小步提交，每个 Phase 独立验收；开发期允许 break change，不保留兼容层。
>
> 完整“已做/未做”清单与 10 个节点的能力矩阵见 [STATUS.md](STATUS.md)。本文保留分阶段计划、验收标准与详细缺口池。

---

## 〇、文档同步（2026-11-17）

- 逐项核对 `Services/`、`ViewModels/`、`Views/` 与测试，修正第四节目录中已完成的条目（画布、数据治理、执行控制、UI 面板等）。
- 修正与代码不一致的描述：LLM Prompt 使用 Fluid 模板（不再写“仅 field.path”）；SFT Export 按行透传、不主动裁剪字段；CurrentFilePath 已接线；内存占用仍只采样一次。
- 新增 Round 13–16 规划：执行与 IO 韧性、数据工作台编辑与预览、节点能力收紧、发布与体验。
- 当前验证：`dotnet build` 0 警告；`dotnet test` 480 通过 + 6 个 gated 跳过。

---

## 附六：工作流预设、节点说明与 UI 本地化（2026-11-17）

### 6.1 本轮做了什么

- [x] 本地化框架：`LocalizationService` + `{loc:Loc key}` 标记扩展，中英文资源内嵌于程序集，语言切换时所有本地化绑定原地刷新。
- [x] 语言设置：`AppSettings.Language` 支持 `system` / `zh-CN` / `en-US`，设置面板提供语言选择并持久化；`SettingsStore` 对非法语言回退并给出警告。
- [x] 静态 UI 本地化：MainWindow、属性面板、节点库、Data 工作台、Executions、消息对话框的标签、按钮、提示与水印全部资源化（约 230 个键）。
- [x] 节点说明：新增 `BlockCatalog`，为 10 类节点提供名称、一句话简介、详细说明与端口摘要；属性面板显示 Node Guide，画布节点悬停显示简介。
- [x] 工作流预设：新增 `RecipePresetCatalog` 与 6 个基础流水线（LLM 批量生成、OAI 轨迹清洗、字段映射、校验与切分、模拟 Agent 轨迹、真实工具调用），支持 File → New from Preset 与节点库点击加载。
- [x] 自定义预设：`RecipePresetStore` 按预设单文件持久化到 `%APPDATA%\LMTrainingDataStudio\presets`；支持保存当前 Recipe、导入外部预设（分配新本地 ID）与删除；原子写入、坏文件跳过并告警。
- [x] 自定义预设 UI：新增保存预设对话框、File 菜单保存/导入入口、节点库自定义标记与删除按钮。
- [x] 预设加载遵守脏文档确认流程，加载后标记未保存，并复用现有 Recipe 校验。
- [x] 新增 34 个测试：本地化服务/节点目录/预设目录、预设加载、设置语言往返与回退、Headless 语言切换与节点说明、自定义预设存储/导入/删除/快照隔离。

### 6.2 验证结果

| 项目 | 命令 | 结果 |
| --- | --- | --- |
| 构建 | `dotnet build LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 成功，0 警告，0 错误 |
| 测试 | `dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 420 通过，0 失败，4 跳过（2 provider + 2 性能 gated） |
| 本地化绑定 | `dotnet test --filter HeadlessUiTests` | 通过：中英文切换后 BlocksHeader 与节点库名称即时更新 |
| 预设校验 | `RecipePresetCatalogTests` | 6 个内置预设均为合法 DAG，无校验错误 |
| 自定义预设 | `RecipePresetStoreTests` / `MainWindowViewModelDocumentTests` | 保存/导入/删除/快照隔离/坏文件告警全部通过 |

### 6.3 本轮未做

- 运行日志、执行状态、数据集统计摘要仍为英文技术文本；后续可逐步资源化。
- Executions 状态筛选与运行状态值仍使用内部英文枚举值，尚未做本地化显示。
- 自定义预设暂不支持从 UI 导出到任意路径（当前保存在本机预设目录，可直接复制文件分享）。

---

## 附七：Round 13 执行与 IO 韧性（2026-11-17）

### 7.1 本轮做了什么

- [x] 节点间溢写：新增 `RowSpillBuffer`，每个节点输出先驻留内存，超过 `SpillThresholdRows` 后溢写磁盘（保留原始行号），下游按流读取；`SpillThresholdRows` 与 `RunTempDirectory` 可在设置面板配置并持久化。
- [x] 临时目录清理：每次运行使用独立溢写目录，成功/失败/取消均在 `finally` 中释放缓冲并删除溢写目录。
- [x] 节点级检查点：运行输出目录新增 `nodes/`，节点成功后原子保存其输出；失败/取消运行保留检查点，成功运行后自动清理。
- [x] 中断恢复：`RecipeRunner.RunAsync(..., resumeFrom)` 复用已完成节点输出，从首个未完成节点继续；Executions 视图新增“恢复执行”，仅对失败/取消且存在输出目录的运行可用。
- [x] 取消硬化：溢写、检查点与执行链路均响应取消；测试断言取消后 1 秒内停止且溢写目录为空。
- [x] SFT Export 收尾：属性面板新增输出文件选择器、运行前覆盖确认、可选顶层字段白名单（如 `messages, tools`）、写出行数与首行预览日志。
- [x] 新增 17 个测试：溢写缓冲 5、Runner 溢写/恢复/重试清理/取消 5、SFT 字段/预览/非对象 3、设置往返与回退 2、Executions 恢复可用性 1、Headless SFT 编辑器 1。

### 7.2 验证结果

| 项目 | 命令 | 结果 |
| --- | --- | --- |
| 构建 | `dotnet build LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 成功，0 警告，0 错误 |
| 测试 | `dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 437 通过，0 失败，5 跳过（2 provider + 3 性能 gated） |
| 取消验收 | `RunAsync_Cancel_StopsWithinOneSecondAndCleansSpillFiles` | 取消后 <1s 返回，溢写目录为空 |
| 恢复一致性 | `RunAsync_FailedRun_KeepsCheckpointsAndResumeReusesCompletedBlock` | 已完成节点不重跑，恢复后行数与内容一致 |
| 1M 行链路 | `LMTDS_PERF_TESTS=1 dotnet test --filter PipelinePerformanceTests` | Seed → Expression → SFT Export 17.2s，托管内存增量 1MB，输出 1,000,000 行 |

### 7.3 本轮未做

- 恢复结果与不中断运行的逐字节/哈希比对（当前验证行数、内容与节点复用次数）。
- 溢写阈值按行数计，未提供按字节阈值；检查点磁盘占用无自动配额。
- 取消 1s 断言依赖本机调度，CI 上未单独设置性能门禁。

---

## 附八：Round 14 数据工作台编辑与预览（2026-11-17）

### 8.1 本轮做了什么

- [x] 编辑会话：新增 `DatasetEditSession`，按源行号记录替换/删除/插入（插入行有稳定 ID），支持跨页保留、撤销/重做（快照栈，最多 200 步）与整体放弃。
- [x] 流式保存：新增 `DatasetEditService`，先校验所有编辑行 JSON，再按块读取源文件、应用编辑并写临时文件，最后原子替换；取消/失败不产生半成品，未修改 JSONL 行文本原样复制。
- [x] 保存摘要：保存前生成替换/删除/插入计数与最多 5 条 before/after 示例；覆盖源文件与另存为均先确认。
- [x] Data 工作台编辑 UI：插入上方/下方、复制行、删除/恢复、撤销/重做、丢弃、编辑另存为、覆盖源文件；行显示 NEW/EDIT/DEL 标记，删除行删除线显示。
- [x] 字段级单元格编辑：选中行默认进入 Fields 模式，按顶层字段编辑 JSON 值，支持新增/删除字段；非对象行回退 Raw JSON 模式；嵌套对象/数组按 JSON 文本编辑。
- [x] 搜索高亮：`HighlightMatcher` 支持字面量与正则（100ms 超时、非法模式降级），`TextHighlightBehavior` 用 Inlines 渲染行内高亮；搜索框查询即时映射到表格行。
- [x] 校验问题分组与筛选：级别/错误码筛选 + 按级别/按错误码分组树，组头显示计数，叶子双击跳行；筛选与分组可组合。
- [x] 统一样本预览：`SamplePreviewService` 读取 JSONL/CSV/Parquet 前 N 行，或对 JSONL 应用 Cleaner 规则生成前后 diff；`SamplePreviewWindow` 支持文件/双栏 diff 两种模式。
- [x] 预览入口：SFT Export 输出、Validator 拒绝行、Simulation 失败样本接入文件预览；Cleaner 面板选择样本文件后查看清洗 diff；预览窗口可跳转数据工作台打开该文件。
- [x] JSONL 写出统一使用 LF 换行，保证未修改行在 LF 源上逐字节一致。
- [x] 新增 43 个测试：编辑服务 9、工作台编辑/字段编辑/撤销/覆盖/筛选/分组 12、字段读写 8、高亮匹配 8、样本预览 5、Headless 高亮渲染 1；另新增 1 个 gated 1M 行编辑保存基线。

### 8.2 验证结果

| 项目 | 命令 | 结果 |
| --- | --- | --- |
| 构建 | `dotnet build LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 成功，0 警告，0 错误 |
| 测试 | `dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 480 通过，0 失败，6 跳过（2 provider + 4 性能 gated） |
| Headless UI | `dotnet test --filter HeadlessUiTests` | 7 通过，含行内高亮 Inlines 渲染断言 |
| 编辑保存 | `DatasetEditServiceTests` | 行数守恒、未修改行保持、无效 JSON 先失败、取消不落盘、覆盖源文件、撤销重做还原 |
| 1M 行编辑保存 | `LMTDS_PERF_TESTS=1 dotnet test --filter EditSave_LargeInput` | 1000 替换/100 删除/100 插入，55.6s，托管内存增量 11MB，写出 1,000,000 行 |
| 启动冒烟 | `LMTrainingDataStudio2.exe`（Debug，8s） | 进程持续运行，无异常输出 |

### 8.3 本轮未做

- 编辑只支持保存为源格式；跨格式保存需先走批量转换。
- 字段编辑覆盖顶层字段；`messages` 等嵌套结构仍以整段 JSON 文本编辑，未做消息/角色级表单。
- CSV/Parquet 保存会重写整表（内容等价但字节不保证一致）；逐字节一致仅对 LF JSONL 保证。
- 搜索高亮不区分“当前匹配项”，也未实现表格内上一处/下一处导航。
- 预览窗口固定加载前 50 行，未提供分页或全文搜索。
- 数据工作台编辑 UI 未做逐项人工点击验收（Headless 加载、ViewModel 单测与启动冒烟通过）。

---

## 附九：Round 15 节点能力收紧（2026-11-17）

### 9.1 本轮做了什么

- [x] 多输出端口：`BlockExecutionResult` 支持附加输出流；Runner 按端口名缓冲、持久化检查点与恢复；Validator 提供 `pass`/`reject` 端口，Sampler 按 splits 动态生成 `train`/`valid` 等端口；旧单端口 Recipe 仍按主端口回落。
- [x] LLM：请求侧携带 Tool Profile tools、多轮 `messages`（`UseRowMessages`）与 `response_format` JSON Schema；`MaxConcurrency` 有界并行且输出保序；累计 token 与按单价估算成本写日志；失败行可跳过并在 `FailureOutputPath` 原子写入 `{lineNumber,error,data}`；结构化输出非法 JSON 视为失败样本。
- [x] Validator：支持多个规则（`Rules`）与 `MatchAllRules`/任意匹配、`expression`/`schema`/`regex`/`script`（Fluid 渲染 true/false）四种规则、单行超时 `TimeoutMs`、规则取反；reject 端口输出原始行，reject 文件保留失败原因；属性面板可增删附加规则。
- [x] Sampler：splits 生成独立输出端口；`StratifyField` 在 ratio/splits 模式下按顶层字段分组独立随机流；属性面板显示切分比例/预计行数统计预览。
- [x] Simulation：属性面板新增 Tools / Harness Rules / Script 回合可视化编辑器（增删行、行为、参数 JSON、响应模板、失败/延迟/超时、允许工具、重试与中止、脚本回合与工具调用）；新增 `RequestsPerMinute` 限速、OpenAI Chat SSE 流式 delta 累积、内置价格预设下拉。
- [x] Query Source：查询文本支持 Fluid 模板变量（含 `index`）；新增外部 JSONL/CSV/Parquet query 源、`QueryField`/`SystemField` 映射，源行字段保留并写入 `query`/`system`。
- [x] 新增 14 个测试：LLM 请求体/多轮/并发/失败样本/结构化输出 4、Validator 多规则/双端口/超时 2、Sampler 多端口/分层/统计预览 3、Simulation 编辑器/流式 delta 2、Query Source 模板与外部源 1、配置序列化与校验 2。

### 9.2 验证结果

| 项目 | 命令 | 结果 |
| --- | --- | --- |
| 构建 | `dotnet build LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 成功，0 警告，0 错误 |
| 测试 | `dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 495 通过，0 失败，6 跳过（2 provider + 4 性能 gated） |
| Round 15 用例 | `dotnet test --filter Round15NodeCapabilityTests` | 14 通过 |
| 启动冒烟 | `LMTrainingDataStudio2.exe`（Debug，8s） | 进程持续运行，无异常输出 |

### 9.3 本轮未做

- LLM tool_calls 只记录到输出，不执行工具回填（多轮工具执行仍由 Simulation 节点承担）；Anthropic 不发送结构化输出约束。
- Validator `script` 是 Fluid 布尔表达式，不是任意 C#/JS 脚本；超时只停止等待，不强制终止底层纯 CPU 渲染。
- Sampler 分层对 count 模式不生效（count 模式按全局顺序填充）；端口动态变化时指向被删除端口的旧连线只在下次校验时报错。
- Simulation 流式 delta 仅支持 OpenAI Chat；限速按 query 启动间隔近似，不是严格 token bucket。
- 属性面板未逐项人工点击验收（XAML 编译、Headless UI、ViewModel 单测与启动冒烟通过）。

---

## 附十：Round 16 发布与体验（2026-11-17）

### 10.1 本轮做了什么

- [x] 画布 polish：节点右下角尺寸手柄（宽高持久化到 Recipe、`ResizeBlocksCommand` 可撤销）、边 hover 流动虚线动画与粗细过渡、边标签避让节点与其他标签、Edit 菜单 Undo/Redo 与 `Ctrl+Shift+Z`。
- [x] 属性修改撤销：名称/显示名修改与所有节点配置编辑进入同一 `CommandHistory`；连续编辑同一节点配置在 1.5s 窗口内自动合并为一条撤销记录。
- [x] 完整浅色主题：扩展 `Themes/Colors.axaml` 的 Dark/Light 语义资源（表面、文本、语义色、节点、端口、边标签、Mini-map、选择框、Modal、阴影），删除 Views 下全部硬编码颜色字面量；主题切换使用 `DynamicResource` 即时生效，画布在主题变体变化时重绘。
- [x] 状态栏内存占用每 2 秒刷新，窗口关闭时停止定时器。
- [x] 单实例：命名互斥体 + 命名管道；后启动实例把 Recipe 路径转发给主实例，主实例在 UI 线程打开（遵守脏文档确认）。
- [x] 命令行打开：`LMTrainingDataStudio2.exe <recipe.json>`；启动时在恢复上次文件后打开命令行文件。
- [x] 崩溃报告与诊断：未处理异常自动写入 `%LOCALAPPDATA%\LMTrainingDataStudio\crashes`；Help → Export Diagnostics 导出 zip（日志、脱敏设置、最近运行摘要），`RedactSettingsForDiagnostics` 移除 API Key。
- [x] 发布：MSIX `AppxManifest.xml`（`.recipe` 文件关联、`lmtds.exe` 别名）与 `scripts/package-msix.ps1`（自动生成图标、调用 makeappx、可选签名）；Inno Setup 脚本 `packaging/inno/LMTrainingDataStudio.iss` 与 `scripts/package-inno.ps1`（HKCU 关联 + `.json` OpenWithProgids）。
- [x] 示例 Recipe：`examples/` 下 6 个可直接打开的 `*.recipe.json`；`ExampleRecipeTests` 校验全部示例合法，显式测试可重新生成。
- [x] README 截图：`docs/screenshots/editor-dark.png` 与 `editor-light.png`。
- [x] 基线入口：10M 行 Seed → Expression → SFT Export（`LMTDS_PERF_PIPELINE_ROWS=10000000`，实测 154.0s、托管内存增量 1MB）、CSV/Parquet 大规模分页与往返（20 万行，JSONL→CSV 14.3s、→Parquet 13.1s、最慢分页 18.2ms、内存增量 17MB）。
- [x] 工程质量：根目录 `.editorconfig`、`Directory.Build.props` 启用 .NET 分析器（`latest-recommended`）、`TreatWarningsAsErrors`；修复 CA1826/CA1854/CA1716/CA2020 等问题。
- [x] 新增测试：配置编辑合并撤销、尺寸命令、显示名重命名、边标签避让、诊断包与脱敏、内存刷新、Views 无硬编码颜色、Dark/Light 资源完整性、主题切换动态刷新、示例 Recipe 校验。

### 10.2 验证结果

| 项目 | 命令 | 结果 |
| --- | --- | --- |
| 构建 | `dotnet build LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 成功，0 警告，0 错误（分析器开启） |
| 测试 | `dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 505 通过，0 失败，7 跳过（2 provider + 5 性能 gated） |
| Round 16 用例 | `dotnet test --filter Round16ExperienceTests` | 9 通过 |
| 示例 Recipe | `ExampleRecipeTests.BundledExamples_LoadAndValidate` | 6 个示例均为合法 DAG |
| 10M 行链路 | `LMTDS_PERF_TESTS=1 LMTDS_PERF_PIPELINE_ROWS=10000000 dotnet test --filter PipelinePerformanceTests` | 通过：生成 5.0s、运行 154.0s、托管内存增量 1MB、输出 10,000,000 行 |
| CSV/Parquet 基线 | `LMTDS_PERF_TESTS=1 dotnet test --filter CsvAndParquet_LargeInput` | 通过：20 万行 @256B，JSONL→CSV 14.3s、JSONL→Parquet 13.1s、最慢 100 行分页 18.2ms、托管内存增量 17MB |
| 启动冒烟 | `LMTrainingDataStudio2.exe`（Debug） | 进程持续运行，无异常输出 |

### 10.3 本轮未做

- MSIX/Inno 安装包未在本机实际安装验证（脚本与清单已提供；本机未安装 Windows SDK/Inno Setup）。
- 单实例管道未做双进程端到端自动化测试（管道客户端在无服务端时返回 false 的行为已覆盖）。
- 画布尺寸/缩放逐帧计时未做 UI 帧级实测（布局与增量渲染测试仍通过）。
- 截图由 Avalonia Headless + Skia 一次性生成，未纳入常规测试（避免给测试项目引入 Skia 字体依赖）。
- 边动画为高亮边流动虚线，不是路径几何过渡；标签避让为贪心候选位置，不保证全局最优。
- 分析器规则采用 `latest-recommended` 并保留一份 `NoWarn` 清单（CA1001/CA1863/CA1068 等），未逐条重构。

---

## 一、Round 12 进展（历史）

### 1.1 本轮做了什么

- [x] 画布：网格背景（随平移/缩放）、网格吸附（Alt 临时关闭）、鼠标中心缩放、Fit to View、拓扑自动布局、Mini-map 视口矩形与点击导航。
- [x] 画布交互：双击空白添加节点、双击节点内联重命名（可撤销）、复制携带内部连线、真实渲染端口坐标、边 hover 高亮、拖拽增量更新（不重建全部节点）。
- [x] 执行：层内受控并行（`MaxParallelism`）、暂停/恢复、节点重试（`NodeRetryCount`）、每次运行输出目录与 `run-summary.json`；Executions UI 显示输出目录。
- [x] 工程化：滚动文件日志与全局异常处理（AppDomain/TaskScheduler/UI 线程）、主题变体（Dark/Light 资源字典）、GitHub Actions CI、win-x64 单文件发布脚本、Avalonia Headless UI 测试、MIT LICENSE、应用图标。
- [x] 新增 18 个测试：并行/暂停/重试/输出摘要 4、视口/布局 7、日志 2、Headless UI 3、设置 2。

### 1.2 验证结果

| 项目 | 命令 | 结果 |
| --- | --- | --- |
| 构建 | `dotnet build LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 成功，0 警告，0 错误 |
| 测试 | `dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 386 通过，0 失败，4 跳过（2 provider + 2 性能 gated） |
| Headless UI | `dotnet test --filter HeadlessUiTests` | 3 通过：主窗口加载、画布添加节点、主题切换 |
| 启动冒烟 | `LMTrainingDataStudio2.exe`（Debug，8s） | 持续运行，无异常输出 |
| 发布 | `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true` | 成功，单文件 `LMTrainingDataStudio2.exe`（约 49 MB） |
| 500 节点布局 | `CanvasInteractionTests.Layout_500Blocks_CompletesQuickly` | 通过，布局耗时断言 < 100ms |

### 1.3 本轮改动文件

```text
PLAN/TODO.md
PLAN/STATUS.md
README.md
LICENSE
.github/workflows/ci.yml
scripts/publish-win-x64.ps1
scripts/generate-icon.ps1
LMTrainingDataStudio2/LMTrainingDataStudio2/Assets/app-icon.png
LMTrainingDataStudio2/LMTrainingDataStudio2/Controls/GridBackground.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/CanvasViewport.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/RecipeLayoutService.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/ExecutionPauseController.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/RecipeRunOptions.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/RecipeRunner.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/AppLog.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/ThemeManager.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/SettingsStore.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Models/AppSettings.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Commands/RecipeCommands.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/MainWindowViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Views/RecipeCanvasView.axaml
LMTrainingDataStudio2/LMTrainingDataStudio2/Views/RecipeCanvasView.axaml.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Views/MainWindow.axaml
LMTrainingDataStudio2/LMTrainingDataStudio2/Views/MainWindow.axaml.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Views/ExecutionsView.axaml
LMTrainingDataStudio2/LMTrainingDataStudio2/App.axaml.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Program.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Themes/Colors.axaml
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/RecipeRunnerTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/CanvasInteractionTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/AppLogTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/HeadlessUiTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/TestAppBuilder.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/SettingsStoreTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/LMTrainingDataStudio2.Tests.csproj
```

### 1.4 技术决策与取舍

1. **拖拽增量更新。** 结构变化才重建节点；拖拽时只更新 `Canvas.Left/Top` 与已有边的几何数据，避免 500 节点时每帧重建视觉树。
2. **端口坐标取实测值。** 首次布局后读取端口椭圆相对节点的位置并缓存，边与端口命中测试使用真实坐标；布局未完成时退回估算。
3. **视口数学独立成纯类。** `CanvasViewport` 负责吸附、锚点缩放、Fit 与坐标换算，便于单测；视图只负责把结果应用到变换与网格。
4. **层内并行 + 首个失败取消同层。** 使用带最大并发的 `Task.WhenAll` 与 linked CTS；失败块取消同层兄弟，下游保持 Skipped；日志加锁、计数用 Interlocked。
5. **暂停是协作式门控。** 暂停只阻止新块启动，已运行块自然完成；恢复后继续。
6. **运行摘要落盘。** 每次运行在 `RunOutputDirectory/<runId>` 下写 `run-summary.json`，即使取消/失败也写，便于排查。
7. **主题资源按变体拆分。** Colors.axaml 改为 ThemeDictionaries；仍有个别视图硬编码深色，完整主题化留待后续。

### 1.5 本轮未做

- 节点尺寸手柄、边过渡动画（当前仅 hover 高亮）、自动布局的间距/分组选项。
- 节点间流式或溢写磁盘、执行中断后的节点级恢复。
- 10M 行实测（保留 `LMTDS_PERF_ROWS=10000000` 入口）、MSIX/安装包（当前为单文件 zip）、README 截图。
- 完整浅色主题（部分视图仍有硬编码深色）。
- 拖动/缩放单帧 <100ms 的 UI 帧级实测（已通过 500 节点布局测试与增量渲染实现，未做逐帧计时）。

---

## 附五：Round 11 历史进展（2026-11-17）

### 1.1 本轮做了什么

- [x] 质量校验：`DatasetValidationService.ValidateAsync` 支持 JSONL 字节级扫描与 CSV/Parquet 行校验；问题包含级别、行号、JSON 路径与错误码；覆盖语法、非对象、空行、消息结构、未知格式、超长行、非法 UTF-8；达到 `MaxIssues` 后继续计数。
- [x] 自动修复：JSONL 修复预览（可修复/需人工）与“写修复副本”；只写目标文件，原文件字节不变；非法 UTF-8 与超长行原样复制。
- [x] 搜索/正则替换：当前页/全文件、大小写、字面量/正则、分组替换、匹配预览（行号/次数/片段）、取消、原子写目标文件、正则超时保护。
- [x] 去重/混洗/切分：SHA256 + SQLite 去重；固定 seed 的确定性混洗（SQLite 外排 + 批量随机读）；`name:ratio` / `name:count` 多文件切分；CSV/JSONL 输出保持源类型；行数守恒。
- [x] 数据集统计：角色分布、长度分桶、空样本、空内容消息、重复率、工具调用/工具消息、格式分布、坏行数；SQLite 去重集合保持内存有界。
- [x] Data 视图右侧面板改为 Convert / Search / Transform / Stats / Validate 五个标签页；校验问题与搜索结果双击跳行；文件/目录选择器。
- [x] 坏行 fixtures 与错误上限测试：截断 JSON、超长行、非法 UTF-8、错误上限、BOM。
- [x] 新增 37 个常规测试 + 1 个 gated 大规模质量操作基线（总计 368 通过 + 4 gated）。

### 1.2 验证结果

| 项目 | 命令 | 结果 |
| --- | --- | --- |
| 构建 | `dotnet build LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 成功，0 警告，0 错误 |
| 测试 | `dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 368 通过，0 失败，4 跳过（2 真实 provider 冒烟 + 2 性能 gated） |
| 启动冒烟 | `LMTrainingDataStudio2.exe`（Debug，直接启动 8s） | 进程持续运行，无异常输出 |
| 1M 质量基线 | `LMTDS_PERF_TESTS=1 dotnet test --filter QualityOperations_LargeInput` | 通过：生成 0.2s、去重+混洗+切分 124.2s（50 万保留/50 万重复）、统计 63.6s、托管内存增量 2MB |

1M 行（256B/行）质量操作实测：生成 0.2s；去重 + 混洗 + `train:0.8/valid:0.2` 切分 124.2s（保留 500,000 行、去重 500,000 行）；统计 63.6s；托管内存增量 2MB（≤300MB）。10M 行可通过 `LMTDS_PERF_ROWS=10000000` 运行；本轮未执行 10M（预计约 25–35 分钟、数 GB 临时文件）。

本轮新增/扩展测试：

| 测试类 | 数量 | 覆盖内容 |
| --- | --- | --- |
| `DatasetQualityValidationTests` | 9 | 语法/非对象/空行/路径/错误码、非法 UTF-8、超长行、错误上限、结构告警、修复预览、修复写副本、BOM |
| `DatasetSearchReplaceTests` | 9 | 字面量大小写、当前页范围、正则匹配/截断、分组替换、取消不发布、CSV、空查询/非法正则 |
| `DatasetTransformTests` | 10 | 去重、同 seed 混洗可复现、比例/份数切分、组合操作、CSV 输出、路径冲突、非法切分、取消 |
| `DatasetStatisticsTests` | 4 | 角色/长度/空样本/重复率/工具调用/格式、关闭重复计算、CSV、长度边界 |
| `DataWorkbenchViewModelTests` 扩展 | 5 | 校验与问题跳行、修复命令、搜索预览/替换、去重运行、统计运行 |
| `DatasetPerformanceTests`（gated） | 1 | 1M（可配置）行去重/混洗/切分与统计基线 |

### 1.3 本轮改动文件

```text
PLAN/TODO.md
PLAN/STATUS.md
README.md
LMTrainingDataStudio2/LMTrainingDataStudio2/Models/ValidationResult.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/DatasetQualityModels.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/DatasetValidationService.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Datasets/JsonlRawLineReader.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Datasets/DatasetSearchReplaceService.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Datasets/DatasetTransformService.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Datasets/DatasetStatisticsService.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/DataWorkbenchViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/DatasetValidationViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/DatasetSearchReplaceViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/DatasetTransformViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/DatasetStatisticsViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/MainWindowViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Views/DataWorkbenchView.axaml
LMTrainingDataStudio2/LMTrainingDataStudio2/Views/DataWorkbenchView.axaml.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/DatasetQualityValidationTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/DatasetSearchReplaceTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/DatasetTransformTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/DatasetStatisticsTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/DataWorkbenchViewModelTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/DatasetPerformanceTests.cs
```

### 1.4 技术决策与取舍

1. **JSONL 质量校验按字节扫描。**
   - 严格 UTF-8 解码可准确识别非法字节；超长行超过上限即丢弃缓冲并继续扫描，避免病态输入耗尽内存；BOM 在首行剥离。CSV/Parquet 复用统一 `IDatasetReader`，按行校验 JSON 与聊天结构。
2. **自动修复写副本而非原地修改。**
   - 原文件字节保持不变；可修复行重编码，其余行（含非法 UTF-8、超长行）原样复制；目标文件临时文件 + 原子替换。
3. **去重与混洗使用临时 SQLite，内存有界。**
   - 去重表只存 SHA256；混洗表存 `(seq, key, src)`，按 seed 哈希排序后批量读取源行并重排；10M 行时磁盘承担排序与索引，托管内存不随行数增长。
4. **切分复用 Sampler 语义并保持源文件类型。**
   - `name:ratio` 与 `name:count` 行为与 Sampler 节点一致；CSV/Parquet 源输出同名同类型文件；取消或异常不发布目标文件。
5. **统计口径明确。**
   - 坏行计入 0 长度桶，保证长度桶总数等于总行数；重复率默认计算、可关闭；角色别名与格式检测复用现有转换规则。
6. **搜索替换正则限时。**
   - 单行正则匹配 2s 超时，超时按行报错而不是挂起；替换写入独立目标文件，支持当前页/全文件范围。

### 1.5 本轮未做

- CSV / Parquet 的 1M 质量操作基线（本轮 1M 基线为 JSONL）。
- 10M 实测（已提供 `LMTDS_PERF_ROWS=10000000` 入口，本轮未执行）。
- 数据工作台 UI 未逐项人工点击验证（XAML 编译、启动冒烟与 ViewModel 单测通过）。

---

## 附四：Round 10 历史进展（2026-11-17）

### 1.1 本轮做了什么

- [x] 统一数据集读写：新增 `IDatasetReader` / `IDatasetWriter` 与 `DatasetIO` 工厂；JSONL 读取器接入接口；CSV 读取使用 RFC 4180 记录边界索引，Parquet 按 row group 随机读取，均只加载当前页。
- [x] CSV / Parquet 写出：临时文件 + 原子替换；CSV 列取自首行，嵌套值存 JSON 文本；Parquet 由首行推断 schema，嵌套值存字符串并写入 `lmtds.json_fields` 元数据，读取时还原为 JSON。
- [x] Seed 节点支持 JSONL / CSV / Parquet；`UseIndexCache=false` 时 JSONL 仍走纯流式分支；删除“仅 JSONL 可执行”的旧校验。
- [x] 数据工作台（Data 视图）：打开 JSONL/CSV/Parquet、100–1000 行分页、首/上/下/末页、按行号跳转、虚拟化列表与 JSON 预览；分页请求带版本号，避免快速翻页串页。
- [x] 批量格式转换：流式分页读取、进度、取消、原子目标文件；失败行写入同目录 `<name>.errors.jsonl`（`lineNumber` / `reason` / `data`），成功时清理旧错误文件；取消不落半成品。
- [x] DPO / Pretrain：新增格式模型、检测与规范化；支持 OpenAI ↔ ShareGPT ↔ Alpaca、chat → Pretrain、DPO → DPO、Pretrain → Pretrain，跨族不支持时明确报错。
- [x] 新增 21 个测试：数据 IO/转换 12、Data 工作台 7、Seed CSV/Parquet 2；另有 1 个 gated 1M 行性能测试。

### 1.2 验证结果

| 项目 | 命令 | 结果 |
| --- | --- | --- |
| 构建 | `dotnet build LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 成功，0 警告，0 错误 |
| 测试 | `dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 331 通过，0 失败，3 跳过（2 真实 provider 冒烟 + 1 性能） |
| 启动冒烟 | `LMTrainingDataStudio2.exe`（Debug，直接启动） | 进程持续运行 10s，无异常输出 |
| 1M 行基线 | `LMTDS_PERF_TESTS=1 LMTDS_PERF_ROW_BYTES=1024 dotnet test --filter DatasetPerformanceTests` | 通过：索引 8.8s、最慢 100 行分页 10.9ms、转换 1M 行 66.9s、内存增量 14MB |

1M 行基线（平均 1KB/行，1,000,000 行）：生成 0.8s；索引 8.8s（≤10s）；任意 100 行分页最慢 10.9ms（≤100ms）；JSONL→JSONL 转换 66.9s；内存增量 14MB（≤300MB）。

本轮新增/扩展测试：

| 测试类 | 数量 | 覆盖内容 |
| --- | --- | --- |
| `DatasetIOTests` | 12 | CSV 引号/换行/嵌套 JSON、随机分页；CSV 往返；Parquet 标量/嵌套/跨 row group 往返；JSONL→CSV 失败行；JSONL↔Parquet；转换矩阵与错误；取消清理；扩展名检测 |
| `DataWorkbenchViewModelTests` | 7 | 打开/分页/跳转边界、CSV 表头映射、JSONL→CSV 转换、坏行列表、不支持组合、目标路径建议 |
| `ExecutorTests` 扩展 | 2 | Seed 读取 CSV / Parquet |
| `DatasetPerformanceTests`（gated） | 1 | 1M 行索引、分页、转换与内存基线 |

### 1.3 本轮改动文件

```text
PLAN/TODO.md
PLAN/STATUS.md
README.md
LMTrainingDataStudio2/LMTrainingDataStudio2/Models/DatasetFormat.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Models/DatasetSamples.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/DatasetFormatConverter.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/JsonlDatasetReader.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/RecipeValidationService.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Datasets/DatasetContracts.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Datasets/DatasetIO.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Datasets/JsonlDatasetWriter.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Datasets/CsvDatasetReader.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Datasets/CsvDatasetWriter.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Datasets/ParquetDatasetReader.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Datasets/ParquetDatasetWriter.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Datasets/DatasetConversionService.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Datasets/DatasetBatchConverter.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Executors/SeedBlockExecutor.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/DataWorkbenchViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/MainWindowViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Views/DataWorkbenchView.axaml
LMTrainingDataStudio2/LMTrainingDataStudio2/Views/DataWorkbenchView.axaml.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Views/MainWindow.axaml
LMTrainingDataStudio2/LMTrainingDataStudio2/Views/PropertyPanelView.axaml.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/DatasetIOTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/DataWorkbenchViewModelTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/DatasetPerformanceTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/ExecutorTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/RecipeValidationServiceTests.cs
```

### 1.4 技术决策与取舍

1. **所有数据集统一以“JSON 文本行”为交换格式。**
   - JSONL 天然如此；CSV 每行映射为 `header → value` 对象，Parquet 每行映射为列名对象。预览、转换、Seed 共用同一套读取接口。
2. **CSV 用自建记录边界索引，不依赖全文件重扫。**
   - 扫描一次记录字节区间（处理引号内换行与 `""` 转义），之后任意页直接 seek；嵌套 JSON 值按 `{`/`[` 前缀识别并还原。
3. **Parquet 用 row group 随机读，嵌套字段用 schema 自定义元数据标记。**
   - 首行推断 `bool/int64/double/string` schema，对象/数组存 JSON 字符串并写入 `lmtds.json_fields`，读取时还原，避免结构丢平。
4. **转换错误按行收集，不中断整批。**
   - 失败行写入 `<target>.errors.jsonl`；目标文件仅在全部处理完成后原子替换，取消或异常不破坏已有目标。
5. **不支持的格式组合显式失败。**
   - chat ↔ DPO 需要双答案，DPO/Pretrain → chat 会丢失语义；仅支持 chat 三格式互转、chat → Pretrain、同族规范化。

### 1.5 本轮未做

- 数据编辑、搜索/正则替换、去重/混洗/切分、数据集统计（Round 11）。
- CSV 类型推断（当前所有 CSV 值按字符串读取，JSON-looking 值除外）；Parquet schema 演进与多类型列合并。
- SFT Export 节点直接输出 DPO / Pretrain（本轮只在数据工作台批量转换支持）。
- CSV / Parquet 的 1M 行性能基线（本轮性能验收针对 JSONL）。
- 数据工作台的人工 UI 验收（XAML 编译、启动冒烟与 ViewModel 单测通过；未逐项点击验证）。

---

## 附三：Round 9 历史进展（2026-11-16）

### 1.1 本轮做了什么

- [x] Expression：接入 `Fluid.Core`，支持变量、条件、循环、过滤器与自定义 `json` 过滤器；渲染结果立即做 JSON 解析校验，失败带行号；专属面板覆盖模板、字段别名、样本 JSON、语法校验与预览输出。
- [x] Validator：新增 `schema` 规则（`JsonSchema.Net`）、`RejectOutputPath` 拒绝行 JSONL（`lineNumber` / `reason` / `data`）、失败率日志；`RejectOnFailure=false` 时非法行透传；专属面板支持规则/Schema 与路径编辑。
- [x] Sampler：`Splits` 支持 `name:ratio` 与 `name:count`，确定性、单行只进一个文件、原子写多文件；下游仍输出全部行；专属面板覆盖 Count/Ratio/Seed/Splits/输出目录。
- [x] Tool Profile：`ToolProfileBlockExecutor` 配置型节点；参数树可增删/改名/改类型/必填并与 JSON Schema 双向同步；支持完整 profile、tool 数组或单 tool 的 JSON 导入导出；Simulation 合并 profile tools，LLM 将 tools 写入输出元数据；校验未知引用。
- [x] 新增 26 个测试：Expression 模板/预览 5、Validator Schema/reject 6、Sampler splits 5、Tool Profile 复用/编辑 6、校验补强 4。
- [x] 新增依赖：`Fluid.Core 2.41.1`、`JsonSchema.Net 9.7.0`。

### 1.2 验证结果

| 项目 | 命令 | 结果 |
| --- | --- | --- |
| 构建 | `dotnet build LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 成功，0 警告，0 错误 |
| 测试 | `dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 311 通过，0 失败，2 跳过（真实 provider 冒烟需环境变量） |
| 启动冒烟 | `dotnet run --project LMTrainingDataStudio2/LMTrainingDataStudio2/LMTrainingDataStudio2.csproj --no-build` | 进程持续运行 10s，无异常输出 |

### 1.3 本轮改动文件

```text
PLAN/TODO.md
PLAN/STATUS.md
README.md
LMTrainingDataStudio2/LMTrainingDataStudio2/LMTrainingDataStudio2.csproj
LMTrainingDataStudio2/LMTrainingDataStudio2/Models/BlockConfig.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Models/SimulationConfig.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Models/ToolSchema.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/ToolProfileResolver.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/RecipeRunner.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/RecipeValidationService.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Executors/ExecutionContracts.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Executors/TemplateRenderer.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Executors/ExpressionBlockExecutor.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Executors/JsonSchemaRuleEvaluator.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Executors/ValidatorBlockExecutor.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Executors/SamplerSplitPlan.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Executors/SamplerSplitWriter.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Executors/SamplerBlockExecutor.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Executors/ToolProfileBlockExecutor.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Executors/SimulationBlockExecutor.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Executors/LlmBlockExecutor.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/BlockNodeViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/ExpressionConfigEditorViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/ValidatorConfigEditorViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/SamplerConfigEditorViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/ToolProfileEditorViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/ToolEditorViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/ToolParameterNodeViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/LlmConfigEditorViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/SimulationConfigEditorViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/MainWindowViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Views/PropertyPanelView.axaml
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/ExpressionTemplateTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/ValidatorSchemaAndRejectTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/SamplerSplitTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/ToolProfileReuseTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/RecipeValidationServiceTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/BlockConfigSerializationTests.cs
```

### 1.4 技术决策与取舍

1. **Expression 采用 Fluid 原生语义，JSON 值用 `| json` 过滤器嵌入。**
   - 不再自动转义 `{{ value }}`；模板必须自己产出合法 JSON，执行时立即校验，避免错误延迟到下游。
2. **Validator reject 写独立 JSONL，而不是新增输出端口。**
   - 当前 Runner 仍按 block 单输出执行；文件方案可立即交付并保留失败行号/原因，多输出端口留到后续。
3. **Sampler 采用“多文件 + 全量下游”语义。**
   - 每个输入行只进一个 split 文件，同时全部行按原顺序传给下游；ratio 模式用固定 seed 的随机序列保证可复现。
4. **Tool Profile 通过引用名解析，而不是数据边。**
   - Tool Profile 是配置型节点（无端口）；引用可匹配 block 名称或 profile 名称；Simulation 实际使用，LLM 先写入输出 `tools` 元数据，请求侧 tool definitions 留到后续。
5. **树编辑器与 JSON Schema 双向同步。**
   - 参数树变更、JSON 文本框、工具名/描述都会回写 `ToolProfileBlockConfig`，保持单一事实来源。

---

## 附二：Round 8 历史进展（2026-11-16）

### 1.1 本轮做了什么

- [x] Recipe 文件生命周期：File 菜单/快捷键、新建（空白/示例）、打开、保存、另存为、最近文件、脏标记、关闭确认、重启恢复。
- [x] Avalonia `StorageProvider` 文件选择；加载失败保留当前文档，错误信息包含路径与 JSON 位置。
- [x] 端口 schema：Seed / Query Source 仅输出，SFT Export 仅输入，Tool Profile 无数据端口，其余输入 + 输出；数据端口统一为 `rows`。
- [x] 连接守卫：方向、DataType、重复边、单输入、成环在入图前拦截，并写入状态栏/日志。
- [x] 校验补强：端口契约、必需输入连接、单输入、方向/类型；属性面板问题列表可定位；画布错误/警告徽标。
- [x] 启动默认空白文档；“New from Sample”提供 Query Source → Simulation → Cleaner → SFT Export 示例。
- [x] 测试：端口工厂、连接规则、端口契约校验、坏文件加载、保存重载往返、最近文件/恢复/脏标记。

### 1.2 验证结果

| 项目 | 命令 | 结果 |
| --- | --- | --- |
| 构建 | `dotnet build LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 成功，0 警告，0 错误 |
| 测试 | `dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 285 通过，0 失败，2 跳过（真实 provider 冒烟需环境变量） |
| 启动冒烟 | `dotnet run --project LMTrainingDataStudio2/LMTrainingDataStudio2/LMTrainingDataStudio2.csproj --no-build` | 进程持续运行 10s，无异常输出 |

### 1.3 本轮改动文件

```text
PLAN/TODO.md
PLAN/STATUS.md
README.md
LMTrainingDataStudio2/LMTrainingDataStudio2/Models/BlockPortSchema.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Models/AppSettings.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/ConnectionRules.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/RecipeValidationService.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/SettingsStore.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/MainWindowViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/BlockNodeViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/ValidationIssueViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Views/MainWindow.axaml
LMTrainingDataStudio2/LMTrainingDataStudio2/Views/MainWindow.axaml.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Views/MessageDialog.axaml
LMTrainingDataStudio2/LMTrainingDataStudio2/Views/MessageDialog.axaml.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Views/PropertyPanelView.axaml
LMTrainingDataStudio2/LMTrainingDataStudio2/Views/RecipeCanvasView.axaml.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/BlockPortSchemaTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/ConnectionRulesTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/MainWindowViewModelDocumentTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/RecipeValidationServiceTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/RecipeRunnerTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/SimulationExecutorsTests.cs
```

### 1.4 技术决策与取舍

1. **端口契约集中在 `BlockPortSchema`。**
   - 画布、示例 Recipe、ViewModel 与校验共用同一份定义，避免“新建节点端口”与“校验规则”漂移。
2. **连接在入图前拦截，而不是只依赖执行前校验。**
   - 错误连接立即反馈到状态栏/日志；图校验仍作为兜底。
3. **脏标记忽略选择与校验状态。**
   - 只对文档内容（名称、节点、端口、边、配置、坐标）置脏，避免点选节点或执行校验触发关闭确认。
4. **设置文件同时保存最近文件与上次打开路径。**
   - 复用既有原子写入；启动时文件缺失或损坏只回退空白并记录警告，不阻塞启动。

---

## 附一：Round 7 历史进展（2026-11-16）

### 1.1 本轮做了什么

- [x] 真实 provider tool-calling：新增 `ProviderChatCompletionClient`，支持 OpenAI Chat Completions function calling、OpenAI Responses `function_call`、Anthropic Messages `tool_use`；解析文本、工具调用、finish reason 与 token usage。
- [x] Simulation 配置扩展：`ModelMode`（Scripted / Provider）、`Endpoint`、`ToolMode`（Simulated / Real）、`SandboxLimits`、`MaxConcurrency`、`FailureOutputPath`、`MaxFailureSamples`、输入/输出百万 token 成本。
- [x] 真实沙箱执行：
  - `SandboxProcessRunner`：argv 直传、无 shell 拼接、环境变量白名单、临时工作目录、输出上限、墙钟超时与进程树终止。
  - Windows Job Object：进程内存上限、活动进程上限、宿主退出即杀；其他平台保留超时与进程树终止。
  - `RealToolRuntime`：python（`-I`）、PowerShell（`-NoProfile -NonInteractive -ExecutionPolicy Bypass -File`）、bash（`--noprofile --norc -c`）、web_fetch（显式开启、阻止私网/回环、禁跟随重定向、响应大小上限）。
- [x] `SimulationBlockExecutor`：按 `MaxConcurrency` 并行处理 query，输出保持输入顺序；记录模型调用数、tokens、延迟与估算成本；未完成轨迹可原子写入独立失败 JSONL（受 `MaxFailureSamples` 限制），取消时不落盘。
- [x] `TrajectorySerializer` 新增失败样本 JSON（query / status / error / messages / tools / usage）；SFT 输出仍只含 `messages` + `tools`。
- [x] 校验补强：max concurrency、max failure samples、provider endpoint、沙箱负数限制、真实工具执行器白名单。
- [x] 专属属性面板：Query Source / Simulation / Cleaner / SFT Export 编辑器；Simulation 面板覆盖 provider、模式、并发、成本、失败样本与沙箱参数，Tools/Rules/Script 仍可用 Config JSON 编辑。
- [x] 新增 gated 真实 provider 冒烟测试：仅当设置 `LMTDS_PROVIDER_SMOKE_*` 环境变量时联网，默认跳过；不保存 API Key。
- [x] 新增 48 个测试：沙箱 24、provider 客户端 9、执行器扩展 7、专属编辑器 5、校验 1、真实 provider 冒烟 2。

### 1.2 验证结果

| 项目 | 命令 | 结果 |
| --- | --- | --- |
| 构建 | `dotnet build LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 成功，0 警告，0 错误 |
| 测试 | `dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx` | 246 通过，0 失败，2 跳过（真实 provider 冒烟需环境变量） |
| 真实 provider 冒烟 | `LMTDS_PROVIDER_SMOKE_* dotnet test --filter ProviderSmokeTests` | 2 通过：tool_calls + usage（288+41）；harness 2 轮、tokens 644+54、1.75s |

本轮新增/扩展测试：

| 测试类 | 数量 | 覆盖内容 |
| --- | --- | --- |
| `SandboxTests` | 24 | 输出/退出码、超时杀进程、输出截断、工作目录、环境隔离、取消、参数校验、Windows 内存限制、python/powershell/bash 真实执行、`RealToolRuntime` 脚本映射、web_fetch 响应/截断/禁重定向、网络防护 |
| `ProviderChatCompletionClientTests` | 9 | OpenAI Chat/Responses、Anthropic 请求结构、tool_calls/tool_use 解析、usage、缺密钥、HTTP 错误、未知 provider、harness 往返 |
| `SimulationExecutorsTests` 扩展 | 7 | 失败样本文件与上限、并发保序、真实工具工厂、Provider 无凭据、usage/成本日志、取消不写失败文件 |
| `BlockNodeViewModelTests` 扩展 | 5 | 四种专属编辑器创建、Query/Simulation/Cleaner/SFT 配置回写、隐藏字段保留 |
| `RecipeValidationServiceTests` 扩展 | 1 | endpoint、并发、失败样本、沙箱负数、真实工具白名单 |
| `BlockConfigSerializationTests` 扩展 | 0（扩展既有） | 新字段往返与 Sandbox 深拷贝 |
| `ProviderSmokeTests`（gated） | 2 | 真实端点 tool_calls/usage；真实 provider + harness 两轮工具往返；无环境变量时跳过 |

### 1.3 本轮改动文件

```text
PLAN/TODO.md
README.md
LMTrainingDataStudio2/LMTrainingDataStudio2/Models/SandboxConfig.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Models/SimulationConfig.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Models/BlockConfig.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Sandbox/SandboxProcessRunner.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Sandbox/RealToolRuntime.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Simulation/ChatCompletionContracts.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Simulation/ProviderChatCompletionClient.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Simulation/SimulationHarness.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Simulation/TrajectorySerializer.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/Executors/SimulationBlockExecutor.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Services/RecipeValidationService.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/BlockNodeViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/QuerySourceConfigEditorViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/SimulationConfigEditorViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/CleanerConfigEditorViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/SftExportConfigEditorViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/ViewModels/MainWindowViewModel.cs
LMTrainingDataStudio2/LMTrainingDataStudio2/Views/PropertyPanelView.axaml
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/SandboxTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/ProviderChatCompletionClientTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/SimulationExecutorsTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/BlockNodeViewModelTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/BlockConfigSerializationTests.cs
LMTrainingDataStudio2/LMTrainingDataStudio2.Tests/RecipeValidationServiceTests.cs
```

### 1.4 技术决策与取舍

1. **provider 客户端只做单次 HTTP 调用，超时与重试仍由 harness 统一控制。**
   - 避免两套重试策略叠加；模型级 `TimeoutSeconds` / `ModelMaxRetries` 对所有 transport 一致生效。
2. **沙箱是协作式隔离，不是内核级安全边界。**
   - 同一用户运行、无容器/命名空间；Windows 用 Job Object 限制内存与活动进程，其他平台只有超时、进程树终止和输出上限。
   - 环境变量白名单不继承 API key 等宿主密钥；脚本落在独立临时目录并在结束后删除。
3. **web_fetch 默认阻断，必须显式开启网络，并拒绝回环/私网地址。**
   - 不跟随 HTTP 重定向，避免重定向绕过私网检查；响应大小受限。
4. **并发只做有界窗口，输出严格保持输入顺序。**
   - 日志通过锁串行化；失败样本在内存中受 `MaxFailureSamples` 限制，结束时临时文件 + 原子替换写入。
5. **成本统计使用节点显式配置的单价。**
   - 不内置价格表，避免模型价格变动导致数据失真；成本只写 run log，不进入 SFT 样本。

### 1.5 本轮未做

- 容器/文件系统/网络命名空间级隔离；非 Windows 平台的内存与进程数限制。
- provider 流式输出与 tool-call delta 增量解析、速率限制、多模态消息。
- 新面板的手工 UI 验收（XAML 编译与编辑器回写单测通过）。
- CI 中默认执行真实 provider 冒烟（需显式环境变量；本轮已用真实 OpenAI 兼容端点跑通 2 项）。
- 内置各模型价格预设。

---

## 一·附、Round 6 进展（历史）

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

> 以下四项已在 Round 7 完成，保留原文作为 Round 6 历史记录。

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
| 6 | 模拟 API 测试 | Query Source / Simulation Harness / Cleaner / SFT Export 子节点，确定性多轮轨迹与 OAI 导出 | 已完成 Round 6 核心与 Round 7：真实 provider tool-calling、真实沙箱、专属面板、并发与成本统计 |

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
   - 使用 Kahn 拓扑排序分层；MVP 先按层内顺序执行，Round 12 已升级为层内受控并行。
   - 失败策略：默认 fail-fast，当前节点失败后下游标记 Skipped。
   - 进度通过 `IProgress<ExecutionProgress>` 汇报；每处理 N 行或状态变化时上报。
   - 取消：响应 `CancellationToken`，1 秒内停止并清理临时输出。
4. **执行器（MVP 顺序）**
   - Seed：通过 `JsonlDatasetReader` 逐行输出。
   - Expression：先实现 `{{ field }}` 变量替换与字段映射子集，用 `IExpressionEngine` 隔离，Round 9 已替换为 Fluid 引擎。
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
9. **Prompt / Fluid 编辑器**
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

### Phase 6 — 模拟 API 测试与 SFT 轨迹生成（P0，Round 6–7）

#### 目标

支持“用户 query → 多轮 LLM/tool 交互 → 清洗 → OAI messages 导出”的完整数据生成链路：默认使用确定性脚本与模拟工具（零网络、零费用、可复现），也可切换到真实 provider tool-calling 与真实沙箱工具；工具行为、失败、超时、重试、并发、成本与失败样本均可配置。

#### 当前进度（Round 7，2026-11-16）

- [x] Query Source 节点：同 query 重复（`SameQuery`）、多组 query（`MultipleQueries`）、可配置 system prompt。
- [x] Simulation 节点：`ScriptedChatClient` + `SimulationHarness` 多轮轨迹；记录 assistant `tool_calls` 与 `tool` 消息。
- [x] mini harness 规则：`ToolHarnessRules`（工具白名单、默认超时、重试次数、重试延迟、失败中止）+ 单工具规则（成功/失败/超时/前 N 次失败/延迟）。
- [x] 模拟工具目录：python / web_fetch / powershell / bash；只返回确定性模拟响应。
- [x] 轨迹清洗：正则替换、空白/换行规范化、ANSI 清理、移除 reasoning、丢弃空消息；Validator 可选 `Cleanup`。
- [x] SFT 导出：Simulation 输出 OAI `messages` + `tools`；SftExport 节点原子写 JSONL。
- [x] 4 个新 BlockType 接入 `BlockConfig`、默认配置、深拷贝、Recipe 校验、Block Sheet、执行器注册。
- [x] 通用 Block Config JSON 编辑器（带类型校验与错误提示）。
- [x] 端到端测试：Query → Simulation → Cleaner → SftExport 由 `RecipeRunner` 真跑并产出 JSONL。
- [x] 真实 tool-calling provider 客户端（OpenAI Chat function calling / OpenAI Responses function_call / Anthropic tool_use）。
- [x] 真实 python / PowerShell / bash 沙箱执行（环境白名单、临时工作目录、超时杀进程树、Windows Job Object 内存/进程限制、输出上限）。
- [x] 新节点专属属性面板（Query Source / Simulation / Cleaner / SFT Export）。
- [x] 并发 query（有界窗口、输出保序）、模型 tokens/延迟/成本统计、失败样本单独原子输出。
- [x] web_fetch 真实抓取（默认关闭、私网防护、禁重定向、大小上限）。

#### 范围与实现方式

1. **数据模型（`Models/SimulationConfig.cs`、`Models/DatasetCleanup.cs`）**
   - `QuerySourceBlockConfig(string[] Queries, QuerySetMode Mode, int Repeat, string? SystemPrompt)`。
   - `SimulationBlockConfig(ModelAlias, MaxRounds, SystemPrompt, Tools, Rules, Script, FinalContent, Provider, Temperature, MaxTokens, TimeoutSeconds, ModelMaxRetries, EmitIncompleteTrajectories, ModelMode, ToolMode, Sandbox, Endpoint, MaxConcurrency, FailureOutputPath, MaxFailureSamples, InputCostPerMillionTokens, OutputCostPerMillionTokens)`。
   - `SimulatedToolRule`：name / description / OpenAI parameters JSON / behavior / response template / error / delay / failures-before-success / timeout。
   - `ToolHarnessRules`：allowed tools、default timeout、max retries、retry delay、abort-on-tool-failure。
   - `SimulatedTurn` / `SimulatedToolCallSpec`：确定性脚本回合。
   - `SandboxLimits`：工具超时、输出上限、内存/进程数上限、网络开关、私网开关、抓取上限、解释器路径。
   - `DatasetCleanupRules`：regex replacements、whitespace、newline、ANSI、reasoning、empty-message 规则。
2. **Mini harness（`Services/Simulation/`）**
   - `IChatCompletionClient` + `ScriptedChatClient`：请求携带过滤后的 tool definitions 与轮次。
   - `ProviderChatCompletionClient`：OpenAI Chat function calling、OpenAI Responses function_call、Anthropic tool_use；解析文本、工具调用、finish reason 与 usage。
   - `IToolRuntime` + `SimulatedToolRuntime` / `RealToolRuntime`：单次工具尝试；失败计数按轨迹重置。
   - `Services/Sandbox/SandboxProcessRunner`：环境白名单、临时目录、输出上限、超时杀进程树、Windows Job Object。
   - `SimulationHarness`：system/user 初始化、逐轮模型调用、工具白名单、每工具超时、重试与延迟、错误回填/中止、max rounds、取消、诊断日志、usage/延迟累计。
   - `TrajectoryResult`：messages、tools、status（Completed / MaxRoundsExceeded / Aborted）、轮数、工具调用/重试次数、tokens、延迟、估算成本。
3. **清洗与导出**
   - `TrajectoryCleaner`：对 `messages[].content`（字符串或 text 数组）与 `tools[].function.description` 应用规则；未知字段保留。
   - `TrajectorySerializer`：SFT 仅输出 `messages` + `tools`，宽松 JSON 编码保留中文；失败样本另含 query/status/error/usage。
   - `SftExportBlockExecutor`：流式写临时文件，完整枚举后 `File.Move(overwrite: true)`；取消/异常删除临时文件。
   - 失败样本：内存中有界收集，结束时临时文件 + 原子替换写入；取消不产出。
4. **执行器**
   - `QuerySourceBlockExecutor`、`SimulationBlockExecutor`、`CleanerBlockExecutor`、`SftExportBlockExecutor`。
   - `SimulationBlockExecutor` 每行独立 harness/runtime；未完成轨迹默认跳过并写日志；`MaxConcurrency` 有界并行且输出保序。
   - `ValidatorBlockExecutor` 在规则通过后可选应用 `DatasetCleanupRules`。
5. **UI**
   - Block Sheet 新增 Query Source / Simulation / Cleaner / SFT Export。
   - 属性面板新增通用 Config JSON 编辑器：反序列化为 `BlockConfig`，校验 kind 与 BlockType 一致后替换配置。
   - 四个新节点均有专属面板；Simulation 面板覆盖 provider、模式、并发、成本、失败样本、沙箱参数。
   - 新节点颜色与图标资源。

#### 交付物

- `Models/SimulationConfig.cs`、`Models/DatasetCleanup.cs`
- `Services/Simulation/ChatCompletionContracts.cs`、`ScriptedChatClient.cs`、`ProviderChatCompletionClient.cs`、`SimulatedToolRuntime.cs`、`SimulationHarness.cs`、`TrajectoryCleaner.cs`、`TrajectorySerializer.cs`
- `Services/Sandbox/SandboxProcessRunner.cs`、`RealToolRuntime.cs`、`Models/SandboxConfig.cs`
- `Services/Executors/QuerySourceBlockExecutor.cs`、`SimulationBlockExecutor.cs`、`CleanerBlockExecutor.cs`、`SftExportBlockExecutor.cs`
- `ViewModels/QuerySourceConfigEditorViewModel.cs`、`SimulationConfigEditorViewModel.cs`、`CleanerConfigEditorViewModel.cs`、`SftExportConfigEditorViewModel.cs`
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
- [x] 真实 provider function calling 往返（Fake HTTP 覆盖三种格式 + 真实 OpenAI 兼容端点冒烟 2 项通过）。
- [x] 真实沙箱工具执行与资源限制（进程/超时/输出/环境；Windows 额外内存与进程数限制）。

#### 收尾动作

- [x] 更新本文件 Round 6 进展与 Phase 6 状态。
- [x] 更新 README 当前状态、节点列表与路线图。
- [x] 运行 `dotnet build` / `dotnet test`，记录 0 警告与 200 通过。
- [x] 真实 provider / 沙箱执行 / 专属面板完成后再次验收（自动化部分）。

---

## 四、详细缺口清单（按领域）

> 以下是当前代码的全部已知未完成项，作为 Phase 1–5 的工作池。完成后在对应项打勾。

### 4.1 文件与数据集 IO

- [x] Recipe 新建 / 打开 / 保存 / 另存为（Round 8）。
- [x] JSONL 流式分页预览与数据表格（Round 10，Data 视图；ListBox 虚拟化）。
- [x] 大文件随机行跳转 UI（Round 10）。
- [x] CSV 读写（CsvHelper，Round 10）。
- [x] Parquet 读写（Parquet.Net，Round 10）。
- [x] `DataFileType` 枚举接线（Round 10，Seed + 数据工作台）。
- [x] 去重、混洗、切分（Round 11，数据工作台 Transform）。
- [x] 正则搜索与替换（Round 11，当前页/全文件/预览/取消）。
- [x] 数据集统计（角色、长度、空样本、重复率、工具调用，Round 11）。
- [x] 数据增删查改、字段级单元格编辑与整行 JSON 编辑、保存到新文件或覆盖源文件、编辑撤销/重做（Round 14）。
- [x] 输出预览弹窗 / 样本查看器（SFT Export、Validator reject、Simulation 失败样本、Cleaner 前后 diff，Round 14）。
- [x] 搜索命中行内高亮（Round 14）。
- [x] 最近文件、脏标记、关闭确认（Round 8）。
- [部分] 文件编码、CRLF、超长行、无结尾换行：JSONL 读取/校验已覆盖（Round 11）；CSV/Parquet 编码选项未暴露。
- [ ] CSV 类型推断与列类型展示（当前全部按字符串）。
- [ ] 多文件/目录数据源与 HuggingFace/HTTP 加载。

### 4.2 格式转换与校验

- [x] 转换器与 `DatasetValidationService` 均已接入数据工作台（Round 10/11）。
- [x] 批量转换 UI / 服务、进度、失败行报告（Round 10）。
- [x] DPO 格式（Round 10，批量转换；SFT Export 节点未直接输出）。
- [x] Pretrain 格式（Round 10，批量转换；SFT Export 节点未直接输出）。
- [x] 未知格式不再静默 fallback，给出明确错误（Round 10）。
- [x] JSONL 结构校验（messages/roles/content/tools，Round 11）。
- [x] 自动修复入口、预览、写修复副本（Round 11）。
- [x] 校验问题列表、行号跳转（Round 11）；级别/错误码筛选未做。
- [x] 校验错误上限/采样，避免超大坏文件吃满内存（Round 11）。
- [x] 校验问题分组与级别/错误码筛选 UI（Round 14）。
- [ ] Alpaca 多轮转换不丢轮次、不丢 tools。
- [ ] 转换保留额外字段 / 元数据，或明确声明丢弃策略。
- [ ] ShareGPT tool_call 解析失败上报，不静默吞异常。
- [ ] `AttachTools` 不插入空 system 消息。
- [ ] HuggingFace 数据源加载。

### 4.3 Recipe 节点模块

- [x] 节点配置模型：`BlockConfig` 多态层级 + `RecipeBlock.Config`，校验按配置类型检查（Round 2）。
- [x] 节点端口类型化：统一为 `rows`，输入/输出按 BlockType 生成（Round 8）。
- [ ] 端口增删、改名、改类型（属性面板只读展示）。
- [x] Recipe 序列化 / 反序列化 / schema 版本（`RecipeStore`，Round 2）。
- [x] 节点状态与错误徽标（Round 8）。
- [x] Seed 数据源文件选择、文件类型、Index Cache 编辑（Round 5）。
- [部分] Seed Streaming 开关仍只对 JSONL 生效且 UI 未暴露；预览在 Data 视图，节点内过滤/列映射未做。
- [x] LLM Provider（OpenAI Chat Completions / Responses / Anthropic Messages）、ModelAlias、Endpoint、温度、max tokens、Prompt（Round 5）。
- [x] LLM Prompt 使用 Fluid 模板（条件/循环/过滤器），执行期语法校验。
- [x] LLM 节点级超时与指数退避重试（Round 5）。
- [x] LLM 密钥设置界面：settings.json 本机保存，环境变量兜底（Round 5）。
- [x] LLM Tool Profile 引用：tools 写入输出行。
- [x] LLM 请求侧 tool definitions、结构化输出、多轮上下文、并发控制、token/成本统计、失败样本（Round 15）。
- [x] Expression：Fluid 模板、变量映射、渲染预览、语法校验（Round 9）；无外部执行沙箱。
- [x] Validator：表达式/JSON Schema/正则/脚本规则、多规则组合、超时、reject 文件与 pass/reject 端口（Round 9/15）。
- [x] Sampler：数量/比例、随机种子、Splits 多文件、多输出端口与分层采样、统计预览（Round 9/15）。
- [x] Tool Profile：树形编辑器、JSON 导入导出、Simulation/LLM 复用（Round 9）；MCP 未做。
- [x] 节点配置校验覆盖所有 10 种类型（Round 8/9）。
- [x] 环、孤立节点、重复边、端口方向/类型兼容校验（Round 3）。
- [x] 模拟 API 测试子节点：Query Source / Simulation / Cleaner / SFT Export（Round 6）。
- [x] 多轮 tool-calling 轨迹记录与工具白名单 / 失败 / 超时 / 重试规则（Round 6）。
- [x] 轨迹清洗（正则替换、格式规范化、ANSI、reasoning、空消息）与 Validator 可选清洗（Round 6）。
- [x] JSONL 导出（按行透传 + 原子替换，Round 6）。
- [x] 真实 tool-calling provider 客户端（OpenAI Chat/Responses、Anthropic tool_use，Round 7）。
- [x] 真实 python / PowerShell / bash 沙箱执行与资源限制（Round 7）。
- [x] Query / Simulation / Cleaner / SFT Export 专属属性面板（Round 7）。
- [x] 并发 query、模型成本/延迟统计、失败样本单独输出（Round 7）。

### 4.4 编辑器 UI / Canvas

已实现（Round 1–12，保留在此便于对照）：

- [x] Block Sheet 拖放添加、可搜索添加菜单（`~` / 右键）、节点拖动、多选拖动、框选。
- [x] 端口连线、边选择/删除、中键平移、滚轮缩放、缩放百分比。
- [x] Undo/Redo（添加/删除/移动/连线/粘贴块/重命名）、节点/边右键菜单、`Ctrl+Z` / `Ctrl+Y`。
- [x] 复制/粘贴块、Mini-map 渲染、双击空白添加、双击节点内联重命名。
- [x] 网格背景、网格吸附（Alt 临时关闭）、鼠标中心缩放、Fit to View、拓扑自动布局。
- [x] Mini-map 视口矩形与点击导航、复制携带内部连线、真实端口坐标、边 hover、增量拖拽。
- [x] 左右 `GridSplitter` 可调整宽度。
- [x] 属性面板校验问题列表与画布错误/警告徽标。
- [x] 10 个节点的专属属性面板（Cleaner/Simulation 部分字段仍走 Config JSON）。
- [x] 通用 Block Config JSON 编辑器（Round 6）。
- [x] Executions 视图与运行详情（列表、状态过滤、节点表、日志、取消、输出目录）。
- [x] 状态栏 `TotalLines`、`CurrentFilePath`、缩放百分比真实数据。
- [x] 未保存更改指示与关闭确认、Recipe 内容持久化。

未实现：

- [x] Undo/Redo 菜单/按钮入口与 `Ctrl+Shift+Z`；属性面板删除与配置修改进入撤销历史（Round 16）。
- [ ] 系统剪贴板 / 跨实例粘贴。
- [ ] 快捷键全局化，避免焦点冲突。
- [x] 节点尺寸调整手柄、边过渡动画、标签避让（Round 16）；自动布局间距/分组选项未做。
- [x] 样本/输出预览弹窗；Cleaner 前后 diff；Validator 失败样本预览（Round 14）。
- [x] Simulation Tools / Rules / Script 可视化编辑器、流式 delta、速率限制与价格预设（Round 15）。
- [ ] Cleaner 正则规则列表 UI；Validator Cleanup 规则 UI。
- [x] 状态栏内存占用定时刷新（Round 16，2s）。
- [x] 完整浅色主题：所有 Views 使用主题资源，无硬编码颜色（Round 16）。

### 4.5 运行时与执行

- [x] 真实 Recipe 执行引擎（服务层，Round 4）。
- [x] 节点执行器与执行上下文。
- [x] 拓扑排序与依赖传递（Round 12：层内受控并行）。
- [x] Execution / NodeState / 日志模型（内存态）。
- [x] 取消（运行级与节点级）。
- [x] 暂停 / 恢复 / 节点重试（Round 12）。
- [x] MainWindow 运行按钮接入 Runner，删除 `Task.Delay` 模拟。
- [x] 异常处理与 `IsRunning` 恢复。
- [x] Runner 按节点/行数上报进度。
- [x] 状态栏展示当前节点与进度；Executions UI 展示节点结果。
- [x] 状态栏 `CurrentFilePath`、缩放、总行数等实时数据。
- [x] 运行日志持久化与历史记录（`ExecutionStore` + Executions UI）。
- [x] 输出目录、命名、结果摘要（`run-summary.json`，Round 12）。
- [x] 中断恢复：从上次失败/中断节点继续（Round 13）。
- [x] 全链路临时文件清理验收；取消 1s 内停止的定量验收（Round 13）。
- [x] 节点间输出改为溢写磁盘 + 流式读取（Round 13）。
- [x] 模拟 API 多轮执行（确定性模型 + 模拟工具）与四节点 SFT 轨迹流水线（Round 6）。
- [x] 真实模型 tool-calling 与真实工具执行（Round 7）。

### 4.6 设置、主题与生命周期

- [x] 设置持久化：`ISettingsStore`/`SettingsStore`（Round 2）。
- [x] 暗/亮主题变体与 `ThemeManager`（Round 12；部分视图仍有硬编码深色）。
- [部分] 硬编码颜色资源化：主面板/画布已主题化，Data 视图与节点内部颜色仍为常量。
- [x] 网格吸附设置生效（Round 12；Alt 临时关闭）。
- [x] 窗口/应用图标（Round 12：`Assets/app-icon.png`）。
- [x] 全局异常处理与滚动日志（Round 12：`AppLog` + AppDomain/TaskScheduler/UI 线程）。
- [ ] 数值输入校验与范围提示（当前是保存时 clamp）。
- [ ] 缓存目录浏览按钮与可写性检查。
- [ ] API 密钥加密存储（当前 settings.json 明文，仅限本机使用）。
- [ ] 设置变更通知已创建的服务。
- [x] 单实例 / 文件关联 / 命令行打开（Round 16）。
- [x] 完整浅色主题（移除剩余硬编码颜色，Round 16）。
- [x] 内存占用定时刷新（Round 16）。

### 4.7 代码质量与已知缺陷

- [x] `_datasetValidation`、`_formatConverter` 未使用字段已移除（Round 12）。
- [x] `ToolSchemaNode`、`ToolProfile` 已用于 Tool Profile 编辑器与 Simulation/LLM 复用（Round 9）。
- [x] `MainWindowViewModel.AddBlock` 已被画布/添加菜单调用（Round 12 复核）。
- [ ] `BlockTypeToColorConverter`、`InverseBoolConverter` 未使用。
- [ ] `BlockTemplateViewModel.ColorKey` 未使用（若确认无用则删除）。
- [ ] `RecipeValidationService` 校验缺口（列号、问题分组；见 4.3）。
- [ ] `ValidationIssue` 缺少错误码、列号、分组信息。
- [ ] `Task.Run` + `Dispatcher.Post` 模式异常不可传播。
- [x] 日志已包含 UTC 时间戳与级别（`AppLog`）；来源/分类筛选未做。
- [部分] Recipe 有 schema 版本；Settings/Index Cache 无迁移策略。
- [x] 大文件内存策略已有 1M 行基准（Round 10/11）；执行链路持久化内存未解决（见 4.5）。

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
- [x] 模拟 harness：多轮、白名单、失败重试、超时、max rounds、abort、取消、OAI 序列化、非 ASCII 保留、usage/成本（14 个）。
- [x] 轨迹清洗：正则、空白/换行、ANSI、reasoning、空消息、数组 content（9 个）。
- [x] 模拟执行器与端到端流水线：Query / Simulation / Cleaner / SftExport、取消清理、失败样本、并发保序、真实工具工厂（23 个）。
- [x] 新配置序列化/深拷贝、Config JSON 编辑器、配置校验（16 个）。
- [x] 真实沙箱：进程执行、超时、输出截断、环境隔离、Windows 内存限制、python/powershell/bash 真实执行、web_fetch 响应/截断/禁重定向/网络防护（24 个）。
- [x] provider tool-calling：三种 provider 请求/响应、usage、错误、harness 往返（9 个）。
- [x] 真实 provider 冒烟：tool_calls/usage 与 harness 两轮往返，环境变量启用（2 个，默认跳过）。
- [x] 四个新节点专属编辑器：创建、回写、隐藏字段保留（5 个）。
- [x] 畸形输入测试集与 fixtures（Round 11：坏行/超长行/非法 UTF-8/错误上限）。
- [x] CI 自动化（Round 12：`.github/workflows/ci.yml`）。
- [x] Avalonia Headless UI 测试（Round 12：主窗口、画布、主题）。
- [x] 视口/布局/日志/并行执行/暂停/重试测试（Round 12，18 个）。
- [x] ViewModel 单测：文档生命周期、数据工作台、Executions、各节点配置编辑器（Round 5–12）。
- [部分] 集成测试：分模块 1M 行、四节点流水线与 1M 行执行链路已覆盖；完整 10M 行多节点链路待 Round 16。
- [部分] 性能基准：NUnit gated 1M 行测试已有（含编辑保存基线）；BenchmarkDotNet 专项基准未做。
- [x] 本地化/节点目录/预设：语言切换、10 类节点文案与端口摘要、6 个预设合法性与加载（16 个，Round 附六）。
- [x] 自定义预设：单文件存储、保存/导入/删除、原子写入、坏文件告警、快照隔离与 VM 加载（18 个）。
- [x] 溢写缓冲/节点检查点/恢复/重试清理/取消清理（Round 13，10 个）。
- [x] SFT 字段白名单/预览、运行溢写设置往返与回退（Round 13，7 个）。
- [x] 数据编辑：会话/保存/摘要/覆盖/取消/撤销重做（Round 14，9 个）。
- [x] 工作台编辑/高亮传播/删除插入/校验筛选/覆盖重开/字段编辑/分组树（Round 14，12 个）。
- [x] 字段读写：对象字段解析、嵌套 JSON、增删字段、非法值与重名（Round 14，8 个）。
- [x] 行内高亮匹配器（字面量/正则/大小写/超时/上限，Round 14，8 个）。
- [x] 样本预览：文件读取、Cleaner diff、格式限制与缺失文件（Round 14，5 个）。
- [x] Headless 行内高亮 Inlines 渲染（Round 14，1 个）。
- [x] 新增 10 个测试：配置编辑合并撤销、尺寸命令、显示名重命名、边标签避让、诊断包与脱敏、内存刷新、Views 无硬编码颜色、Dark/Light 资源完整性、主题切换动态刷新、示例 Recipe 校验（Round 16）。
- [x] 当前测试总数：505 通过 + 7 gated（2026-11-17 Round 16 复核）。

### 4.9 工程化与发布

- [x] LICENSE（Round 12：MIT）。
- [x] Assets：应用图标、README Dark/Light 截图、`examples/` 示例 Recipe（Round 16）。
- [x] 发布/打包：win-x64 self-contained 单文件脚本；MSIX 清单/脚本与 Inno Setup 安装脚本（Round 16，未做本机安装验证）。
- [x] CI/CD 流水线（Round 12：GitHub Actions）。
- [x] `.editorconfig` / 分析器 / 警告即错误（Round 16）。
- [x] 日志框架（Round 12：滚动文件日志 + 全局异常；诊断导出 Round 16）。
- [x] 崩溃报告导出/一键打包诊断（Round 16）。
- [部分] 中英文文案统一 / 本地化（静态 UI、节点说明、预设与语言切换已完成；运行日志/执行状态仍为英文）。
- [x] README 路线图与实现状态同步（Round 16）。
- [x] 构建环境：`net9.0` + `global.json` 已落地，本机构建通过。

---

## 五、优先级与建议顺序

> 历史 P0（Phase 1 持久化、Phase 2 执行引擎、Round 6/7 模拟与真实 provider/沙箱、Round 8–12）已完成，见 [STATUS.md](STATUS.md)。

### P0 — Round 13：执行与 IO 韧性（已完成，见附七）

1. [x] 节点间输出流式/溢写磁盘，内存阈值与临时目录可配置，运行结束/取消/失败统一清理。
2. [x] 节点级中断恢复：保存已完成节点输出，从上次失败/中断节点继续。
3. [x] 取消 1s 内停止与全链路临时文件清理的定量测试。
4. [x] SFT Export：文件选择器、覆盖确认、可选字段白名单、写出行数/预览摘要。
5. [x] 1M 行 Seed → Expression → SFT Export 链路内存 ≤ 300MB 验收。

### P1 — Round 14：数据工作台编辑与预览（已完成，见附八）

1. [x] 逐行 CRUD（插入/删除/复制/字段级单元格编辑）与另存/覆盖（覆盖需确认）。
2. [x] 编辑撤销/重做与保存前差异摘要。
3. [x] 搜索命中高亮、校验问题按级别/错误码筛选。
4. [x] 统一样本/输出预览弹窗（SFT / Validator reject / Simulation 失败样本 / Cleaner diff）。
5. [x] 1M 行编辑/保存内存上限验收（55.6s，托管内存增量 11MB）。

### P1 — Round 15：节点能力收紧（已完成，见附九）

1. [x] LLM 请求侧 tools、结构化输出、多轮、并发、token/成本、失败样本。
2. [x] Validator 代码规则/超时/多规则/pass-reject 端口。
3. [x] Sampler 多输出端口/分层采样/统计预览。
4. [x] Simulation Tools/Rules/Script UI、流式 delta、速率限制/价格预设。
5. [x] Query Source 模板与外部 query 源。

### P2 — Round 16：发布与体验

1. [x] 节点尺寸手柄、边动画、标签避让、Undo/Redo 菜单、属性修改撤销。
2. [x] 完整浅色主题、内存定时刷新。
3. [x] MSIX/安装包、文件关联、单实例、命令行打开、崩溃报告导出。
4. [x] 10M 行与 CSV/Parquet 大规模基线、README 截图、示例 Recipe。
5. [x] `.editorconfig`、分析器、警告即错误、文案统一。

### 已完成（供对照）

- [x] Recipe 文件生命周期、端口契约、错误徽标（Round 8）。
- [x] Expression/Validator/Sampler/Tool Profile 核心能力（Round 9）。
- [x] CSV/Parquet、格式转换、DPO/Pretrain、1M 基线（Round 10）。
- [x] 校验/修复、搜索替换、去重/混洗/切分、统计（Round 11）。
- [x] 画布核心交互、并行/暂停/重试、CI/打包/日志（Round 12）。

---

## 六、阶段规划

> 目标、交付与验收的完整版本见 [STATUS.md](STATUS.md) 第六节；这里保留执行顺序与出口标准。

### 6.1 已完成：Round 8–12

| Round | 主题 | 优先级 | 核心交付 | 出口标准 |
| --- | --- | --- | --- | --- |
| 8 | Recipe 生命周期与节点契约 | P0 | 新建/打开/保存/另存为、最近文件、脏标记、关闭确认；按类型端口 schema；端口与必需连接校验；错误徽标 | 保存重载完全一致；端口正确；坏文件有明确错误；0 警告、测试全绿 |
| 9 | 节点能力补齐 | P0/P1 | Expression 真实模板引擎 + 面板/预览；Validator Schema + reject 分支；Sampler Splits；Tool Profile 树形编辑器与复用 | 每个节点成功/失败/边界测试；模板/规则/切分结果可验证 |
| 10 | 数据工作台 A | P1 | JSONL 分页预览/行跳转；CSV/Parquet 读写；格式转换 UI；DPO/Pretrain；1M 行验收 | 1M 行内存 ≤ 300MB；转换行数守恒；失败行有行号 |
| 11 | 数据工作台 B | P1 | 校验问题列表/自动修复；搜索/正则替换；去重/混洗/切分；数据集统计 | 行数守恒、同 seed 可复现、原文件不被修复破坏；368 测试通过 |
| 12 | 画布、执行与发布 | P1/P2 | 网格/吸附/锚点缩放/Fit/自动布局/Mini-map 交互；层内并行、暂停/重试、输出目录；Headless UI、CI、打包、主题、日志 | 386 测试通过；500 节点布局 <100ms；单文件发布启动成功 |

### 6.2 下一阶段：Round 13–16

| Round | 主题 | 优先级 | 核心交付 | 出口标准 |
| --- | --- | --- | --- | --- |
| 13 | 执行与 IO 韧性 | P0 | 节点间溢写磁盘/流式读取；节点级中断恢复；取消/清理硬化；SFT Export 收尾 | 1M 行链路内存 ≤300MB；取消 1s 内停止；恢复输出一致；无临时文件残留 |
| 14 | 数据工作台编辑与预览 | P1 | 逐行 CRUD/单元格编辑/写回；搜索高亮；问题分组；样本/输出预览 | 编辑行数守恒、撤销可恢复、覆盖有确认；1M 行编辑内存 ≤300MB |
| 15 | 节点能力收紧 | P1 | LLM tools/结构化输出/多轮/并发/成本；Validator 代码规则/超时/双端口；Sampler 多端口/分层；Simulation 可视化配置 | 请求体与结构化输出可验证；端口行数守恒；分层比例在容差内 |
| 16 | 发布与体验 | P2 | 画布 polish、完整浅色主题、单实例/文件关联/诊断、MSIX/Inno、10M 与 CSV/Parquet 基线、README 截图、示例 Recipe、分析器 | 画布尺寸/撤销可用；浅色无残留；命令行/单实例可用；基线记录；0 警告 |

**执行顺序**：Round 13 → 14 → 15 → 16；四轮均已完成（见附七、附八、附九、附十）。后续从第六节 6.1 backlog 中选择排期；每轮结束按第七节收尾模板更新勾选状态、README 与提交记录。

---

## 七、每轮结束的收尾模板

每个 Phase/每轮开发结束时按以下顺序收尾：

1. 运行 `dotnet build`，确认 0 警告。
2. 运行 `dotnet test`，确认全绿；新增逻辑必须有测试。
3. 按本阶段“验收标准”逐条手工验证，记录未通过项。
4. 删除本阶段产生的临时代码、死字段、调试输出。
5. 更新本文件勾选状态与“本轮进展”。
6. 更新 README 中受影响的说明。
7. 提交：提交信息写明阶段、break change、验证命令与结果。