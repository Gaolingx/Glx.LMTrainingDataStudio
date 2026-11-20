# HANDOFF — 02-Hardening（节点扩展、规模与发布验证）

> 状态：进行中
> 创建：2026-11-20
> 基线：`736` 个常规测试通过 + `7` 个 gated + `1` 个 `[Explicit]`；`--list-tests` 744；覆盖率总行 77.55%、Services/Commands 85.07%。
> 上一阶段归档：[PLAN/Done/02-Bugfix1/HANDOFF.md](../../Done/02-Bugfix1/HANDOFF.md)
> 边界：本阶段只保留归档 HANDOFF 中仍未完成或新发现的工作；已完成的 Round 0–32 内容不再回写。

---

## 1. 本轮定位

Round 32 已关闭大部分 P1/P2：Recipe 校验错误码、CSV 类型推断、Sampler count 分层、SFT 比例拆片、三种 provider 流式、nightly/release/依赖审计。本阶段聚焦：

1. 节点能力扩展：MCP、查询生成、Simulation 隔离与限速。
2. 数据规模：视图索引磁盘化、10M 行筛选/排序、CSV/Parquet 质量基线、Seed 多源。
3. 发布验证：干净机器安装、签名、双进程单实例、依赖锁定决策。
4. 体验与质量：剪贴板/快捷键/分栏/节点分组、本地化、可释放资源与大文件拆分。

---

## 2. 工作池

### 2.1 节点能力（P2）

- [ ] Tool Profile MCP（stdio/HTTP）客户端、工具发现、调用与鉴权。
- [ ] Tool Profile 共享工具库、版本管理与 LLM 请求侧复用。
- [ ] Query Source 随机/模型生成 query、query 去重、权重与元数据。
- [ ] Simulation 容器/命名空间级隔离；非 Windows 资源限制。
- [ ] Simulation 严格令牌桶限速；工具结果裁剪/摘要策略 UI。
- [ ] Simulation 非 OpenAI Chat 流式 delta（当前仅 OpenAI Chat 记录 delta）。
- [ ] Validator 独立失败摘要面板；任意代码沙箱规则（当前 script 为 Fluid 布尔表达式）。
- [ ] Expression 自定义函数/类型转换；模板错误跳过或写失败文件策略。
- [ ] Cleaner 坏行跳过与报告；自定义字段/JSONPath 清洗。
- [ ] SFT Export DPO/Pretrain 直出；输出路径存在性/可写性提示。
- [ ] Sampler 切分内再混洗/保序选项；动态端口变化后旧连线即时失效。

### 2.2 数据规模与格式（P2/P3）

- [ ] 视图索引磁盘化，支持 10M 行筛选/排序并记录实测。
- [ ] 10M 行质量/转换基线；CSV/Parquet 1M 行质量基线。
- [ ] Seed 多文件/目录、节点内过滤/列映射/Limit/Offset。
- [ ] Seed 编码/分隔符/CSV 类型选项；HuggingFace/HTTP 数据源。
- [ ] Alpaca 多轮转换、额外字段保留策略、ShareGPT `tool_call` 解析失败上报。
- [ ] `AttachTools` 空 system 消息确认与修正。
- [ ] 数据集统计摘要本地化。

### 2.3 发布与工程化（P1/P2）

- [ ] MSIX/Inno 在干净机器安装、启动、文件关联各一次记录。
- [ ] 安装包签名流程与证书 Subject/Pubisher 对齐；可选 Azure Trusted Signing。
- [ ] 单实例命名管道双进程端到端自动化测试（gated）。
- [ ] 依赖锁定决策：当前 `RestorePackagesWithLockFile` 与 Debug-only `Avalonia.Diagnostics` 冲突，需移除配置相关包或改用其他锁定方案。
- [ ] 处理或明确接受当前传递依赖 High 公告（Snappier、SQLitePCLRaw、Tmds.DBus.Protocol）；升级 Avalonia/SQLite 依赖后复测。
- [ ] PR/Issue 模板与贡献指南；PR 测试摘要/注解。
- [ ] 跨平台 CI 构建冒烟（Linux/macOS，条件跳过 Windows 专属沙箱测试）。

### 2.4 体验与质量（P3）

- [ ] 系统剪贴板/跨实例粘贴；全局快捷键避免文本框焦点冲突。
- [ ] 自动布局间距/分组选项；分栏宽度持久化；端口增删/改名/改类型（若产品允许）。
- [ ] 自定义预设导出到任意路径；节点分组/注释框。
- [ ] 运行日志/执行状态/统计摘要本地化。
- [ ] 10 个节点面板逐项人工验收；大文件工作台交互验收；画布逐帧性能计时；视觉回归基线。
- [ ] CA1001：视图模型与 `RequestRateLimiter`、`AutoFixWriter` 接入 `IDisposable`。
- [ ] `async void` 与 `Task.Run` + UI 回调异常边界复核。
- [ ] 大文件拆分：`MainWindowViewModel`、`RecipeValidationService`、`RecipeRunner`、`DataWorkbenchViewModel`。
- [ ] 设置 schema 版本与迁移、恢复默认设置、日志级别/来源筛选、全局代理/HTTP 配置。
- [ ] 崩溃报告保留策略；web_fetch 域名白名单/代理策略；独立威胁模型文档。
- [ ] 用户手册/安装排障文档；README 新 UI 截图。

### 2.5 Backlog（暂不排期）

- [ ] 插件化节点、云端执行、Recipe diff/merge、便携模式/设置同步。
- [ ] 实时图表（LiveCharts2/OxyPlot）与执行指标面板。
- [ ] 自动更新/版本检查；更多界面语言；BenchmarkDotNet 专项基准。

---

## 3. 建议顺序

1. **发布可信度**：干净机器安装/签名、双进程单实例、依赖公告处理。
2. **节点扩展**：MCP、Query 生成、Simulation 隔离/限速。
3. **数据规模**：视图索引磁盘化与 10M 基线、Seed 多源。
4. **体验与质量**：剪贴板/快捷键/分栏/节点分组、本地化与大文件拆分。

每批建议 3–5 项，先补测试再改实现；涉及 UI 的项至少补充 Headless 断言并记录手工验收结果。

---

## 4. 验证命令

```powershell
dotnet build LMTrainingDataStudio2/LMTrainingDataStudio2.slnx -c Release
dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx -c Release
dotnet format LMTrainingDataStudio2/LMTrainingDataStudio2.slnx --verify-no-changes
```

gated 性能：

```powershell
$env:LMTDS_PERF_TESTS = "1"
$env:LMTDS_PERF_ROWS = "1000000"
$env:LMTDS_PERF_PIPELINE_ROWS = "1000000"
dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx -c Release --filter "FullyQualifiedName~DatasetPerformanceTests|FullyQualifiedName~PipelinePerformanceTests"
```

真实 provider 冒烟：

```powershell
$env:LMTDS_PROVIDER_SMOKE_KEY = "<key>"
$env:LMTDS_PROVIDER_SMOKE_BASE_URL = "https://api.openai.com/v1"
$env:LMTDS_PROVIDER_SMOKE_MODEL = "gpt-4o-mini"
dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx -c Release --filter "FullyQualifiedName~ProviderSmokeTests"
```

---

## 5. 验收标准

- `dotnet build` 0 警告、0 错误；`dotnet test` 全绿；格式门禁通过。
- 每个新增/修改核心逻辑有成功、失败与边界测试。
- UI 入口真实可用，新增控件有 Headless 或手工验收记录。
- 文件写入保持临时文件 + 原子替换；多文件拆片失败不留下半成品。
- README / STATUS / HANDOFF / CHANGELOG 与代码一致。
- 发布项至少有一份干净机器安装与启动记录；未做项需写明原因与后续入口。
