# HANDOFF — 02-Bugfix1（全仓缺陷修复与门禁补齐）

> 状态：已完成并归档（Round 32，2026-11-20）；DoD 有 3 项例外，已转交 [02-Hardening HANDOFF](../../TODO/02-Hardening/HANDOFF.md)。
> 创建：2026-11-17
> 基线：`895c08e feat(release): Round 16 发布与体验`
> 上一阶段归档：[PLAN/Done/01-BuildALL/HANDOFF.md](../../Done/01-BuildALL/HANDOFF.md)
> 扫描基线：`dotnet build` 0 警告；Round 32 收尾基线为 `--list-tests` 744 个用例（736 通过 + 7 gated + 1 Explicit），覆盖率 77.55% / 85.07%。
> 边界：本文件只记录 Round 17 及之后的修复/补齐工作；Round 0–16 的原始计划、验收标准与技术决策已冻结在归档文件中，不再回写。
> 历史快照：文中测试数与本地化键数为归档时值；当前值以 [STATUS.md](../../STATUS.md) 为准。

---

## 1. 本轮定位

本轮不新增节点类型，也不做大规模架构重写。目标是：

1. 修复建设期遗留的**一致性问题**（安装器、命令行、文件选择器、版本、图标、文档）。
2. 补齐**测试与 CI 门禁**，让后续改动有可验证的回归保护。
3. 收敛**节点、数据、执行、UI**的已知边角，避免“能用但不可交付”。
4. 完成**发布链路验证**（安装、签名、版本、诊断）。
5. 让 README / STATUS / HANDOFF 与代码重新一致。

本轮完成的标准不是“清单全部勾完”，而是：**P1 全部关闭，P2 有明确结论（完成或带原因延期），并且每个关闭项都有对应验证证据。**

---

## 2. 本轮已完成（2026-11-17）

- [x] 完成全仓扫描：`LMTrainingDataStudio2/`、测试项目、`.github/`、`scripts/`、`packaging/`、`examples/`、`README.md`、`PLAN/*.md`。
- [x] 建立当前基线：`dotnet build` 0 警告；`dotnet test --list-tests` 513 个用例（505 通过 + 7 gated + 1 Explicit）。
- [x] 将建设期 TODO 归档并冻结到 [PLAN/Done/01-BuildALL/HANDOFF.md](../../Done/01-BuildALL/HANDOFF.md)。
- [x] 建立本 HANDOFF，形成 167 项未完成工作池，按 P1/P2/P3 与领域分类；二次扫描后增补至 205 项。
- [x] 2026-11-17 二次全仓扫描复核：`dotnet test --list-tests` 返回 513 个用例且构建成功；生产代码无遗留 `TODO` / `FIXME` / `NotImplementedException`；zh-CN / en-US 资源键各 432 个（归档时值；当前 507）；确认 7.x 清单与代码一致，并新增 8.3 的 38 项增补。
- [x] 确认以下高优先级问题（本轮尚未修复）：
  - `.recipe` 文件关联与命令行解析不一致：`Program.cs:62` 只接受 `.json`，但 MSIX/Inno 注册 `.recipe`。
  - `.recipe` 不在打开/保存文件选择器中：过滤器只有 `*.json`。
  - MSIX `-Version` 不会写入 `AppxManifest`；csproj / app.manifest / MSIX / Inno 版本多源。
  - Windows EXE 未配置 `<ApplicationIcon>`，快捷方式/关联图标会显示默认图标。
  - README 画布“尚未实现”段落、STATUS 多处状态、TODO 4.2/4.6/4.7 与代码不一致。
  - 单实例、命令行、本地化键、打包脚本均缺少自动化测试。
- [x] 本轮未修改业务代码；本轮改动仅为文档结构调整与归档。

**本轮验证方式**：`git diff --check` 通过；归档文件保持原 TODO 内容不变；新 HANDOFF 与归档文件路径固定。

---

## 3. 本轮计划（按批次推进）

### 3.1 Batch A：一致性缺陷 + 文档同步（P1，先做）

目标：让“安装器说的”和“程序做的”一致，让文档不再误导。

- `.recipe` 打开链路：命令行、文件选择器、安装器、README 四处统一。
- `.recipe.json` 关联表述修正：要么真正支持，要么删除宣传。
- 版本号单一来源：构建、MSIX、Inno、诊断包一致。
- EXE 图标、发布依赖、安装器注释修正。
- 设置页路径/数值校验。
- Recipe 校验错误码。
- README / STATUS / 历史 TODO 状态同步。

### 3.2 Batch B：测试与 CI 门禁（P1）

目标：让后续修改能被自动发现。

- 单实例、命令行、文件关联、本地化键一致性、打包清单/脚本校验。
- 覆盖率采集与阈值（先记录，再逐步提高）。
- `dotnet format --verify-no-changes`。
- 打包/发布产物启动冒烟。
- nightly 或手动触发的 gated 性能/真实 provider job。

### 3.3 Batch C：节点与数据边角（P1/P2）

目标：把“已可用”提升到“可交付”。

- Seed Streaming UI、Validator Cleanup UI、Cleaner 正则规则 UI。
- LLM tool_calls 回填、Anthropic 结构化输出。
- SFT Export 追加/目标格式/拆分/并发写检测。
- 数据工作台跨格式保存、匹配导航、预览分页。
- 转换器多轮 Alpaca / 额外字段 / ShareGPT tool_call 错误上报。

### 3.4 Batch D：执行、安全与发布验证（P2）

目标：把恢复、取消、安全与安装验证补齐。

- 恢复哈希比对、检查点配额、执行历史保留策略。
- API Key 加密、web_fetch DNS 重绑定加固、诊断日志脱敏。
- MSIX/Inno 干净机器安装验证、签名、版本一致性。
- 10M 行质量/转换基线按需实测并记录。

### 3.5 可延后（P3 / backlog）

- MCP、容器级隔离、HuggingFace/HTTP 数据源、插件节点、云端执行、实时图表、自动更新、更多语言。
- 死代码清理、大文件拆分、视觉回归基线、无障碍验收。

---

## 4. 大致怎么实现

### 4.1 Recipe 打开与文件关联

**实现要点**

1. 新增统一扩展名工具（例如 `RecipeFileExtensions`）：
   - 接受 `.json`、`.recipe`，并明确 `.recipe.json` 的处理方式（`.recipe.json` 的最终扩展名仍是 `.json`，因此天然被接受）。
   - 统一判断“是否为 Recipe 路径”，供 `Program`、文件选择器、单实例转发共用。
2. `Program.IsRecipePath` 改为调用统一工具；不要只比较 `.json`。
3. `RecipeFileType()` 的 `Patterns` 增加 `*.recipe`（保留 `*.json`），保存时仍默认 `.json`，或按产品决定默认 `.recipe.json`。
4. 安装器二选一：
   - 方案 A（推荐）：注册 `.recipe`，应用接受 `.recipe`；README 只宣传 `.recipe` 与 `.recipe.json` 可按需打开。
   - 方案 B：取消 `.recipe` 关联，只保留 `.json` 的 OpenWith；同步删除 README 与 Inno 中的 `.recipe` 表述。
5. 为 `.recipe` 双击打开、命令行打开、第二实例转发补测试。

**验收**

- 双击 `.recipe` 能启动并加载；文件选择器能看到并选择 `.recipe`。
- 命令行 `LMTrainingDataStudio2.exe <path>.recipe` 与 `.recipe.json` 均能打开。
- 安装器注释、README、程序行为三者一致。

### 4.2 版本、图标与打包

**实现要点**

1. 在 `Directory.Build.props` 增加单一版本源：
   - `Version`、`AssemblyVersion`、`FileVersion`、`InformationalVersion`。
2. `scripts/package-msix.ps1`：
   - 读取 `-Version` 后替换 `AppxManifest.xml` 的 `Identity Version`（建议用 XML 解析，不要字符串替换）。
   - 保留参数校验：MSIX 版本必须是四段数字。
3. Inno：
   - 通过 `ISCC /DMyAppVersion=...` 或生成临时 `.iss` 传入版本；不要在 `.iss` 中硬编码。
4. csproj 增加 `<ApplicationIcon>Assets\app-icon.png</ApplicationIcon>`（如格式允许；否则使用 `.ico`）。
5. `ApplicationIcon` 需要一个真正的 `.ico` 文件；PNG 可直接用于窗口图标，但 EXE 图标通常需要 ICO。
6. `Avalonia.Diagnostics` 改为 Debug 条件引用：
   ```xml
   <PackageReference Include="Avalonia.Diagnostics" Version="11.3.0" Condition="'$(Configuration)' == 'Debug'" />
   ```
7. 保留 `packaging/msix/AppxManifest.xml` 的 `Publisher` 说明，签名证书 Subject 必须匹配。

**验收**

- `dotnet publish` 产物属性中的版本与传入版本一致。
- MSIX 安装包 `Identity Version` 与传入版本一致。
- Inno 安装包文件名与显示版本一致。
- Explorer 中 EXE、快捷方式、`.recipe` 关联图标均为应用图标。
- Release 包不包含 `Avalonia.Diagnostics`。

### 4.3 设置与配置

**实现要点**

1. 给数值设置增加 ViewModel 层校验（`IDataErrorInfo` / `INotifyDataErrorInfo` 或显式错误文本），保存前提示而非静默 clamp。
2. 为 CacheDirectory、RunOutputDirectory、RunTempDirectory 增加 Browse 按钮与“可写性检查”：
   - 创建目录 → 写入/删除临时文件 → 失败时显示路径与原因。
3. API Key 加密：
   - Windows 使用 DPAPI（`ProtectedData`）或 Credential Manager；非 Windows 退化为环境变量优先。
   - 迁移逻辑：检测旧明文 → 加密回写 → 清理明文。
4. 给 Settings 增加 `SchemaVersion`，加载时按版本迁移/回退。
5. 增加“恢复默认设置”命令。
6. 日志面板增加级别/来源筛选；文件日志保留现有滚动策略。

**验收**

- 非法数值无法保存，错误提示包含字段名与允许范围。
- 不可写目录在保存时立即报错，不等到运行。
- `settings.json` 中不再出现明文 Key；旧配置可自动迁移。
- 重置默认设置后，主题/语言/路径均回到默认且可继续使用。

### 4.4 测试补齐

**实现要点**

1. 单实例：
   - 进程内启动服务端 → 客户端发送路径 → 断言回调；无服务端时 `TrySendToPrimary` 返回 false；超时/异常路径。
   - 双进程端到端可放在 gated/手动测试，避免 CI 不稳定。
2. 命令行：
   - 抽出参数解析方法并单测 `.json`、`.recipe`、无参数、非文件参数。
3. 本地化：
   - 新增 `LocalizationParityTests`：zh/en key 集合一致、占位符数量一致、所有静态 key 可解析。
   - 动态 key（`node.*`、`dialog.*`）由现有 BlockCatalog/Preset 测试覆盖，补一个“所有引用 key 均存在”的静态扫描测试。
4. 打包：
   - 新增 PowerShell 校验脚本：XML 版本、必需资产、关联项、版本一致。
   - 可在 CI 中以 `-WhatIf`/只读方式运行，不必真的安装。
5. 恢复：
   - 增加小 fixture 的逐字节/哈希比对测试。
6. 覆盖率：
   - 引入 `coverlet.collector`，先记录基线，再设置不低于当前值的门槛，避免一次性卡死。

**验收**

- 新增测试在 `dotnet test` 中默认运行（除明确 gated 的双进程/性能测试）。
- 每个 P1 缺陷至少 1 个回归测试。
- 覆盖率门槛写入 CI，且不会因单个无关改动频繁波动。

### 4.5 CI 与工程化

**实现要点**

1. CI 增加 job/step：
   - `dotnet format --verify-no-changes`
   - `dotnet test --collect:"XPlat Code Coverage"` + 上传 coverage
   - 打包冒烟：`dotnet publish` 后启动进程并检查 3 秒内未退出
   - Windows 打包脚本静态校验（不要求安装 MSIX/Inno）
2. 增加 `nightly.yml` 或手动 `workflow_dispatch`：
   - `LMTDS_PERF_TESTS=1` 的性能基线
   - `LMTDS_PROVIDER_SMOKE_*` 的真实 provider 冒烟（Secret 只存 GitHub Secrets）
3. 增加 Dependabot / CodeQL；至少启用 NuGet 与 GitHub Actions 更新。
4. 增加 `packages.lock.json` 或 `Directory.Packages.props`，统一版本。
5. 增加 release workflow：tag → build/test → publish → 打包 → 签名（可选）→ GitHub Release。
6. 增加 PR/Issue 模板与贡献指南（低优先）。

**验收**

- PR 上能直接看到格式、测试、覆盖率、打包结果。
- 失败信息能定位到具体 step。
- nightly 失败有明确 owner 与重跑方式。

### 4.6 节点模块

**实现要点**

1. **Seed**：
   - 在 `SeedConfigEditorViewModel` 增加 `Streaming` 属性并回写；在面板增加开关与说明（仅 JSONL + 关闭缓存时生效）。
   - 过滤/列映射建议作为 Seed 配置的 `Filters`/`ColumnMap`，执行时在读取器输出后流水线处理。
2. **LLM**：
   - tool_calls 回填：读取响应中的 tool_calls → 若有本地/Simulation 工具运行器 → 追加 tool 消息 → 再次调用模型；需要明确最大轮数与失败策略。
   - Anthropic 结构化输出：按 Anthropic 的 tool/JSON 约束实现，或明确文档化为不支持。
3. **Validator**：
   - 把 Cleanup 规则编辑抽成可复用面板（与 Cleaner 共用组件），避免两套 UI。
4. **Cleaner**：
   - 正则规则列表 UI：增删行、模式、替换文本、忽略大小写、上下移动；实时校验正则。
5. **Sampler**：
   - count 模式分层：先按 `StratifyField` 分组，再按 count 分配；需要定义余数分配规则。
6. **Tool Profile**：
   - Enum/Default 值编辑；与 JSON Schema 双向同步。
7. **SFT Export**：
   - 追加模式：使用 `FileMode.Append` 前仍需确认目标文件；明确“失败时不写半行”。
   - 目标格式：复用数据工作台的转换服务，或明确只支持 JSONL。
8. **Query Source / Simulation**：
   - 随机/模型生成 query、严格 token bucket、MCP 属于较大特性，先做设计文档再排期。

**验收**

- 每个节点改动必须有：成功路径、失败路径、边界路径测试。
- 所有 UI 入口真实可用，不允许“按钮无反应/面板无内容”。
- 配置序列化往返、深拷贝、Recipe 校验同步更新。

### 4.7 数据工作台

**实现要点**

1. 跨格式保存：
   - 方案 A：保存时先写源格式临时文件，再调用批量转换到目标格式。
   - 方案 B：扩展 `DatasetEditService` 支持目标 writer，但需保证未修改行语义不变。
   - 明确 CSV/Parquet 无法逐字节保真，UI 需提示。
2. 嵌套消息编辑：
   - 为 `messages` 增加结构化编辑器（角色/内容/工具调用），保留 Raw JSON 作为兜底。
3. 匹配导航：
   - 维护当前匹配索引，提供上一处/下一处；滚动到行并高亮当前匹配。
4. 预览分页：
   - 复用 `IDatasetReader.ReadPageAsync`，增加页码/页大小；Cleaner diff 预览可保持固定样本。
5. CSV 类型推断：
   - 先做“展示层推断”（bool/int/double/date/string），写入仍按原文本，避免破坏数据。
6. 取消：
   - 为打开/分页引入可取消令牌，切换文件时取消旧请求并忽略过期结果。

**验收**

- 编辑保存行数守恒；未修改的 LF JSONL 行逐字节一致。
- 取消保存/切换文件不产生半成品，不覆盖原文件。
- 1M 行编辑/保存托管内存增量 ≤ 300MB。
- 跨格式保存有明确提示与失败回滚。

### 4.8 执行与恢复

**实现要点**

1. 恢复哈希：节点检查点保存输出 SHA256；恢复时比对已完成节点输出哈希。
2. 溢写字节阈值：在 `RowSpillBuffer` 增加 `SpillThresholdBytes`，与行数阈值取先到者。
3. 检查点配额：运行结束/启动时按目录大小清理最旧检查点；提供设置项。
4. 并发写检测：运行前收集所有目标路径（大小写不敏感），重复则拒绝启动或提示。
5. 执行历史保留：按数量/天数清理 `ExecutionStore`；UI 提供清理入口。
6. 取消门禁：CI 中增加宽松阈值（例如 3s），避免调度抖动导致假失败；严格 1s 保留为本机验收。

**验收**

- 恢复输出与不中断运行哈希一致（至少小 fixture）。
- 取消后目标文件不存在半成品，溢写目录为空。
- 检查点占用不超过配置配额。
- 同一目标路径并发写被拒绝并给出清晰错误。

### 4.9 UI / 画布

**实现要点**

1. 系统剪贴板：序列化复制的块/边为 JSON，写入 `IClipboard`；粘贴时校验 ID/类型。
2. 全局快捷键：使用 `KeyBindings` 或命令层统一处理；在 TextBox 获得焦点时跳过画布快捷键。
3. 自动布局选项：层间距、节点间距、是否按分组布局；布局结果作为一次 Undo。
4. 分栏宽度：保存到设置，启动时恢复。
5. 端口编辑：需要先决定是否允许 Recipe 内自定义端口；若不允许，删除“未实现”表述并明确只读。
6. 预设导出：增加“导出到…”文件选择器。

**验收**

- 复制/粘贴跨实例可用；粘贴后 ID 不冲突。
- 文本框输入时 `Ctrl+C/V/Z` 不被画布拦截。
- 分栏宽度重启后保持。
- 所有新增 UI 有 Headless 或手工验收记录。

### 4.10 文档与本地化

**实现要点**

1. 修正 README 画布“尚未实现”段落；以当前代码为准。
2. 修正 STATUS 2.9/2.10/3.2/3.3 的过期状态；已完成项移到“已完成”。
3. 修正 TODO 4.2/4.6/4.7 的矛盾；未勾选的 Phase 验收项逐项复核。
4. 统一测试计数口径：513 = 505 通过 + 7 gated + 1 Explicit。
5. 增加 CHANGELOG / Release Notes / 已知问题清单。
6. 运行日志、执行状态、统计摘要本地化：先抽出可本地化键，再逐步替换英文技术文本。
7. 截图更新：预设、数据编辑、校验分组、诊断导出、浅色主题。

**验收**

- README/STATUS/HANDOFF 中不再有与代码冲突的“未实现/已实现”描述。
- 所有文档链接可打开。
- 测试计数与 `--list-tests` 一致。
- 中英文切换后无缺失键（缺失时显示 key 视为失败）。

### 4.11 安全与沙箱

**实现要点**

1. API Key：DPAPI/Credential Manager；日志与异常消息中禁止输出 Key。
2. web_fetch DNS 重绑定：
   - 使用 `SocketsHttpHandler.ConnectCallback` 在校验后的 IP 上连接；或解析一次后固定 IP + Host 头。
   - 增加测试：解析到公网后再解析到私网的模拟场景。
3. 诊断包：
   - 日志/运行摘要增加敏感字段脱敏（至少 API Key、Authorization、常见 token 字段）。
   - 提供“包含/不包含运行内容”的导出选项。
4. 沙箱：
   - 文档明确协作式隔离边界；Windows Job Object 保持；非 Windows 至少保留超时/进程树终止。
5. 文件权限：
   - 设置/日志目录创建时收紧 ACL（Windows）。

**验收**

- `settings.json` 中无明文 Key。
- web_fetch 私网/回环/重绑定测试通过。
- 诊断包中不出现 Key/Authorization。
- 安全边界在 README 中有明确说明。

### 4.12 代码质量

**实现要点**

1. 删除未使用的转换器与 `ColorKey`/`HeaderColorKey`，或接入实际用途。
2. 逐条评估 `NoWarn`，先修低风险项，保留项写明原因。
3. `async void` 事件处理器统一 try/catch 或改为 `AsyncRelayCommand`；至少保证异常进入全局日志。
4. 大文件拆分：先拆纯逻辑（校验规则、运行阶段、设置绑定），避免一次性重构。

**验收**

- 构建 0 警告，无新增 `NoWarn`。
- 删除的公开成员无残留引用。
- 事件处理器异常不再出现“静默吞掉”。

---

## 5. 如何收尾

### 5.1 每个批次

1. 从本清单选择 P1/P2 项，建立小任务（建议每批 3–5 项）。
2. 先写/补测试，再改实现（或至少同提交）。
3. 运行：
   - `dotnet build LMTrainingDataStudio2/LMTrainingDataStudio2.slnx`
   - `dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx`
   - 涉及 UI 的项：运行应用并逐项点击验收。
4. 更新本文件勾选状态与“验证记录”。
5. 提交信息写清：范围、修复项、验证命令、结果。
6. 不在本文件回写归档的 Round 0–16 内容。

### 5.2 本轮结束

1. 所有 P1 关闭；P2 完成或写明延期原因与后续入口。
2. 运行一次完整验收：
   - build / test / Headless UI
   - 手动 UI 清单
   - 打包冒烟与安装验证（至少一次干净环境）
   - 1M/10M 基线（按需）
3. 更新 README、STATUS、CHANGELOG。
4. 将本文件移动到 `PLAN/Done/02-Bugfix1/HANDOFF.md` 并冻结。
5. 新建 `PLAN/TODO/02-<下一主题>/HANDOFF.md`，只保留未完成项。

### 5.3 冻结规则

- 归档文件只读，不再修改。
- 新发现的问题写入当前 HANDOFF，不回写归档。
- 每个 HANDOFF 顶部保留基线 commit，便于追溯。

---

## 6. 验收标准

### 6.1 总体验收（DoD）

> 归档说明：下列 3 项未在归档时闭合，已作为例外转交 02-Hardening（UI 逐项人工验收、错误上下文、干净机器安装记录），不作为本阶段“已完成”的依据。

- [x] `dotnet build` 0 警告、0 错误（分析器开启）。
- [x] `dotnet test` 全绿；gated 测试有明确运行记录（nightly + 本机记录）。
- [x] 每个 P1 缺陷有回归测试或自动化检查。
- [x] 每个新增/修改的 core 逻辑有成功 + 失败/边界测试。
- [ ] UI 入口真实可用，无占位按钮/空面板（仅 Headless 与手工抽查，未逐项点击）。
- [x] 长耗时操作可取消，UI 线程不阻塞。
- [ ] 错误信息包含路径、行号、节点名/ID、失败原因（节点级已覆盖，部分解析错误仍只有消息）。
- [x] 文件写入使用临时文件 + 原子替换，失败不破坏原文件。
- [x] 没有为未完成功能保留的静默 catch、空实现或死代码（Round 31 清理）。
- [x] README / STATUS / HANDOFF 与代码一致（Round 32 同步）。
- [ ] 构建、测试、打包、安装至少各有一份可复现记录（安装验证仍缺）。

### 6.2 分类验收

| 类别 | 验收标准 |
| --- | --- |
| 一致性缺陷 | `.recipe` 双击/命令行/选择器/安装器/README 五处一致；版本、图标、依赖、设置校验符合 4.1–4.3 |
| 测试 | 11.2 中 P1 项全部有自动化测试；覆盖率基线入库；本地化键一致 |
| CI | 格式、测试、覆盖率、打包冒烟在 PR 可见；nightly 可手动触发 |
| 配置 | 非法输入有提示；路径可用性检查；Key 不明文；设置可重置 |
| 节点 | 每个改动节点有成功/失败/边界测试；配置往返与校验同步 |
| 数据 | 行数守恒；LF JSONL 未修改行逐字节一致；取消无半成品；1M 行内存 ≤300MB |
| 执行 | 恢复哈希一致；取消后清理完整；无并发写冲突；检查点有配额 |
| UI | 手工清单逐项通过；快捷键无冲突；主题无硬编码颜色；分栏/预设行为符合预期 |
| 文档 | 无过期“未实现”描述；链接有效；测试计数准确；CHANGELOG 存在 |
| 安全 | Key 加密；web_fetch 重绑定防护；诊断包脱敏；沙箱边界有文档 |
| 质量 | 死代码清理；无新增 NoWarn；事件异常可追踪 |

### 6.3 验证记录模板

| 日期 | 批次/项 | 命令/步骤 | 结果 | 证据 |
| --- | --- | --- | --- | --- |
| 2026-11-17 | 扫描与归档 | `git diff --check`；`dotnet test --list-tests` | 通过；513 用例 | 本文件与归档文件 |
| 2026-11-17 | 二次扫描复核 | `dotnet test --list-tests`；`dotnet test --no-build`；全仓 grep 遗留标记 | 513 用例；505 通过 / 0 失败 / 7 跳过；生产代码 0 遗留标记 | 第 8 节 |
| 2026-11-17 | Batch 1：`.recipe` 一致性 | `dotnet build -c Release`；`dotnet test -c Release --no-build` | 0 警告；522 通过 / 0 失败 / 7 跳过 | `RecipeFileExtensionsTests` |
| 2026-11-17 | Batch 2：版本、图标、打包 | `dotnet build -c Release`；检查 Release deps.json 与 exe VersionInfo；PowerShell Parser 校验脚本 | 0 警告；FileVersion 1.0.0.0；Release 不含 Avalonia.Diagnostics；脚本语法通过 | 本文件 7.1/8.3.1 勾选 |
| 2026-11-17 | Batch 4：节点编辑器 | `dotnet build -c Release`；`dotnet test -c Release --no-build` | 0 警告；528 通过 / 0 失败 / 7 跳过 | `NodeEditorTests`、Cleanup 共享模板 Headless 测试 |
| 2026-11-17 | Batch 5：数据与恢复 | `dotnet build -c Release`；`dotnet test -c Release --no-build` | 0 警告；531 通过 / 0 失败 / 7 跳过 | 分页取消、跨格式保存、恢复哈希/指纹测试 |
| 2026-11-18 | Batch 7：启动/打包/设置 | `dotnet build -c Release`；`dotnet test -c Release`；`dotnet format --verify-no-changes` | 0 警告；550 通过 / 0 失败 / 7 跳过；格式通过 | `StartupLaunchTests`、`PackagingConsistencyTests`、`LocalizationConsistencyTests`、`SettingsPanelTests` |
| 2026-11-18 | Batch 8：CI 门禁 | 本地 `check-coverage.ps1`；发布脚本 `-Version 0.0.0`；示例再生成 + `git diff` | 总行 74.64%、Services/Commands 84.32%；FileVersion 0.0.0.0；示例无漂移 | `.github/workflows/ci.yml`、`scripts/check-coverage.ps1` |
| 2026-11-18 | Batch 10：SFT 导出与路径选择 | `dotnet build -c Release`；`dotnet test -c Release`；`dotnet format --verify-no-changes`；示例再生成 | 见第 12.3 节 | `SftExportExecutorTests`、`SftExportFieldSelectorTests`、`HeadlessUiTests` |
| 2026-11-18 | Batch 11：LLM 工具回填与 Anthropic 结构化输出 | `dotnet build -c Release`；`dotnet test -c Release`；`dotnet format --verify-no-changes`；示例再生成 | 见第 13.3 节 | `LlmToolLoopTests`、`LlmApiClientTests`、`LlmConfigEditorTests`、校验与 Headless 测试 |
| 2026-11-18 | Batch 12：数据工作台 `messages` 结构化编辑 | `dotnet build -c Release`；`dotnet test -c Release`；`dotnet format --verify-no-changes` | 见第 14.3 节 | `DatasetMessageEditorTests`、`DataWorkbenchEditTests`、Headless 面板测试 |
| 2026-11-18 | Batch 13：Tool Profile Enum/Default 与输出冲突校验 | `dotnet build -c Release`；`dotnet test -c Release`；`dotnet format --verify-no-changes` | 见第 15.3 节 | `ToolProfileReuseTests`、`RecipeValidationServiceTests`、Headless 面板测试 |
| 2026-11-18 | Batch 14：API Key DPAPI 加密 | `dotnet build -c Release`；`dotnet test -c Release`；`dotnet format --verify-no-changes` | 见第 16.3 节 | `SettingsStoreTests`（6 项新增）、DPAPI 落盘与迁移断言 |
| 2026-11-18 | Batch 15：web_fetch DNS 加固与设置通知 | `dotnet build -c Release`；`dotnet test -c Release`；`dotnet format --verify-no-changes` | 0 警告；630 通过 / 0 失败 / 7 跳过；格式通过 | 见第 17.3 节；`SandboxTests`、`AppSettingsServiceTests`、`DataWorkbenchViewModelTests`、`SettingsPanelTests` |
| 2026-11-18 | Batch 16：诊断脱敏与目录 ACL | `dotnet build -c Release`；`dotnet test -c Release`；`dotnet format --verify-no-changes` | 0 警告；638 通过 / 0 失败 / 7 跳过；格式通过 | 见第 18.3 节；`DiagnosticSecurityTests` |
| 2026-11-18 | Batch 18：输出写锁、溢写目录启动清理、CI 取消门禁 | `dotnet build -c Release`；`dotnet test -c Release`；`dotnet format --verify-no-changes`；`scripts/check-coverage.ps1` | 0 警告；661 通过 / 0 失败 / 7 跳过；覆盖率 76.46% / 84.9% | 见第 20.3 节 |
| 2026-11-20 | Batch 22–24：Round 32 节点与发布收尾 | `dotnet build -c Release`；`dotnet test -c Release`；`dotnet format --verify-no-changes`；`scripts/check-coverage.ps1`；`publish-win-x64.ps1 -Version 0.0.0` | 0 警告；736 通过 / 0 失败 / 7 跳过；`--list-tests` 744；覆盖率 77.55% / 85.07%；发布冒烟通过 | 见第 24.3 节；新增校验 Code/CSV 推断/Sampler 分层/SFT 拆片/流式/CI 测试 |

---

## 7. 详细工作池（167 项；第 8 节增补 38 项，合计 205 项）

> 以下为本轮扫描得到的完整清单。完成一项勾选一项；P1 优先，P2 尽量，P3 可延期但需保留入口。

### 7.1 一致性缺陷（建议先修）

- [x] **[缺陷][P1] `.recipe` 关联打不开**：`Program.IsRecipePath` 只接受扩展名 `.json`（`Program.cs:62`），但 MSIX 清单与 Inno 安装器注册的是 `.recipe`（`packaging/msix/AppxManifest.xml:47`、`packaging/inno/LMTrainingDataStudio.iss:48-53`），README 也宣称支持 `.recipe`。双击 `.recipe` 启动后不会加载文件。
- [x] **[缺陷][P1] `.recipe` 不在文件选择器里**：打开/保存 Recipe 的过滤器只有 `*.json`（`Views/MainWindow.axaml.cs:537-540`、`549-561`），用户无法选择 `.recipe`。
- [x] **[缺陷][P1] `.recipe.json` 关联宣传不实**：安装器只注册 `.recipe`；`.json` 仅加入 `OpenWithProgids`，不是默认关联；`README.md:18` 写“支持 `.recipe.json` / `.recipe` 文件关联”。
- [x] **[缺陷][P2] MSIX `-Version` 不生效**：`scripts/package-msix.ps1:16,37` 的版本只传给 `dotnet publish`，复制 `AppxManifest.xml` 时未替换 `Identity Version="1.0.0.0"`（`packaging/msix/AppxManifest.xml:13`）。
- [x] **[缺陷][P2] 版本号多源**：csproj 无 `Version`；`app.manifest:3`、`AppxManifest.xml:13`、`LMTrainingDataStudio.iss:7` 各自硬编码 1.0.0；诊断包中的 App version 也取自程序集默认版本。
- [x] **[缺陷][P2] Windows EXE 图标未嵌入**：csproj 没有 `<ApplicationIcon>`，只设置了 Avalonia 窗口图标（`Views/MainWindow.axaml:12`）；Inno 的快捷方式/文件关联图标指向 exe，会显示默认图标。
- [x] **[缺陷][P2] 安装器注释与代码相反**：`packaging/inno/LMTrainingDataStudio.iss:48` 写“command line accepts any .json/.recipe path”，但代码只接受 `.json`。
- [x] **[缺陷][P2] 发布包包含无用依赖**：`Avalonia.Diagnostics` 无条件引用（`LMTrainingDataStudio2.csproj:23`），代码未调用 `AttachDevTools`；建议改为 Debug 条件引用，减小发布体积。
- [x] **[缺陷][P2] 设置页路径输入无选择器/可写性检查**：CacheDirectory、RunOutputDirectory、RunTempDirectory 只能手输（`Views/MainWindow.axaml:398,442,448`），无 Browse 与可写性检查。
- [x] **[缺陷][P2] 数值设置无即时校验**：MaxCacheSizeMb、MaxParallelism、NodeRetryCount、SpillThresholdRows、GridSnapSize 均为 TextBox 直绑数值，非法输入无提示，只在 Apply 时 clamp（`ViewModels/MainWindowViewModel.cs:1345-1365`）。
- [x] **[缺陷][P2] 数据工作台在途分页不可取消**：`OpenDatasetAsync`/`LoadPageAsync` 使用 `CancellationToken.None`（`ViewModels/DataWorkbenchViewModel.cs:234,325`），切换/关闭文件无法取消旧分页请求。
- [x] **[缺陷][P2] Recipe 校验问题无错误码**：Round 32 为全部 `RecipeValidationService` 问题填充稳定 kebab-case Code（如 `block-name-duplicate`、`edge-duplicate`、`sft-export-splits-invalid`），属性面板问题行展示 Code；新增 `Validate_IssuesCarryStableCodes` 断言所有问题均有 Code。

### 7.2 测试缺口

- [x] **[测试][P1] `SingleInstanceService` 无任何自动化测试**：双进程端到端、管道超时、外部路径转发、脏文档确认均未覆盖。
- [x] **[测试][P1] 命令行/文件关联无测试**：`.json` 与 `.recipe` 参数解析、启动恢复、第二次实例转发无测试。
- [x] **[测试][P1] 打包脚本/清单无自动校验**：版本一致性、MSIX 必需资产、Inno 关联项、发布产物启动冒烟均未纳入测试或 CI。
- [x] **[测试][P2] 本地化键一致性测试**：`LocalizationConsistencyTests` 自动比对 zh-CN/en-US key 集合、非空值与占位符，并校验静态引用键存在性（当前 507/507）；动态拼接 key（`node.*`、`dialog.*`）仍无覆盖。
- [ ] **[测试][P2] 自动崩溃入口无测试**：仅覆盖 `CrashReportService.CreateReport` 与设置脱敏；AppDomain/TaskScheduler/UI 三个未处理异常入口未验证。
- [x] **[测试][P2] 中断恢复无逐字节/哈希比对**：Round 17 已加入输出 SHA256 与 Recipe 指纹校验（见 7.7）。
- [ ] **[测试][P2] 格式转换边界测试不足**：DPO/Pretrain 往返、多轮 Alpaca、额外字段保留/丢弃策略、ShareGPT tool_call 解析失败未覆盖（现有为 Alpaca 基础转换 + DPO/Pretrain 基础矩阵）。
- [ ] **[测试][P2] 索引缓存边界不足**：并发打开、非法指纹、超长行与容量清理后的读取行为覆盖较少。
- [ ] **[测试][P2] 10 类节点编辑器无逐项验收**：Headless/手工只覆盖主窗口、画布、主题、高亮、SFT 字段白名单；其余节点面板未逐项点击。
- [ ] **[测试][P2] 数据工作台大文件操作无手工验收**：跨页编辑/撤销/覆盖/另存/预览在 1M 行上的交互未逐项验证。
- [x] **[测试][P2] 覆盖率无采集与门禁**：DoD 中 `Services/`+`Commands/` ≥70%、全仓 ≥60% 的目标未度量。
- [x] **[测试][P2] Gated 性能/真实 provider 测试未进入 CI**：Round 32 新增 `.github/workflows/nightly.yml`（schedule + workflow_dispatch），按 `LMTDS_PERF_TESTS` 运行性能测试，并在手动触发时使用 Secrets 运行 provider 冒烟。
- [ ] **[测试][P3] 画布逐帧性能未测**：500 节点布局有测试，但 resize、边动画、拖拽的 UI 帧级耗时未计时。
- [ ] **[测试][P3] 无视觉回归基线**：截图由 Headless+Skia 一次性生成，未纳入常规测试，UI 改动的视觉回归无自动保障。

### 7.3 CI 与工程化

- [x] **[CI][P1] 无格式门禁**：CI 未运行 `dotnet format --verify-no-changes` 或等价检查。
- [x] **[CI][P1] 无覆盖率采集/上传/阈值**：与 7.2 的覆盖率目标对应。
- [x] **[CI][P1] 无打包/安装冒烟**：CI 不构建 MSIX/Inno，也不验证发布产物可启动。
- [x] **[CI][P2] 无发布工作流**：Round 32 新增 `.github/workflows/release.yml`，tag `v*` 或手动触发，校验版本与 `Directory.Build.props` 一致，执行格式/构建/测试/发布/MSIX/Inno 并创建 GitHub Release；签名仍为可选外部步骤。
- [x] **[CI][P2] 无 nightly/gated job**：Round 32 新增 `nightly.yml`，schedule + workflow_dispatch 运行性能与真实 provider 冒烟。
- [x] **[CI][P2] 无依赖安全扫描**：Round 32 在 CI 增加 `dotnet list package --vulnerable --include-transitive` 审计 job（当前上游传递依赖有 High 级公告，暂为 advisory），并加入 `.github/dependabot.yml` 跟踪 NuGet 与 GitHub Actions。
- [ ] **[CI][P2] 依赖版本可重复性弱**：尝试启用 `RestorePackagesWithLockFile` 后，Debug-only `Avalonia.Diagnostics` 导致 Debug/Release 锁文件依赖集不一致，锁定模式无法同时通过；本轮撤回锁文件，保留审计与 Dependabot。若后续移除配置相关包，可重新引入。
- [x] **[CI][P2] 版本号未单一来源**：见 7.1；构建、MSIX、Inno、诊断包各自取值。
- [ ] **[CI][P3] 无 PR 测试摘要/注解**：仅上传 TRX artifact，PR 页面看不到失败用例摘要。
- [x] **[CI][P3] 无 concurrency/timeout/cache**：重复推送会并行跑完整流程，NuGet 未缓存。
- [ ] **[CI][P3] 无 PR/Issue 模板与贡献指南**（低优先）。
- [x] **[工程][P2] 分析器 `NoWarn` 清单较大**：Round 31 用 `-p:NoWarn=` 全量构建逐条审计，55 项缩减到 7 项并注明理由；CA1707 移入测试项目，CA1068/CA1725/CA1806/CA1869/CA1310 已改代码消除。
- [x] **[工程][P3] Release 仍引用 `Avalonia.Diagnostics`**：Round 17 已改为 `Condition="'$(Configuration)' == 'Debug'"`。
- [ ] **[工程][P3] 无 BenchmarkDotNet 专项基准**：仅有 NUnit gated 大文件测试。

### 7.4 配置与设置

- [x] **[配置][P2] API Key 加密存储**：Round 24 已使用 Windows DPAPI（CurrentUser）加密落盘并迁移旧明文；见 7.10。
- [x] **[设置][P2] 设置变更通知**：Round 25 新增 `IAppSettingsService` / `AppSettingsService`（`Current` + `Changed`），主窗口在 Apply 成功后发布变更；数据工作台订阅事件并重建读取器工厂、批量转换器与四个子服务。按需创建的服务（预览、索引缓存、每次运行执行器）在创建时读取 `Current`，因此始终使用最新设置。
- [ ] **[设置][P2] Settings/Index Cache 无 schema 版本与迁移策略**：Recipe 有 schemaVersion，其余持久化没有。
- [ ] **[设置][P2] 无“恢复默认设置”入口**。
- [ ] **[设置][P2] 无日志级别/来源筛选**：日志面板只显示文本，滚动文件日志也没有 UI 筛选。
- [ ] **[设置][P2] 无全局网络配置**：代理、全局 HTTP 超时、重试策略只能按节点/环境变量配置。
- [ ] **[设置][P3] 无执行历史/崩溃报告保留策略**：运行目录、`%LOCALAPPDATA%\LMTrainingDataStudio\crashes` 会持续累积。
- [ ] **[设置][P3] `app.manifest` 未显式声明 DPI/longPathAware**：依赖运行时/系统默认，超长路径与混合 DPI 场景未验证。
- [ ] **[设置][P3] `.editorconfig` 强制 CRLF 与 `.gitattributes` 的 `text=auto`、JSONL LF 约定需确认**，避免跨平台行尾抖动。

### 7.5 节点模块

#### Seed

- [x] **[节点/P1] Streaming 开关 UI 未暴露**：`SeedConfigEditorViewModel` 只保留 `_streaming` 原值（`ViewModels/SeedConfigEditorViewModel.cs:18,43`），且仅 JSONL + `UseIndexCache=false` 生效。
- [ ] **[节点/P2] 节点内过滤/列映射/Limit/Offset 未做**。
- [ ] **[节点/P2] 多文件/目录数据源、HuggingFace/HTTP 源未做**。
- [ ] **[节点/P2] CSV 编码/分隔符/类型推断选项未暴露**：CSV 值当前全部按字符串读取。
- [ ] **[节点/P3] 节点内预览/统计未做**：目前依赖 Data 视图。

#### LLM

- [x] **[节点/P1] `tool_calls` 只记录不执行**：已加 `MaxToolRounds`（默认 0 保持只记录）与 `ToolMode`（模拟/真实沙箱）；工具结果作为 tool 消息回灌并再次调用模型，超轮次该行记失败，未知工具/执行错误以 JSON 错误回灌。输出完整 assistant/tool 轨迹；OpenAI Chat/Responses 与 Anthropic 均支持工具消息往返。
- [x] **[节点/P2] Anthropic 无结构化输出约束**：`ResponseSchema` 在 Anthropic 下通过强制 `structured_output` 工具实现，响应 input 作为内容返回；同时配置 tools + schema + 工具回填时校验给出警告。
- [x] **[节点/P2] LLM 节点无流式输出**：Round 29 新增 `LlmBlockConfig.Streaming`；Round 32 补齐 OpenAI Responses（文本/函数调用/usage 事件）与 Anthropic（text_delta/input_json_delta/message_delta）SSE，均沿用超时与重试，面板说明更新为三种 provider 均支持。
- [x] **[节点/P2] 无模型列表/探测**：Round 29 新增 `LlmModelCatalog`（OpenAI `/models`、Anthropic `/v1/models`）与 LLM 面板模型下拉/刷新按钮；密钥与 Base URL 复用 `ILlmCredentials`，失败在面板内提示。
- [ ] **[节点/P3] 编辑期无 Prompt 语法校验与渲染预览**：仅在执行期校验。
- [ ] **[节点/P3] 多模态消息未支持**。

#### Expression

- [ ] **[节点/P2] 无自定义函数/类型转换**：仅 Fluid 内置能力。
- [ ] **[节点/P2] 模板错误即节点失败**：无跳过/写失败文件策略。

#### Validator

- [x] **[节点/P1] Cleanup 规则 UI 缺失**：`ValidatorConfigEditorViewModel` 只保留 `_cleanup` 不提供编辑（`ViewModels/ValidatorConfigEditorViewModel.cs:54,80`），仍走 Config JSON。
- [ ] **[节点/P2] 无失败率/规则结果摘要面板**：只有日志与 reject 文件。
- [ ] **[节点/P2] `script` 为 Fluid 布尔表达式**，不是任意脚本；超时只停止等待，不强制终止底层 CPU 渲染。

#### Sampler

- [x] **[节点/P2] count 模式不支持分层**：Round 32 为 count 模式增加按 `StratifyField` 分组的 reservoir，读取后按组大小用最大余数分配总数量，行数守恒并按源行号输出；抽签种子改为稳定的 FNV-1a 哈希。
- [ ] **[节点/P2] 无切分内再混洗/保序选项**。
- [ ] **[节点/P2] 端口动态变化后旧连线不即时失效**：指向已删除端口的边只在下次校验时报错。

#### Tool Profile

- [x] **[节点/P2] Enum/Default 值 UI 未暴露**：Round 23 在参数树中新增枚举（逗号分隔字符串或 JSON 数组）与默认值（JSON）编辑，与 JSON Schema 双向同步；非法输入在面板显示错误且不覆盖上一次有效 Schema。
- [ ] **[节点/P2] 无 MCP（stdio/HTTP）工具发现与调用**。
- [ ] **[节点/P2] 无共享工具库/版本管理/鉴权**：profile 仍嵌在 Recipe 内。
- [ ] **[节点/P3] 仅 schema 复用**：缺少工具调用请求侧复用与凭据管理。

#### Query Source

- [ ] **[节点/P2] 无随机/模型生成 query**。
- [ ] **[节点/P2] 无 query 去重/权重/元数据扩展**。

#### Simulation

- [ ] **[节点/P2] 流式 delta 仅 OpenAI Chat**：Responses/Anthropic 无流式。
- [ ] **[节点/P2] 限速是 query 启动间隔近似**，不是严格 token bucket。
- [ ] **[节点/P2] 无容器/命名空间级隔离**；非 Windows 无内存/进程数限制。
- [ ] **[节点/P2] 无 MCP 工具接入**。
- [ ] **[节点/P3] 无工具结果裁剪/摘要策略 UI**：只有输出上限与超时。

#### Cleaner

- [x] **[节点/P1] 正则规则列表 UI 缺失**：仍走 Config JSON（`ViewModels/CleanerConfigEditorViewModel.cs` 只有 5 个布尔开关）。
- [ ] **[节点/P2] 坏 JSON 直接失败**：无坏行跳过与坏行报告。
- [ ] **[节点/P3] 无自定义字段/JSONPath 清洗**。

#### SFT Export

- [x] **[节点/P2] 无追加模式**：已支持安全追加（复制临时副本 → 追加 → 成功原子替换，失败保留原文件）。
- [x] **[节点/P2] 无目标格式选择**：已支持 Auto/OpenAI/ShareGPT/Alpaca 转换；DPO/Pretrain 直出仍走数据工作台。
- [x] **[节点/P2] 无按数量/比例拆分多文件**：已支持按行数分片 `name.part001.jsonl` 与 `name:ratio` 比例拆片；比例拆片复用 `SamplerSplitPlan`，输出目录取 OutputPath 所在目录，所有分片临时写完后再原子替换，并注册到输出目标锁。
- [x] **[节点/P2] 无并发写同一目标文件的检测/提示**：Round 23 在 Recipe 校验中收集 SFT 输出、LLM/Simulation 失败样本、Validator reject 文件与 Sampler 分片文件，大小写不敏感地拒绝重复目标；同一 Sampler 输出目录也视为冲突。

### 7.6 数据工作台与数据治理

- [x] **[数据/P1] 跨格式保存未支持**：编辑保存只允许源格式，`DatasetEditService.SaveAsync` 对不同目标格式直接抛 `DatasetFormatException`。
- [x] **[数据/P2] 嵌套消息/角色级表单未做**：Round 22 新增 `messages` 结构化编辑器（角色/内容/tool_calls、增删与上下移），保留其余消息字段与 Raw JSON 兜底；其他嵌套结构仍按 JSON 文本编辑。
- [x] **[数据/P2] 搜索匹配项导航未做**：只有命中高亮，没有上一处/下一处。
- [x] **[数据/P2] 预览窗口固定 50 行**：`SamplePreviewService.DefaultMaxRows = 50`，无分页/全文搜索。
- [x] **[数据/P2] CSV 类型推断与列类型展示**：Round 32 新增 `CsvTypeInference`（integer/number/bool/date/json/string/empty），数据工作台打开 CSV 时按首屏最多 256 个值推断并在字段表头显示；仅展示，不改写源文本。
- [x] **[数据/P3] 无列排序/筛选/列宽持久化**（191）：Round 30 新增字段表格视图、全量字段筛选/排序（含数值比较与正则）、列宽拖拽与按字段名持久化；视图激活期间禁用行编辑，搜索/校验跳转自动清除视图。
- [ ] **[数据/P2] 多文件/目录数据源、HuggingFace/HTTP 未做**。
- [ ] **[数据/P2] 字节级一致仅保证 LF JSONL**：CSV/Parquet 保存会重写整表。
- [x] **[数据/P2] 数据工作台在途分页不可取消**：见 7.1。
- [ ] **[数据/P2] CSV/Parquet 1M 行质量操作基线未做**：现有质量基线为 JSONL。
- [ ] **[数据/P2] 10M 行质量/转换基线未实测**：仅提供入口。
- [ ] **[数据/P3] Alpaca 多轮转换不丢轮次/不丢 tools**：待补齐与测试。
- [ ] **[数据/P3] 转换额外字段/元数据策略未明确**：保留还是丢弃未声明。
- [ ] **[数据/P3] ShareGPT `tool_call` 解析失败未上报**：存在静默降级风险。
- [ ] **[数据/P3] `AttachTools` 可能插入空 system 消息**：待确认并修正。
- [ ] **[数据/P3] 数据集统计摘要未本地化**。

### 7.7 执行与恢复

- [x] **[执行/P2] 恢复未做逐字节/哈希比对**：只验证行数与内容；Round 13 验收目标中的“哈希一致”未落地。
- [x] **[执行/P2] 溢写阈值只按行数**：已补 `SpillThresholdBytes`，与行数阈值取先到者（Round 27）。
- [x] **[执行/P2] 检查点无磁盘配额**：启动与运行结束按运行输出目录大小清理最旧检查点，保护当前运行（Round 27）。
- [x] **[执行/P2] 无并发写同一目标文件的检测**：同一 Recipe 内的节点级重复目标已在 Round 23 校验；Round 28 新增运行期跨进程锁——每个输出文件同级 `.lmtds.lock` 以 `FileShare.None` 独占打开，运行前获取、结束或失败后释放，冲突时拒绝启动并列出目标。
- [x] **[执行/P2] 无执行历史批量清理/保留策略**：`ExecutionStore.Prune` 按数量/天数清理，启动自动应用，Executions 提供手动入口（Round 27）。
- [x] **[执行/P2] 取消 1s 断言依赖本机调度**：Round 28 起测试读取 `LMTDS_CANCEL_BUDGET_MS`（本机默认 1000ms，CI 设 3000ms），CI 以 3s 作为取消门禁；本机仍按 1s 验收。
- [ ] **[执行/P3] 10M 行执行/质量/转换基线未实测**：入口已有，结果未记录。
- [ ] **[执行/P3] 执行日志/状态/摘要未本地化**：保持英文技术文本。
- [ ] **[执行/P3] 无执行指标图表**：LiveCharts2/OxyPlot 未接入。
- [ ] **[执行/P3] `Task.Run` + UI 回调异常边界未逐项复核**：当前主要依赖全局异常处理。

### 7.8 编辑器 UI / 画布

- [ ] **[UI/P2] 系统剪贴板/跨实例粘贴未做**：当前复制/粘贴使用画布内部 `_clipboardEdges`，不经过系统剪贴板。
- [ ] **[UI/P2] 快捷键未全局化**：画布与窗口各自处理，焦点在文本框等控件时可能冲突。
- [ ] **[UI/P2] 自动布局无间距/分组选项**。
- [ ] **[UI/P2] 左右分栏宽度不持久化**：每次启动恢复默认。
- [ ] **[UI/P2] 端口增删/改名/改类型未做**：属性面板只读展示。
- [ ] **[UI/P2] 自定义预设不能导出到任意路径**：只保存在本机预设目录。
- [ ] **[UI/P2] 10 个节点面板未逐项人工验收**：见 7.2。
- [ ] **[UI/P3] 边动画是虚线流动，不是路径几何过渡**。
- [ ] **[UI/P3] 标签避让为贪心候选位置**：不保证全局最优。
- [ ] **[UI/P3] 无键盘导航/无障碍验收**（若作为发布目标）。
- [ ] **[UI/P3] 状态栏/日志/统计摘要本地化**：见 7.9。

### 7.9 本地化与文档

- [x] **[文档/P1] README 画布“尚未实现”段落过期**：`README.md:569-574` 仍列节点尺寸手柄、边动画、标签避让、Undo/Redo 菜单、浅色主题为未实现，均已在 Round 16 完成。
- [x] **[文档/P1] STATUS 多处状态过期**：`Cleaner` diff（`STATUS.md:278`）、SFT 预览（`STATUS.md:294`）、Undo 菜单/尺寸手柄/边动画（`STATUS.md:359,397`）、浅色主题/内存刷新（`STATUS.md:399,401`）、Round 15 的 LLM/Validator/Sampler/Simulation/Query Source 能力（`STATUS.md:387-391`）仍标 `[ ]`。
- [x] **[文档/P2] 历史 TODO 内部矛盾**：4.2 先写“级别/错误码筛选未做”（旧 TODO 4.2）后写 Round 14 已完成；4.6 仍写硬编码颜色为 `[部分]`；4.7 仍写 `ValidationIssue` 缺错误码，实际模型已有 `Code`/`LineNumber`。（归档文件只读，修正记录写在本文件。）
- [x] **[文档/P2] Phase 1–5 验收清单未回填**：归档 TODO 中 Phase 1–5 的验收项仍为未勾选，与实际完成状态不符；在本文件记录复核结论即可，不回写归档。
- [x] **[文档/P2] 测试基线口径不完整**：README/STATUS 写 512（505 通过 + 7 gated），实际 `--list-tests` 为 513（另有 1 个 `ExampleRecipeTests.WriteExamples` 的 `[Explicit]` 测试）。
- [x] **[文档/P2] README “OpenAI / ShareGPT / Alpaca 完整批量互转”表述过强**：仍存在多轮 Alpaca、额外字段、ShareGPT tool_call 解析等缺口。
- [x] **[文档/P2] 无 CHANGELOG / Release Notes / 已知问题清单**：Phase 5 收尾要求未落地。
- [ ] **[文档/P3] README 截图仅 2 张**：预设、数据编辑、校验分组、诊断导出、浅色主题等新 UI 无对应截图。
- [ ] **[文档/P3] 无用户手册/帮助内容**：除节点 Node Guide 外无操作文档。
- [ ] **[文档/P3] 运行日志/执行状态/统计摘要仍为英文**：与 zh-CN/en-US 界面不一致。

### 7.10 安全与沙箱

- [x] **[安全/P1] API Key 明文落盘**：`settings.json` 未加密；诊断包已脱敏，但本机文件仍需保护。—Round 24 已用 Windows DPAPI（CurrentUser）加密，旧明文在加载时自动迁移；非 Windows 不持久化 Key。
- [x] **[安全/P2] web_fetch 存在 DNS 重绑定窗口**：Round 25 改为解析一次并校验全部地址，随后用 `SocketsHttpHandler.ConnectCallback` 只连接这些 IP；主机名不再二次解析。任一解析结果为私网/回环即拒绝（含混合结果），仍禁跟随重定向。
- [x] **[安全/P2] 诊断包只脱敏设置**：Round 26 新增 `DiagnosticRedactor`，对 summary、日志与执行摘要统一脱敏：`Authorization`/`Bearer` 头、`apiKey`/`token`/`secret`/`password` 等 JSON 字段、查询参数、`sk-`/`gh*_`/`AKIA` 常见前缀，以及设置与环境变量中的 Key 字面量；主流程输出 `[REDACTED]`。
- [ ] **[安全/P2] 沙箱是协作式隔离**：同用户运行，无容器/命名空间；非 Windows 无内存/进程数限制。
- [x] **[安全/P3] settings.json/日志目录 ACL 未收紧**：Round 26 新增 `AppDirectorySecurity`，Windows 上首次创建目录时移除继承权限，仅保留当前用户、SYSTEM 与 Administrators 完全控制；已接入设置、日志与崩溃报告目录，非 Windows 与失败场景仅记录告警。
- [ ] **[安全/P3] web_fetch 无域名白名单/代理策略**：仅阻断私网/回环。
- [ ] **[安全/P3] 崩溃报告无保留/清理策略**：会持续累积。
- [ ] **[安全/P3] 安装包默认未签名**：需证书与签名流程；MSIX Publisher 为占位 `CN=LMTrainingDataStudio`。

### 7.11 代码质量与死代码

- [x] **[质量/P2] `BlockTypeToColorConverter` 未被引用**（`Converters/BlockTypeToColorConverter.cs`）：Round 31 删除文件。
- [x] **[质量/P2] `InverseBoolConverter` 未被引用**（同文件）：Round 31 随文件删除。
- [x] **[质量/P2] `BlockTemplateViewModel.ColorKey` 未被引用**（`ViewModels/BlockTemplateViewModel.cs:48`）：Round 31 删除。
- [x] **[质量/P2] `BlockNodeViewModel.HeaderColorKey` 未被引用**（`ViewModels/BlockNodeViewModel.cs:298`）：Round 31 删除。
- [x] **[质量/P2] 分析器 `NoWarn` 清单未清理**：见 7.3；Round 31 完成审计。
- [ ] **[质量/P3] CA1001 可释放字段未接线**：6 个视图模型与 `RequestRateLimiter`、`AutoFixWriter` 持有 CTS/SemaphoreSlim/MemoryStream，当前依赖所有者生命周期，未实现 `IDisposable`（Round 31 保留抑制）。
- [ ] **[质量/P3] `async void` 事件处理器较多**：28 处，异常主要依赖全局处理器；建议逐项确认边界。
- [ ] **[质量/P3] `Task.Run` + UI 回调模式**：异常传播路径需复核。
- [ ] **[质量/P3] 大文件职责偏重**：`MainWindowViewModel`（约 1420 行）、`RecipeValidationService`（约 970 行）、`RecipeRunner`（约 840 行），可择机拆分。

### 7.12 未排期 backlog（来自 STATUS 6.1 与 README 路线图）

- [ ] Tool Profile MCP（stdio/HTTP）、共享工具库与鉴权。
- [ ] 容器级沙箱隔离、非 Windows 资源限制。
- [ ] HuggingFace/HTTP 数据源。
- [ ] 插件化节点、云端执行。
- [ ] 实时图表（LiveCharts2/OxyPlot）与执行指标面板。
- [ ] 自动更新/版本检查（未规划）。
- [ ] 更多界面语言（当前 zh-CN/en-US）。
- [ ] BenchmarkDotNet 专项基准。

### 7.13 建议修复顺序

1. **一致性缺陷 + 文档同步**：7.1 全部、7.9 的 P1/P2；先保证 `.recipe` 打开、版本/图标、README/STATUS 与实现一致。
2. **CI 与测试门禁**：7.3 的格式/覆盖率/打包 job，7.2 的单实例、命令行、本地化、打包校验。
3. **节点与数据边角**：7.5 的 P1/P2（Seed Streaming、Validator Cleanup、Cleaner 正则、LLM tool_calls、SFT 格式/追加），7.6 的跨格式编辑/匹配导航/预览分页。
4. **执行与发布验证**：7.7 的哈希恢复/保留策略，7.10 的密钥与 web_fetch 加固，7.1/7.3 的安装、签名、版本单源。

---

## 8. 全仓扫描复核与增补（2026-11-17）

> 本节是第二次全仓扫描的结论。完整未完成清单 = **第 7 节 167 项 + 本节 38 项增补（168–205）**。
> 扫描范围：`LMTrainingDataStudio2/`（生产代码、测试）、`.github/`、`scripts/`、`packaging/`、`examples/`、`docs/`、`README.md`、`PLAN/*.md`。

### 8.1 扫描基线

| 检查 | 命令/方式 | 结果 |
| --- | --- | --- |
| 用例枚举 | `dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx --list-tests` | 513 个用例，命令退出码 0；505 常规 + 5 性能 gated + 2 provider gated + 1 Explicit |
| 完整测试 | `dotnet test LMTrainingDataStudio2/LMTrainingDataStudio2.slnx --no-build` | 505 通过，0 失败，7 跳过（5 性能 + 2 provider）；`WriteExamples` 为 `[Explicit]` 未计入运行总数 |
| 遗留标记 | 全仓 grep `TODO` / `FIXME` / `HACK` / `NotImplementedException` | 生产代码 0 处；仅保留有意的 `NotSupportedException` 分支（不支持的类型/转换器/标记扩展） |
| 本地化键 | `Localization/*.json` | 归档时 zh-CN 432、en-US 432（当前 507/507）；`LocalizationConsistencyTests` 自动比对 key 集合、非空值与占位符；动态拼接 key（`node.*`、`dialog.*`）无覆盖 |
| 主题硬编码 | `.axaml` 中 `#RRGGBB` / 命名颜色 | 除 `Themes/Colors.axaml` 外仅剩 `Transparent`；Round 16 的“完整浅色主题”实际已完成，STATUS/README 仍有过期描述 |
| 测试数据 | `git status --short` | 工作区无未提交改动（本文件修改前） |
| 示例 | `examples/*.recipe.json` | 6 个示例文件存在；生成测试 `ExampleRecipeTests.WriteExamples` 为 `[Explicit]`，CI 不校验示例是否漂移 |

**结论**：项目不是“有大量半成品桩代码”的状态；本轮缺口集中在**一致性、门禁、节点/数据能力边角、恢复与安全加固、文档同步**。现有 7.x 清单基本准确，以下为复核后确认遗漏或需要单独跟踪的项。

### 8.2 分类索引

| 分类 | 现有清单 | 本次增补 |
| --- | --- | --- |
| 一致性缺陷 | 7.1（12 项） | 8.3.1（168–173） |
| 测试 | 7.2（12 项） | 8.3.2（174–179） |
| CI | 7.3（12 项） | 8.3.3（180–182） |
| 配置 | 7.4（9 项） | 8.3.4（183–186） |
| 节点模块 | 7.5（Seed/LLM/Expression/Validator/Sampler/Tool Profile/Query Source/Simulation/Cleaner/SFT，共 41 项） | 8.3.5（187–190） |
| 数据工作台 | 7.6（15 项） | 8.3.6（191） |
| 执行恢复 | 7.7（10 项） | 8.3.7（192–193） |
| 编辑器 UI | 7.8（13 项） | 8.3.8（194–196） |
| 本地化文档 | 7.9（12 项） | 8.3.9（197–199） |
| 安全 | 7.10（8 项） | 8.3.10（200） |
| 代码质量 | 7.11（7 项） | 8.3.11（201–203） |
| 未排期 backlog | 7.12（8 项） | 8.3.12（204–205） |
| 修复顺序 | 7.13 | 8.4（修订版） |

### 8.3 本次扫描增补项（168–205）

#### 8.3.1 一致性缺陷（168–173）

- [x] **168 [P1] 保存链强制 `.json`**：`DefaultExtension = "json"`（`Views/MainWindow.axaml.cs:529`）、`GetSuggestedFileName` 只判断 `.json` 并追加 `.json`（`Views/MainWindow.axaml.cs:549-561`）。用户输入 `foo.recipe` 会得到 `foo.recipe.json`；与 7.1 的 `.recipe` 打开/关联修复必须一起改。
- [x] **169 [P2] 第二实例转发失败无反馈**：`Program.Main` 调用 `SingleInstanceService.TrySendToPrimary(recipePath)` 但忽略返回值（`Program.cs:35-41`）；主实例管道不可达时第二个进程直接退出，用户看不到任何错误。
- [x] **170 [P3] 命令行只处理第一个 Recipe 参数**：`Array.Find(args, IsRecipePath)`（`Program.cs:30`）；多文件/批量打开会被静默忽略。
- [x] **171 [P3] MSIX 生成 `Wide310x150Logo.png` 但清单未引用**：脚本生成该资产（`scripts/package-msix.ps1:69`），`packaging/msix/AppxManifest.xml` 只引用 Square/StoreLogo；宽磁贴可能缺失或资产无效。
- [x] **172 [P3] 发布 zip 文件名不含版本**：`scripts/publish-win-x64.ps1:16-20` 固定输出 `win-x64.zip`；无法区分多个版本产物。
- [x] **173 [P2] Inno 打包脚本无 `-Version` 参数**：`scripts/package-inno.ps1` 只接收 `Configuration` / `SkipPublish`，版本继续由 `LMTrainingDataStudio.iss:7` 硬编码；与 7.1 的“版本单一来源”配套修复。
- [ ] **补充**：7.1 的 `.recipe` 打开/选择器/安装器/README 四项仍全部有效；`Program.IsRecipePath` 当前只比较 `.json`（`Program.cs:62-64`）。

#### 8.3.2 测试缺口（174–179）

- [x] **174 [P2] 数据工作台分页取消/过期结果无测试**：`OpenDatasetAsync` / `LoadPageAsync` 使用 `CancellationToken.None`（`ViewModels/DataWorkbenchViewModel.cs:234,325`），且没有请求代际标识；切换文件时旧页可能回写新文件。需要“取消旧请求 + 忽略过期结果”的测试。
- [x] **175 [P2] 中断恢复未校验 Recipe/节点配置指纹**：`RehydrateCompletedNodes` 只按 block ID、输出文件存在和行数复用（`Services/RecipeRunner.cs:353-398`）；修改 Recipe 后恢复可能复用旧配置的输出。需要 recipe/config 哈希或版本指纹。
- [ ] **176 [P2] 本地化测试不扫描硬编码英文**：现有 `LocalizationTests` 只测服务、节点目录和预设，不检查 VM/代码后置里的用户可见英文字符串（见 197）。建议增加静态/反射扫描测试。
- [x] **177 [P2] 设置 UI 校验无测试**：只测 `SettingsStore.Load` 的非法值回退；TextBox 非法数值、不可写目录的即时提示没有测试。
- [x] **178 [P3] 示例文件漂移无检查**：`ExampleRecipeTests.WriteExamples` 为 `[Explicit]`（`Tests/ExampleRecipeTests.cs:36`）；CI 不验证 `examples/` 与 `RecipePresetCatalog`/默认配置一致。
- [ ] **179 [P3] 无跨平台构建冒烟**：CI 只有 Windows；Avalonia/net9.0 核心逻辑没有 Linux/macOS 构建验证。

#### 8.3.3 CI 与工程化（180–182）

- [ ] **180 [P3] 无跨平台 CI job**：与 179 配套；沙箱/Job Object 测试可条件跳过，构建与纯逻辑测试可跨平台跑。
- [x] **181 [P3] 无 Debug/Release 双配置构建**：CI 只构建 Release（`.github/workflows/ci.yml`）；Debug 下的条件编译/诊断引用没有持续验证。
- [x] **182 [P3] CI 不校验示例与显式测试**：`ExampleRecipeTests.WriteExamples` 不会在 PR 中运行，示例 Recipe 可能与代码/预设漂移。

#### 8.3.4 配置与设置（183–186）

- [ ] **183 [P3] 无设置导入/导出**：迁移机器或备份只能手动复制 `%APPDATA%\LMTrainingDataStudio\settings.json`。
- [ ] **184 [P3] 无“打开日志/配置目录”入口**：诊断导出已有，但日常排障需要手动找 `%LOCALAPPDATA%` / `%APPDATA%`。
- [ ] **185 [P3] 新节点默认配置不可改**：`BlockConfig.CreateDefault` 写死默认值（`Models/BlockConfig.cs:21-37`），设置页不能改默认 Seed 路径、默认并发、默认导出字段等。
- [ ] **186 [P3] 无 API Key 测试连接**：Key 错误只能在首次运行时发现；可在设置页加“测试连接”（只读请求）并区分认证失败/网络失败。

#### 8.3.5 节点模块（187–190、206）

- [x] **187 [P2] SFT 字段白名单是逗号文本框（校验已补，字段选择器未做）**：`SftExportConfigEditorViewModel.FieldsText` 直接按逗号拆分（`ViewModels/SftExportConfigEditorViewModel.cs:39-44`），面板只有 TextBox（`Views/PropertyPanelView.axaml:943-947`）；无字段选择器、无样本字段提示、拼写错误会静默输出缺字段。建议从输入样本读取顶层字段做多选。—Round 20 已加“从样本读取字段”、复选列表、全选/清空与文本框双向同步；读取失败在面板内提示。
- [x] **188 [P3] LLM `response_schema` 无编辑期校验/格式化**：面板是纯 TextBox（`Views/PropertyPanelView.axaml:221`），没有 JSON 语法校验、格式化或从 Schema 生成示例；错误只在运行时暴露。
- [ ] **189 [P3] Sampler splits 是纯多行文本**：`SplitsText` 只有整体解析/错误提示（`ViewModels/SamplerConfigEditorViewModel.cs:110-130`），没有逐行增删、排序、重命名和单行预览。
- [ ] **190 [P3] 路径输入体验不统一**：Seed 与 SFT 有 Browse/可写性检查；Validator reject 路径（`Views/PropertyPanelView.axaml:401`）、LLM failure 路径（`:229`）、Simulation failure 路径（`:671`）、Sampler 输出目录（`:451`）仍是纯文本，且没有目录/文件存在性提示。—Round 20 已为以上四处接入 Browse；存在性提示拆分到 206。
- [ ] **206 [P3] 输出路径存在性/可写性提示**：相对路径在运行时按进程工作目录解析，编辑期尚未提示父目录是否存在或可写；需要先统一“相对路径基准”再给出准确提示。

#### 8.3.6 数据工作台（191）

- [x] **191 [P3] 无列排序/筛选/列宽持久化**：Data 工作台当前只有搜索与分页；`DataWorkbenchViewModel` 无按列排序、条件筛选、列宽保存/恢复，CSV/Parquet 大表定位效率受限。—Round 30 新增字段表格视图、全量筛选/排序与列宽持久化（详见第 22 节）。

#### 8.3.7 执行与恢复（192–193）

- [x] **192 [P2] 崩溃后溢写/检查点临时目录无启动清理**：Round 28 新增 `SpillDirectoryMaintenance`，启动时删除溢写根下所有 run-scoped 残留目录（正常结束由 `finally` 删除），删除失败的目录留待下次重试；检查点目录由 Round 27 的 `CheckpointQuota` 在启动时按配额约束。
- [ ] **193 [P3] 无整体运行超时/最大时长**：只有节点级 `TimeoutSeconds`；一个卡住的 DAG 可能需要人工取消。可加运行级超时设置与 UI 提示。

#### 8.3.8 编辑器 UI（194–196）

- [x] **194 [P3] 最近文件无清空/失效路径清理**：`File → Recent` 只追加/去重（`ViewModels/MainWindowViewModel.cs:696-706`），没有“清空最近文件”和失效路径自动移除。
- [x] **195 [P3] Executions 输出目录无“打开文件夹”**：只显示路径文本（`Views/ExecutionsView.axaml:126-129`），排障时需要手动复制路径。
- [ ] **196 [P3] 无节点分组/注释框**：画布没有 Group/Sticky Note/Comment 节点或分组框；大 Recipe 的可读性和布局维护成本高。

#### 8.3.9 本地化与文档（197–199）

- [ ] **197 [P2] 大量用户可见文本硬编码英文**：例如 `Untitled Recipe`（`ViewModels/MainWindowViewModel.cs:35,618`）、`Recipe history updated`（`:825`）、`Connection added`（`:992`）、`Execution cancelled`（`:1034,1074`）、`Invalid block configuration`（`:1230`）；统计指标名（`DatasetStatisticsViewModel.cs:87-95`）、执行状态（`ExecutionsViewModel.cs:46`）、数据集格式名（`DataWorkbenchViewModel.cs:21-25`）、校验/采样错误文本也未资源化。与 7.9 的“日志/状态/摘要英文”是同一类问题，但这里是**静态 UI/错误提示**，应单独跟踪。
- [ ] **198 [P3] 无架构/开发文档**：`docs/` 目前只有 2 张截图；没有模块边界、执行模型、数据格式契约、扩展节点指南。
- [ ] **199 [P3] 无安装/排障文档**：README 有打包命令，但没有干净机器安装、证书/Developer Mode、文件关联修复、日志位置与常见错误排查。

#### 8.3.10 安全与沙箱（200）

- [ ] **200 [P3] 无独立威胁模型**：README 只有一段“协作式沙箱、非内核级安全边界”的说明（`README.md:154`）；API Key 存储、web_fetch 私网/重绑定、诊断包内容、settings/日志目录权限等未形成一页“信任边界与已知限制”。

#### 8.3.11 代码质量（201–203）

- [ ] **201 [P2] `DataWorkbenchViewModel` 约 1183 行，职责偏重**：分页、编辑会话、搜索、校验、转换、统计入口集中在一个 VM；可先拆纯逻辑（分页/编辑/搜索）再拆 UI 绑定。
- [ ] **202 [P3] `MainWindow.axaml.cs` 约 563 行代码后置**：菜单、快捷键、对话框、文件选择、拖动、诊断等集中；与 7.11 的 `async void` 清单一起治理。
- [x] **203 [P3] 扩展名/Recipe 路径判断重复**：`Program.IsRecipePath`、`MainWindow.RecipeFileType`、`GetSuggestedFileName` 各自硬编码 `.json`；应抽出统一 `RecipeFileExtensions`（与 7.1 的修复同一批次）。

#### 8.3.12 未排期 backlog（204–205）

> 7.12 已列 MCP、容器隔离、HuggingFace/HTTP、插件/云端、实时图表、自动更新、更多语言、BenchmarkDotNet；本节只补充尚未出现在 7.12 的项。

- [ ] **204 [P3] Recipe diff/merge 与协作编辑**。
- [ ] **205 [P3] 便携模式 / 设置同步**。

### 8.4 修订后的修复顺序

> 原 7.13 仍有效；结合 8.3 调整为以下批次。每批建议 3–5 项，先测试后实现或同提交。

1. **P1 一致性与文档同步**
   - 7.1 全部（`.recipe` 打开/选择器/安装器/README、版本、图标、诊断包版本）。
   - 168、173、203（保存链 `.json`、Inno 版本参数、统一扩展名工具）。
   - 7.9 的 P1/P2（README 画布段落、STATUS 2.9/2.10/3.2/3.3、测试计数 512→513、CHANGELOG）。
   - 197（先修静态 UI 硬编码英文，至少覆盖主窗口/属性面板/错误提示）。
   - 验收：双击/命令行/选择器/安装器/README 五处一致；版本、图标、测试计数一致；`dotnet build` 0 警告。

2. **P1 测试与 CI 门禁**
   - 7.2 的 P1（单实例、命令行/文件关联、打包清单/脚本校验）。
   - 7.3 的 P1（`dotnet format --verify-no-changes`、覆盖率采集/阈值、打包/启动冒烟）。
   - 174–177（数据分页取消、恢复指纹、本地化硬编码扫描、设置 UI 校验）。
   - 验收：每个 P1 缺陷至少 1 个回归测试；PR 上能看到格式、测试、覆盖率、打包结果。

3. **P1/P2 节点与数据边角**
   - 7.5 的 P1：Seed `Streaming` UI、Validator `Cleanup` UI、Cleaner 正则 UI。
   - LLM `tool_calls` 回填、Anthropic 结构化输出；SFT 追加/目标格式/拆分；187、188、190。
   - 7.6：跨格式保存、`messages` 结构化编辑、匹配导航、预览分页、191。
   - 验收：每个节点成功/失败/边界测试；UI 入口真实可用；行数守恒、未修改 LF JSONL 逐字节一致。

4. **P2 执行、安全与发布验证**
   - 7.7：恢复哈希、溢写字节阈值、检查点配额、并发写检测、执行历史清理；192、193。
   - 7.10：API Key 加密、web_fetch 加固、诊断包脱敏、ACL；200。
   - 7.1/7.3 的安装、签名、版本单源；171、172、179–182。
   - 验收：恢复输出哈希一致；取消后无半成品；检查点不超配额；干净机器安装/启动/文件关联各一次记录。

5. **P3 体验与 backlog**
   - 7.8 剩余 UI 项、183–186、194–196、198–199、201–202、204–205。
   - 这些项可延期，但必须在下一 HANDOFF 保留入口与原因。

### 8.5 扫描结论

- 生产代码没有遗留的 `TODO`/`FIXME`/`NotImplementedException`，当前不是“大量桩代码”状态；未完成工作主要是**功能边角、发布一致性、测试/CI 门禁**。
- 最需要先处理的是：`.recipe` 全链路一致性（168/203 + 7.1）、打包/版本/图标（173 + 7.1）、CI 门禁（7.3）、节点编辑器缺口（7.5）、数据工作台取消/恢复指纹（174/175）、文档过期（7.9）。
- 归档 TODO 的 4.2/4.6/4.7 与 Phase 1–5 勾选状态是冻结快照；按冻结规则不回写归档，当前状态以本文件与 STATUS.md 为准。
- 扫描阶段仅修改计划文档；后续 Batch 1–6 已修改代码，当前验证结果见第 9 节与 6.3 验证记录。扫描基线为 `--list-tests` 513 用例、`dotnet test --no-build` 505 通过 / 0 失败 / 7 跳过（另有 1 个 `[Explicit]` 未计入）。

---

## 9. Round 17 执行记录（2026-11-17）

### 9.1 已完成批次

| 批次 | 范围 | 提交 |
| --- | --- | --- |
| 文档 | 全仓扫描复核与 205 项工作池 | `9abc715 docs(plan): 补全全仓扫描复核与 205 项工作池` |
| Batch 1 | `.recipe` 打开/保存/选择器/命令行、扩展名工具与测试 | `e036531 fix(recipe): 统一 .recipe 打开、保存与文件选择器` |
| Batch 2 | 版本单一来源、EXE 图标、Debug-only 诊断包、MSIX/Inno/发布版本贯通 | `9ea58ab build(release): 统一版本源并补齐图标与打包参数` |
| Batch 4 | Seed Streaming、Cleaner/Validator 共享 Cleanup 编辑器、LLM Schema 校验、SFT 字段校验 | `b3e8cc2 feat(nodes): 补齐 Seed/Cleaner/Validator/LLM/SFT 编辑缺口` |
| Batch 5 | 数据分页取消、跨格式编辑保存、恢复 SHA256/Recipe 指纹 | `f723827 fix(data): 支持跨格式编辑保存与可验证恢复` |
| Batch 6 | README/STATUS/CHANGELOG 同步、HANDOFF 勾选与验证记录 | 本次提交 |

### 9.2 当前验证

- `dotnet build -c Release`：0 警告，0 错误。
- `dotnet test -c Release --no-build`：531 通过，0 失败，7 跳过（5 性能 gated + 2 provider gated）。
- `dotnet test --list-tests`：539 个用例（含 1 个 `[Explicit]` 示例生成测试）。
- 新增回归：`RecipeFileExtensionsTests`、`NodeEditorTests`、分页取消、跨格式保存、恢复哈希/指纹与 Cleanup 模板 Headless 渲染。

### 9.3 本轮未做（保留在 7.x / 8.3）

- CI 作为独立批次：格式、覆盖率、打包冒烟、nightly gated job。
- LLM `tool_calls` 回填、Anthropic 结构化输出、SFT 追加/目标格式/拆分。
- Tool Profile MCP、容器级隔离、Sampler count 分层。
- 数据工作台消息级表单编辑、匹配导航、预览分页、列排序/筛选。
- API Key 加密、web_fetch 加固、诊断日志脱敏、安装包签名与干净机器验证。
- 硬编码英文静态文案、架构/安装文档、健康检查与保留策略。

---

## 10. Round 18 执行记录（2026-11-18）

### 10.1 已完成批次

| 批次 | 范围 | 提交 |
| --- | --- | --- |
| Batch 7 | 单实例/命令行启动测试、打包清单与本地化一致性测试、设置面板校验 | 本次提交 |
| Batch 8 | CI 门禁：格式、Debug/Release、覆盖率、示例漂移、发布冒烟 | 本次提交 |

### 10.2 具体改动

- 新增 `StartupLaunchTests`：命令行 Recipe 参数过滤、启动 Recipe 只消费一次、第二实例线程检测、命名管道转发/缺失回退。
- `SingleInstanceService` 支持可注入实例名与管道名，便于测试；默认行为不变。
- 新增 `PackagingConsistencyTests`：版本单一来源（`Directory.Build.props` → MSIX/Inno/version.ps1）、MSIX 资产、`.recipe` 关联、Inno 关联与 `%1` 转发、脚本 `-Version` 贯通、Release 不含 `Avalonia.Diagnostics`。
- 移除 `Directory.Build.props` 中硬编码的 `AssemblyVersion`/`FileVersion`/`InformationalVersion`，让 `-p:Version` 真正传递到发布产物（发布冒烟验证 FileVersion=0.0.0.0）。
- 新增 `LocalizationConsistencyTests`：zh-CN/en-US key 集合、占位符、重复 key、静态引用的 `{loc:Loc}` / `LocalizationService.L("...")` key 存在性。
- 设置面板：数值项改为有界 `NumericUpDown`；缓存/运行/临时目录增加 Browse 按钮；Apply 前校验数值范围与目录可写性，失败时面板保持打开并在面板内显示错误。
- 新增 `SettingsPanelTests`（VM 校验、读写持久化）与 Headless 输入边界/按钮检查。
- CI 重写：格式门禁、Debug/Release 矩阵、Coverlet 覆盖率门禁（总行 ≥60%，Services/Commands ≥70%）、打包元数据测试、示例再生成 + `git diff` 漂移检查、PowerShell 脚本解析、`publish-win-x64.ps1` 版本冒烟；新增 concurrency/timeout/NuGet 缓存。
- 新增 `scripts/check-coverage.ps1`；测试项目加入 `coverlet.msbuild`。
- `DatasetIOTests` 的 CSV 多行 fixture 使用 `ReplaceLineEndings("\n")`，避免 CRLF 检出导致跨平台测试漂移。
- 全仓执行 `dotnet format`，统一导入顺序、缩进与行尾。

### 10.3 当前验证

- `dotnet build -c Release`：0 警告，0 错误。
- `dotnet test -c Release`：550 通过，0 失败，7 跳过（5 性能 gated + 2 provider gated）；`--list-tests` 557 个用例（含 1 个 `[Explicit]`）。
- `dotnet format --verify-no-changes`：通过。
- 覆盖率（本地）：总行 74.64%，Services/Commands 行 84.32%。
- `scripts/publish-win-x64.ps1 -Version 0.0.0`：产物与 zip 生成，EXE FileVersion=0.0.0.0。
- 示例再生成后 `git diff -- examples/`：无漂移。
- 未在本机执行：GitHub Actions 实际运行、MSIX/Inno 编译、干净机器安装。

### 10.4 本轮未做（保留在 7.x / 8.3）

- 176 硬编码英文静态扫描（与 197 一起处理，需要可维护的允许清单）。
- 179/180 跨平台 CI 构建（Linux/macOS）。
- 7.2 自动崩溃入口、格式转换边界、索引缓存边界、节点面板逐项人工验收。
- 7.1 Recipe 校验错误码、7.7 执行清理/配额、7.10 安全加固。
- 7.3 nightly/gated job、依赖安全扫描、`Directory.Packages.props`/锁定依赖。

---

## 11. Round 19 执行记录（2026-11-18）

### 11.1 已完成项

| 项 | 范围 | 提交 |
| --- | --- | --- |
| Batch 9 | 搜索匹配导航、样本预览分页、最近文件清理、执行输出目录打开 | 本次提交 |

### 11.2 具体改动

- 搜索：新增上一处/下一处匹配命令与按钮，选中匹配即跳转，首尾循环。
- 预览：`SamplePreviewService` 支持 `startRow`；预览窗口按 50 行分页，显示页码并支持上一页/下一页。
- 最近文件：打开菜单时自动移除失效路径，新增“清空最近文件”并立即持久化。
- 执行详情：输出目录存在时显示“打开文件夹”按钮，失败写入日志。

### 11.3 当前验证

- `dotnet build -c Release`：0 警告，0 错误。
- `dotnet test -c Release`：556 通过，0 失败，7 跳过；`--list-tests` 563 个用例。
- `dotnet format --verify-no-changes`：通过。

### 11.4 本轮未做（保留在 7.x / 8.3）

- 预览全文搜索（当前为分页 + 数据工作台搜索高亮）。
- 7.5 节点能力（LLM tool_calls 回填、Anthropic 结构化输出、SFT 追加/目标格式/拆分）。
- 7.7 执行清理/配额、7.10 安全加固、7.1 Recipe 校验错误码。

---

## 12. Round 20 执行记录（2026-11-18）

### 12.1 已完成项

| 项 | 范围 | 提交 |
| --- | --- | --- |
| Batch 10 | SFT 追加/目标格式/按行数分片、字段选择器、失败与 reject 路径 Browse | 本次提交 |

### 12.2 具体改动

- `SftExportBlockConfig` 新增 `Append`、`TargetFormat`、`SplitMaxRows`；旧 Recipe（只含 `outputPath`/`fields`）按默认值加载。
- 新增 `SftExportWriter`：单文件/分片统一走临时文件，成功后再原子替换；追加模式先复制原文件再追加，输入失败时保留原文件；分片命名 `name.part001.jsonl`、`name.part002.jsonl`…。
- 目标格式：`Auto` 透传；`OpenAI`/`ShareGPT`/`Alpaca` 复用 `DatasetFormatConverter` 转换；`Dpo`/`Pretrain` 在校验与执行期明确拒绝。
- 字段白名单在格式转换后应用，可只保留目标格式的字段。
- 校验服务新增：不支持的目标格式、负分片、追加 + 分片组合。
- 新增 `DatasetFieldDiscovery`：通过 `DatasetIO` 读取 JSONL/CSV/Parquet 首行，返回顶层字段。
- SFT 面板：追加开关、目标格式下拉、每片行数 `NumericUpDown`、从样本读取字段、复选列表、全选/清空；字段框与复选双向同步，手动输入的未知字段会保留。
- LLM/Simulation 失败样本、Validator reject 输出、Sampler 输出目录新增 Browse（文件/文件夹选择器）。
- 新增本地化键（zh-CN/en-US）：追加、目标格式、分片、字段加载与状态提示。
- 示例 Recipe 重新生成（`WriteExamples`），补入新增默认字段。

### 12.3 当前验证

- `dotnet build -c Release`：0 警告，0 错误。
- `dotnet test -c Release`：582 通过，0 失败，7 跳过（5 性能 gated + 2 provider gated）；`--list-tests` 590 个用例（含 1 个 `[Explicit]`）。
- `dotnet format --verify-no-changes`：通过。
- 示例再生成后 `git diff -- examples/`：仅包含 SFT 新增默认字段（已提交）。
- 新增测试：`SftExportExecutorTests`（透传、无尾换行追加、失败保留原文件、Alpaca/ShareGPT 转换、字段过滤、分片、分片失败清理、非法组合）、`SftExportFieldSelectorTests`（配置推送、样本字段发现、复选同步、全选/清空、错误提示）、校验服务 3 项、序列化往返、Headless 面板控件检查。

### 12.4 本轮未做（保留在 7.x / 8.3）

- SFT 按比例拆片与 DPO/Pretrain 直出（仍由数据工作台承担）。
- SFT 并发写同一目标文件检测；输出路径存在性提示（206）。
- LLM `tool_calls` 回填、Anthropic 结构化输出。
- 数据工作台 `messages` 结构化表单、列排序/筛选/列宽持久化（191）。
- 7.7 执行清理/配额、7.10 安全加固、7.1 Recipe 校验错误码。

---

## 13. Round 21 执行记录（2026-11-18）

### 13.1 已完成项

| 项 | 范围 | 提交 |
| --- | --- | --- |
| Batch 11 | LLM tool_calls 回填、Anthropic 结构化输出、编辑器与校验 | 本次提交 |

### 13.2 具体改动

- `LlmBlockConfig` 新增 `MaxToolRounds`（默认 0 = 只记录）、`ToolMode`（Simulated/Real）、`Sandbox`；旧 Recipe 按默认值加载。
- `LlmBlockExecutor` 新增工具回填循环：按 `MaxToolRounds` 执行 tool_calls，复用 `IToolRuntime`（Simulated 返回确定性 JSON，Real 走沙箱），未知工具与执行错误以错误 JSON 回灌；超过轮次该行失败；usage 跨轮累加；工具轨迹输出为完整 messages（含 `tool_calls` 与 `tool_call_id`）。
- `LlmChatTurn` 增加 `ToolCallId`/`ToolCalls`；`LlmApiClient` 三家 provider 支持工具消息：
  - OpenAI Chat：`tool_calls` + `tool_call_id`；
  - OpenAI Responses：`function_call` / `function_call_output` 输入项；
  - Anthropic：`tool_use` / `tool_result` 内容块，连续同角色块自动合并。
- Anthropic 结构化输出：`ResponseSchema` 转为强制 `structured_output` 工具（`tool_choice`），解析 `tool_use.input` 作为内容。
- 校验：负轮数、启用轮数但无 Tool Profile、Anthropic 结构化输出 + 工具回填组合。
- LLM 面板新增工具回填轮数与工具执行方式；编辑器改用 `with` 推送以保留未编辑的 Sandbox 配置。
- 本地化新增 2 个键；示例 Recipe 重新生成（仅新增默认字段）。

### 13.3 当前验证

- `dotnet build -c Release`：0 警告，0 错误。
- `dotnet test -c Release`：596 通过，0 失败，7 跳过（5 性能 gated + 2 provider gated）；`--list-tests` 604 个用例（含 1 个 `[Explicit]`）。
- `dotnet format --verify-no-changes`：通过。
- 示例再生成后 `git diff -- examples/`：仅 `llm_batch.recipe.json` 新增 `maxToolRounds`/`toolMode` 默认字段。
- 新增测试：`LlmToolLoopTests`（回填往返、0 轮只记录、超轮失败、工具异常回灌、未知工具回灌）、`LlmApiClientTests` 4 项（Chat/Responses/Anthropic 工具序列化、Anthropic 强制工具）、编辑器 2 项、校验 3 项、序列化与 Headless 面板检查。

### 13.4 本轮未做（保留在 7.x / 8.3）

- LLM 流式输出、模型列表/探测（7.5 P2）。
- Tool Profile Enum/Default 编辑、MCP（7.5 P2）。
- 数据工作台 `messages` 结构化表单、列排序/筛选/列宽持久化（191）。
- SFT 按比例拆片、并发写检测与路径存在性提示（206）。

---

## 14. Round 22 执行记录（2026-11-18）

### 14.1 已完成项

| 项 | 范围 | 提交 |
| --- | --- | --- |
| Batch 12 | 数据工作台 `messages` 结构化编辑 | 本次提交 |

### 14.2 具体改动

- 新增 `DatasetMessageEditor`：解析/构建顶层 `messages` 数组，暴露角色、文本内容、结构化内容与 `tool_calls`；未暴露的属性从原始 JSON 原样保留。
- `DataWorkbenchViewModel` 新增 `HasMessages`、`Messages`、`SelectedMessage`、`MessagesDirty` 与增删/上下移/应用消息命令；行选中时按可用性选择字段页、消息页或原始 JSON。
- `ApplyFieldsAsync` 在消息存在未应用修改时先合并 `messages` 字段，避免两个编辑器互相覆盖；`ApplyMessagesAsync` 反向合并其他字段修改。
- 行编辑器新增“消息”标签页：每条消息可编辑角色、内容与可选 `tool_calls`；结构化内容标记“内容为 JSON”。标签顺序为“字段 / 消息 / 原始 JSON”，双击行仍跳转原始 JSON。
- 本地化新增 9 个键；README / STATUS / CHANGELOG 已同步。

### 14.3 当前验证

- `dotnet build -c Release`：0 警告，0 错误。
- `dotnet test -c Release`：612 通过，0 失败，7 跳过（5 性能 gated + 2 provider gated）；`--list-tests` 620 个用例（含 1 个 `[Explicit]`）。
- `dotnet format --verify-no-changes`：通过。
- 新增测试：`DatasetMessageEditorTests` 10 项、`DataWorkbenchEditTests` 5 项（角色/内容、增删移动、tool_calls 错误、字段合并、无 messages 回退）、消息页 Headless 控件检查。
- UI 仅经 Headless 渲染与 ViewModel 验证，未在交互窗口中手工点击。

### 14.4 本轮未做（保留在 7.x / 8.3）

- 数据工作台列排序/条件筛选/列宽持久化（191）、CSV 类型推断。
- 非 `messages` 嵌套结构（如 ShareGPT `conversations`）的结构化表单。
- SFT 按比例拆片、并发写检测与输出路径存在性提示（206）。
- LLM 流式输出与模型列表、Tool Profile Enum/Default 编辑。

---

## 15. Round 23 执行记录（2026-11-18）

### 15.1 已完成项

| 项 | 范围 | 提交 |
| --- | --- | --- |
| Batch 13 | Tool Profile Enum/Default 编辑、跨节点输出目标冲突校验 | 本次提交 |

### 15.2 具体改动

- Tool Profile 参数树新增“枚举”和“默认值”两列：枚举支持逗号分隔字符串或 JSON 数组（保留数字/布尔/null 类型），默认值按 JSON 解析。
- “枚举/默认值”与 `ParametersJson` 双向同步；非法 JSON 在面板内报错，并保留上一次有效 Schema。
- Recipe 校验新增输出目标冲突检测：SFT `outputPath`、LLM/Simulation `failureOutputPath`、Validator `rejectOutputPath` 与 Sampler 分片文件（`<outputDirectory>/<split>.jsonl`）。
- 冲突判定大小写不敏感，覆盖相同文件与相同 Sampler 输出目录；同一节点的多个目标不参与互相比较。
- 新增 zh-CN/en-US 本地化键（枚举、默认值）。

### 15.3 当前验证

- `dotnet build -c Release`：0 警告，0 错误。
- `dotnet test -c Release`：618 通过，0 失败，7 跳过（5 性能 gated + 2 provider gated）；`--list-tests` 626 个用例（含 1 个 `[Explicit]`）。
- `dotnet format --verify-no-changes`：通过。
- 新增测试：`ToolProfileReuseTests`（枚举/默认值读写与非法输入）、`RecipeValidationServiceTests` 4 项（重复文件、Sampler 分片冲突、同目录不冲突、不同文件不冲突）、参数树 Headless 控件检查。
- 修复回归：输出目标校验最初把“Sampler 目录内的任意文件”视为冲突，导致 `Runner_RoutesSamplerSplitPortsToDownstreamBlocks` 误报；改为展开具体分片文件后通过。
- UI 仅经 Headless 渲染与 ViewModel 验证，未在交互窗口中手工点击。

### 15.4 本轮未做（保留在 7.x / 8.3）

- 跨进程/跨运行的并发写锁（两次独立运行同时写同一路径）。
- SFT 按比例拆片、输出路径存在性提示（206）。
- LLM 流式输出与模型列表。
- 数据工作台列排序/筛选/列宽持久化（191）。

---

## 16. Round 24 执行记录（2026-11-18）

### 16.1 已完成项

| 项 | 范围 | 提交 |
| --- | --- | --- |
| Batch 14 | API Key 加密存储、旧明文迁移、跨平台回退 | 本次提交 |

### 16.2 具体改动

- 新增 `IApiKeyProtector` 与 `DpapiApiKeyProtector`：使用 Windows DPAPI（`DataProtectionScope.CurrentUser` + 应用固定熵）加密 Key，密文格式为 `dpapi:v1:<base64>`。
- `SettingsStore` 在序列化边界加密：写入前复制 `AppSettings` 并替换 Key 为密文，内存中的对象保持明文供 `ILlmCredentials` 使用。
- `Load` 识别三种状态：密文解密、旧明文迁移、无法解密；旧明文加载后立即以原子替换加密回写，并记录迁移警告。
- 无法解密（换用户、损坏、跨平台）时忽略该 Key 并给出警告，回退到 `OPENAI_API_KEY` / `ANTHROPIC_API_KEY`。
- 非 Windows：`Protect` 不可用，`SaveAsync` 不写入 Key（只使用环境变量），避免回退到明文。
- 诊断包脱敏逻辑不变；加密值同样会在导出前被清空。
- 未修改设置界面；Key 输入框行为保持原样。

### 16.3 当前验证

- `dotnet build -c Release`：0 警告，0 错误。
- `dotnet test -c Release`：624 通过，0 失败，7 跳过（5 性能 gated + 2 provider gated）；`--list-tests` 632 个用例（含 1 个 `[Explicit]`）。
- `dotnet format --verify-no-changes`：通过。
- 新增测试：`SettingsStoreTests` 6 项（DPAPI 落盘不含明文、双 Key 加密往返、旧明文迁移后文件加密、损坏密文告警、无保护能力时丢弃 Key、跨平台读取密文告警），并调整原有 camelCase 与往返断言按平台分支。
- 真实 DPAPI 往返在 Windows 本机执行通过；未做干净用户/换机场景的手工验证。

### 16.4 本轮未做（保留在 7.x / 8.3）

- 设置变更通知（7.4）、Settings/Index Cache schema 版本与迁移。
- web_fetch DNS 重绑定加固、诊断日志内容脱敏、目录 ACL 收紧（7.10 P2/P3）。
- LLM 流式输出与模型列表。
- 数据工作台列排序/筛选/列宽持久化（191）、CSV 类型推断。

---

## 17. Round 25 执行记录（2026-11-18）

### 17.1 已完成项

| 项 | 范围 | 提交 |
| --- | --- | --- |
| Batch 15 | web_fetch DNS 重绑定加固、设置变更通知 | 本次提交 |

### 17.2 具体改动

- `RealToolRuntime` 的 web_fetch 只解析一次主机名；解析结果全部为非私网/回环后才发起请求，任一地址为私网即拒绝（包括“公网 + 私网”混合结果）。
- 未注入测试用 `HttpClient` 时，每次抓取创建 `SocketsHttpHandler`，通过 `ConnectCallback` 只连接已校验的 IP；主机名不会再次解析，TLS/Host 仍按原 URL 校验，重定向仍关闭。
- 新增内部解析器与固定客户端工厂注入口，便于在不访问真实网络的情况下验证“只解析一次、只连接校验地址”。
- 新增 `IAppSettingsService` / `AppSettingsService`：`Current` 保存当前设置，`Update` 在实例变化时发布 `Changed`。
- `MainWindowViewModel` 改为以 `AppSettingsService` 为设置单一读取点；Apply 成功后发布变更，不再手工调用数据工作台刷新；对外暴露 `SettingsService` 供长生命周期服务订阅。
- `DataWorkbenchViewModel` 订阅设置变更，自动重建读取器工厂（默认读取器按 `Current` 创建）、批量转换器与四个子服务；修复原先读取器工厂闭包长期持有旧设置、批量转换器不随设置更新的问题。
- 按需创建的服务（样本预览、索引缓存、每次运行的执行器）继续在创建时读取 `Current`，无需订阅。

### 17.3 当前验证

- `dotnet build -c Release`：0 警告，0 错误。
- `dotnet test -c Release`：630 通过，0 失败，7 跳过（5 性能 gated + 2 provider gated）；`--list-tests` 638 个用例（含 1 个 `[Explicit]`）。
- `dotnet format --verify-no-changes`：通过。
- 新增测试：`SandboxTests` 2 项（混合公网/私网解析结果被拒绝且不建立连接；只解析一次并把校验地址交给固定客户端）、`AppSettingsServiceTests` 2 项（变更事件携带前后实例、同一实例不重复通知）、`DataWorkbenchViewModelTests` 1 项（设置更新后新读取器使用新索引缓存目录）、`SettingsPanelTests` 2 项（Apply 更新当前设置并通知订阅者）。
- 仅经单元/集成测试验证，未在真实公网环境发起抓取，也未手工验证 TLS/SNI 行为。

### 17.4 本轮未做（保留在 7.x / 8.3）

- 诊断日志内容脱敏、settings/日志目录 ACL 收紧、web_fetch 域名白名单/代理策略（7.10 P2/P3）。
- Settings/Index Cache schema 版本与迁移、“恢复默认设置”、日志级别/来源筛选（7.4）。
- LLM 流式输出与模型列表。
- 数据工作台列排序/筛选/列宽持久化（191）、CSV 类型推断。

---

## 18. Round 26 执行记录（2026-11-18）

### 18.1 已完成项

| 项 | 范围 | 提交 |
| --- | --- | --- |
| Batch 16 | 诊断包全文脱敏、设置/日志/崩溃目录 ACL 收紧 | 本次提交 |

### 18.2 具体改动

- 新增 `DiagnosticRedactor`：按模式清除 `Authorization` / `Proxy-Authorization`、`Bearer` 令牌、常见 JSON 密钥字段（`apiKey`、`openAiApiKey`、`anthropicApiKey`、`access_token`、`refresh_token`、`token`、`secret`、`password` 等）、查询参数，以及 `sk-`、`gh[pousr]_`、`AKIA` 前缀；同时按字面量替换设置与环境变量中的 Key。
- `CrashReportService` 一次加载设置并收集已知密钥，`summary.txt`、`logs/*.log`、`executions/*.json` 与 `settings.redacted.json` 全部经脱敏后写出；日志读取改为共享读写，避免活动日志被锁时跳过。
- 新增 `AppDirectorySecurity`：Windows 上首次创建目录时移除继承 ACL，仅保留当前用户、SYSTEM、Administrators 完全控制；失败时记录告警但不影响主流程，非 Windows 为无操作，已应用目录做进程内缓存。
- 接入 `SettingsStore.SaveAsync` / API Key 迁移、`AppLog.Initialize` 与自动崩溃报告目录。
- 未改变诊断包结构与其他公开 API。

### 18.3 当前验证

- `dotnet build -c Release`：0 警告，0 错误。
- `dotnet test -c Release`：638 通过，0 失败，7 跳过（5 性能 gated + 2 provider gated）；`--list-tests` 646 个用例（含 1 个 `[Explicit]`）。
- `dotnet format --verify-no-changes`：通过。
- 新增测试：`DiagnosticSecurityTests` 8 项（Authorization/Bearer、JSON 字段、已知密钥字面量、普通文本不误伤、诊断包日志与 summary 不含密钥、空路径拒绝、Windows ACL 生效且无 Everyone 规则、设置保存收紧目录）。
- ACL 测试仅在本机 Windows 执行；未在干净用户/其他机器验证权限继承行为。

### 18.4 本轮未做（保留在 7.x / 8.3）

- web_fetch 域名白名单/代理策略、崩溃报告保留策略、安装包签名（7.10 P3）。
- Settings/Index Cache schema 版本与迁移、“恢复默认设置”、日志级别/来源筛选（7.4）。
- LLM 流式输出与模型列表。
- 数据工作台列排序/筛选/列宽持久化（191）、CSV 类型推断；SFT 比例拆片与输出路径存在性提示（206）。

---

## 19. Round 27 执行记录（2026-11-18）

### 19.1 已完成项

| 项 | 范围 | 提交 |
| --- | --- | --- |
| Batch 17 | 执行韧性收尾：溢写字节阈值、检查点磁盘配额、执行历史保留 | 本次提交 |

### 19.2 具体改动

- `RowSpillBuffer` 增加内存字节预算：按行序列化大小估算，行数或字节任一超限即溢写；`memoryLimitBytes: 0` 关闭字节限制。`RecipeRunOptions`、`AppSettings`、`SettingsStore` 校验、设置面板与运行入口全部接入（默认 64 MB，UI 以 MB 显示）。
- 新增 `CheckpointQuota`：按运行输出目录统计各 run 的 `nodes` 检查点大小，从最旧目录开始删除直到满足配额；保护当前运行目录，只删除检查点，不删除 `run-summary.json` / 日志。配额为 0 时不执行。应用启动与每次运行结束（保护当前 run）各执行一次。
- `ExecutionStore.Prune`：按最后写入时间保留最新 N 条与最近 D 天，删除对应 `.json` + `.log`；数量/天数为 0 表示该维度不限制，可保护指定 run。启动维护自动应用，Executions 视图新增“清理…”按钮（执行前确认）。
- 设置新增 `checkpointQuotaMb`（默认 512）、`executionHistoryLimit`（默认 200）、`executionHistoryRetentionDays`（默认 30）；非法值回退默认并告警；本地化补齐 en-US / zh-CN 键。
- `MainWindowViewModel` 支持注入 `ExecutionStore`，新增 `RunStartupMaintenanceAsync` 与 `CleanupExecutionsCommand`；启动维护在 `App` 创建主窗口后触发，异常仅告警不阻塞启动。

### 19.3 当前验证

- `dotnet build -c Release`：0 警告，0 错误。
- `dotnet test -c Release`：651 通过，0 失败，7 跳过（5 性能 gated + 2 provider gated）；`--list-tests` 659 个用例（含 1 个 `[Explicit]`）。
- `dotnet format --verify-no-changes`：通过。
- 覆盖率（本地）：总行 76.4%，Services/Commands 行 84.87%。
- 新增测试 13 项：`RowSpillBuffer` 字节预算 2 项、`RecipeRunner` 字节溢写 1 项、`CheckpointQuotaTests` 4 项、`ExecutionStore.Prune` 4 项、启动维护/手动清理 2 项；设置面板与设置往返/非法值测试同步扩展。

### 19.4 本轮未做（保留在 7.x / 8.3）

- 跨进程并发写同一目标文件的锁或检测（7.7 仍为部分）。
- CI 取消性能门禁、10M 行执行/质量/转换结果按本机实测记录（7.7 P2/P3）。
- 溢写目录启动清理（当前按运行临时目录前缀随运行结束删除，异常退出残留未单独处理）。
- LLM 流式输出与模型列表；数据工作台列排序/筛选/列宽持久化（191）与 CSV 类型推断；SFT 比例拆片与输出路径存在性提示（206）。

---

## 20. Round 28 执行记录（2026-11-18）

### 20.1 已完成项

| 项 | 范围 | 提交 |
| --- | --- | --- |
| Batch 18 | 执行韧性收尾：输出写锁、溢写目录启动清理、CI 取消门禁 | 本次提交 |

### 20.2 具体改动

- 新增 `RecipeOutputTargets`：把 SFT 输出、LLM/Simulation 失败样本、Validator reject 与 Sampler 分片文件的目标收集从校验服务抽出，校验与运行期锁共用同一份定义。
- 新增 `RunOutputLock`：每个输出文件在同级创建 `<target>.lmtds.lock`，以 `FileShare.None` 独占打开并写入 PID/run/目标；按序获取避免死锁，`Dispose` 释放并删除锁文件。
- `RecipeRunner` 在校验通过后、执行前获取锁；冲突时运行标记失败、未执行节点标记跳过并写入 run summary，日志列出冲突目标；锁在 `finally` 中释放。
- 新增 `SpillDirectoryMaintenance`：启动时删除溢写根下所有 run-scoped 残留目录（正常结束由 `finally` 删除），无法删除的目录留待下次启动重试；接入 `RunStartupMaintenanceAsync` 并记录清理数量。
- `RecipeRunOptions` 暴露 `DefaultSpillDirectory`，与启动清理共用默认值。
- 取消性能测试改为读取 `LMTDS_CANCEL_BUDGET_MS`（未设置时 1000ms）；CI 全局设置 3000ms，本地仍按严格 1s 验收。

### 20.3 当前验证

- `dotnet build -c Release`：0 警告，0 错误。
- `dotnet test -c Release`：661 通过，0 失败，7 跳过（5 性能 gated + 2 provider gated）；`--list-tests` 669 个用例（含 1 个 `[Explicit]`）。
- `dotnet format --verify-no-changes`：通过。
- 覆盖率（本地）：总行 76.46%，Services/Commands 行 84.9%（门槛 60% / 70%）。
- 新增测试 10 项：`RunOutputLockTests` 4 项、`SpillDirectoryMaintenanceTests` 3 项、`RecipeRunnerTests` 2 项（目标被占用则拒绝执行、失败后释放锁）、`ExecutionsViewModelTests` 1 项（启动清理残留溢写目录）。
- 锁的跨进程语义依赖 `FileShare.None`，测试在同一进程内以第二次独占打开模拟冲突；未做真实双进程端到端验证。

### 20.4 本轮未做（保留在 7.x / 8.3）

- 10M 行执行/质量/转换基线按本机实测记录（7.7 P3）。
- LLM 流式输出与模型列表；数据工作台列排序/筛选/列宽持久化（191）与 CSV 类型推断；SFT 比例拆片与输出路径存在性提示（206）。

---

## 21. Round 29 执行记录（2026-11-18）

### 21.1 已完成项

| 项 | 范围 | 提交 |
| --- | --- | --- |
| Batch 19 | 节点能力：LLM OpenAI Chat 流式输出、模型列表探测与面板选择 | 本次提交 |

### 21.2 具体改动

- `LlmBlockConfig` 新增 `Streaming`（默认 false，追加在参数末尾，兼容既有 Recipe/JSON）；LLM 面板新增“流式输出（仅 OpenAI Chat）”开关与说明。
- `LlmApiClient` 在 `Streaming && provider == openai_chat` 时发送 `stream=true` + `stream_options.include_usage`，逐行解析 SSE `data:`，累积 content、按 index 合并 tool_calls（id/name/arguments 增量）与 usage，返回既有 `LlmCompletionResult`；工具回填循环无需改动。
- 新增 `SendStreamingWithRetryAsync`：沿用 `TimeoutSeconds`/`MaxRetries` 与指数退避，每次重试重建请求并从空累积器读取，支持 5xx/429/超时/HTTP/IO 失败重试。
- Responses/Anthropic 在 `Streaming=true` 时保持一次性响应（与面板说明一致），未做非 OpenAI SSE。
- 新增 `ILlmModelCatalog`/`LlmModelCatalog`：OpenAI GET `{base}/models`、Anthropic GET `{base}/v1/models?limit=1000`，复用 `ILlmCredentials`（设置中的 DPAPI 密钥或环境变量、Base URL），固定 30s 超时，返回去重排序后的模型 id。
- `LlmConfigEditorViewModel` 增加模型下拉、刷新命令与状态/错误文本；选中模型写回 `ModelAlias`。`BlockNodeViewModel.ModelCatalog` 由 `MainWindowViewModel` 在块加入集合时注入，画布/粘贴/文档加载创建的编辑器共享同一实例。
- 新增本地化键 7 个（en-US/zh-CN）：模型列表、刷新、加载中/完成/失败、流式开关与说明。
- `MainWindow.axaml`/属性面板增加 `x:Name` 以便 Headless UI 测试断言绑定与命令。

### 21.3 当前验证

- `dotnet build -c Release`：0 警告，0 错误。
- `dotnet test -c Release`：677 通过，0 失败，7 跳过（5 性能 gated + 2 provider gated）；`--list-tests` 685 个用例（含 1 个 `[Explicit]`）。
- `dotnet format --verify-no-changes`：通过。
- 覆盖率（本地）：总行 76.64%，Services/Commands 行 84.95%（门槛 60% / 70%）。
- 新增测试 16 项：`LlmApiClientTests` 流式 4 项（内容/工具/usage 累积、非 Chat 回落、瞬时错误重试、重试耗尽）、`LlmModelCatalogTests` 6 项（OpenAI/Anthropic/自定义端点/错误/缺密钥/未知 provider）、`LlmConfigEditorTests` 5 项（Streaming 写回、目录注入、刷新成功/失败、无目录禁用）、`HeadlessUiTests` 1 项（面板绑定与命令）；`BlockConfigSerializationTests` Round-trip 增加 `Streaming`。
- 未用真实 API Key 验证 SSE 与模型接口，测试使用本地 SSE/JSON 桩；真实 provider 冒烟仍为 gated。

### 21.4 本轮未做（保留在 7.x / 8.3）

- Responses/Anthropic 流式输出（当前仅 OpenAI Chat）。
- 模型列表缓存与手动输入候选提示（每次点击刷新实时请求）。
- 10M 行执行/质量/转换基线按本机实测记录（7.7 P3）。
- 数据工作台列排序/筛选/列宽持久化（191）与 CSV 类型推断；SFT 比例拆片与输出路径存在性提示（206）。


## 22. Round 30 执行记录（2026-11-18）

### 22.1 已完成项

| 项 | 范围 | 提交 |
| --- | --- | --- |
| Batch 20 | 数据工作台：字段表格、全量筛选/排序、列宽持久化（191） | 本次提交 |

### 22.2 具体改动

- 新增 `DatasetViewSpec` / `DatasetViewIndex` / `DatasetViewBuilder`：流式扫描数据集（4,096 行/批），支持按顶层字段做包含/不包含/等于/不等于/数值比较/空值/正则筛选，并按字段升/降序排序；排序键全为数值时按数值比较，否则按 OrdinalIgnoreCase，平局保持源行顺序；匹配上限 100 万行，超出时提示缩窄筛选。
- `IDatasetReader` 新增 `ReadRowsAsync(IReadOnlyList<long>)` 默认实现（升序连续行合并为一次分页读取），`ParquetDatasetReader` 覆写为每个 row group 只解码一次；视图分页按视图顺序读取源行。
- `DataWorkbenchViewModel` 新增列集合（首次出现顺序，上限 128 列）、行单元格、筛选工具栏状态、视图分页、构建进度/取消与清除视图；视图激活期间禁用行编辑，搜索/校验跳转时自动清除视图；存在待保存编辑或转换时拒绝建立视图；视图内“跳转到行”仍按源行号定位，未命中时提示该行不在视图中。
- 列宽按字段名写入 `AppSettings.DataWorkbenchColumnWidths`，加载时钳制到 48–800px；拖拽完成经 `SettingsPersistenceRequested` 由主窗口保存，设置 Apply 时保留该字典。
- 数据工作台 UI 新增“字段”切换、筛选工具栏（列/操作符/值/大小写/应用/清除）、表头点击排序、表头与行横向滚动同步、列宽拖拽手柄；新增 19 个 zh-CN/en-US 本地化键。

### 22.3 当前验证

- `dotnet build -c Release`：0 警告，0 错误。
- `dotnet test -c Release`：701 通过，0 失败，7 跳过（5 性能 gated + 2 provider gated）；`--list-tests` 709 个用例（含 1 个 `[Explicit]`）。
- `dotnet format --verify-no-changes`：通过。
- 覆盖率（本地）：总行 77.15%，Services/Commands 行 85.16%（门槛 60% / 70%）。
- 新增测试 24 项：`DatasetViewTests` 14（筛选、数值比较、正则与非法正则、空值、数值/文本排序、平局稳定、字段发现、行数上限、切片、散列读取、Parquet 散列读取）、数据工作台 VM 7（筛选跨页与清除、排序跨页与反向、待保存编辑拒绝、视图内跳行与隐藏行提示、跳转清除视图、列宽读取/持久化、后续页新增列）、设置 2（列宽清洗、保存往返）、Headless 1（字段视图控件与绑定）。
- 使用 Avalonia Headless + Skia 截图人工检查 120 行 JSONL 的字段视图：表头/单元格对齐、筛选 95/120 行、排序指示与编辑禁用提示正常；截图未提交。

### 22.4 本轮未做（保留在 7.x / 8.3）

- 视图索引上限 100 万行且常驻内存；10M 行筛选/排序未做磁盘索引与实测。
- 排序视图分页按散列源行读取，JSONL/CSV 可接受；Parquet 已按 row group 批量解码但未做大文件基准。
- CSV 类型推断与列类型展示未做。
- 列宽仅按字段名全局保存，不区分数据集；表头与行的横向滚动同步依赖可视树查找。
- SFT 比例拆片与输出路径存在性提示（206）。

## 23. Round 31 执行记录（2026-11-18）

### 23.1 已完成项

| 项 | 范围 | 提交 |
| --- | --- | --- |
| Batch 21 | 代码质量：删除未引用转换器/颜色键、NoWarn 清单审计与低频告警修复（7.11 / 7.3） | 本次提交 |

### 23.2 具体改动

- 删除 `Converters/BlockTypeToColorConverter.cs`（`BlockTypeToColorConverter`、`InverseBoolConverter` 均无 XAML/代码引用），以及 `BlockTemplateViewModel.ColorKey`、`BlockNodeViewModel.HeaderColorKey`；块头颜色统一由 `Themes/Colors.axaml` 的 `*BlockBrush` 资源提供。
- `Directory.Build.props` 的 `NoWarn` 从 55 项缩减到 7 项（CA1001/CA1305/CA1416/CA1822/CA1859/CA1861/CA1863）并逐组注明理由；`CA1707` 移入测试项目。移除的 48 项中，43 项在 `-p:NoWarn=` 全量构建中零命中（含全部 17 个 IDE 规则），5 项通过修改代码消除。
- 修复的低频告警：
  - `JsonlDatasetReader.ReadPageAsync` 参数 `startLine` 改名 `startRow`，与接口一致（CA1725）。
  - `DatasetView.Sort` 数值分支显式处理 `TryParseNumber` 失败（CA1806）。
  - `ExpressionConfigEditorViewModel` 与 `RecipeRunner` 复用静态 `JsonSerializerOptions`（CA1869）。
  - `RecipeRunner.RunAsync` 将 `CancellationToken` 移到参数末尾，`MainWindowViewModel` 调用点同步（CA1068）。
  - 两处测试断言补 `StringComparison.Ordinal`（CA1310）。

### 23.3 当前验证

- `dotnet build -c Release`：0 警告，0 错误。
- `dotnet build -c Release -t:Rebuild -p:NoWarn=`（并关闭警告即错误）：仅剩 8 个规则码（7 个全局 + 测试项目 CA1707），其余已清零。
- `dotnet test -c Release`：701 通过，0 失败，7 跳过；`--list-tests` 709 个用例（含 1 个 `[Explicit]`）。
- `dotnet format --verify-no-changes`：通过。
- 覆盖率（本地）：总行 77.34%，Services/Commands 行 85.15%（门槛 60% / 70%）。
- 本轮为删除与重构，无新增测试；既有 701 项全部通过。

### 23.4 本轮未做（保留在 7.x / 8.3）

- CA1001：6 个视图模型与 `RequestRateLimiter`、`AutoFixWriter` 尚未接入 `IDisposable`，当前按所有者生命周期管理；已在 7.11 新增跟进项。
- CA1305/CA1822/CA1859/CA1861/CA1863 仍保留抑制：属风格/性能建议，修复需跨文件批量改动，未纳入本批次。
- `async void` 边界、`Task.Run` 异常传播与大文件拆分（7.11 P3）未动。
- Responses/Anthropic 流式输出；SFT 比例拆片与输出路径存在性提示（206）。

## 24. Round 32 执行记录（2026-11-20）

### 24.1 已完成项

| 项 | 范围 | 提交 |
| --- | --- | --- |
| Batch 22 | Recipe 校验错误码、CSV 类型推断、Sampler count 分层、SFT 比例拆片 | 本次改动 |
| Batch 23 | LLM Responses/Anthropic 流式输出 | 本次改动 |
| Batch 24 | nightly/release/依赖审计工作流与 Dependabot | 本次改动 |

### 24.2 具体改动

- Recipe 校验：`RecipeValidationService` 的全部问题填充稳定 kebab-case `Code`（含 `AddError`/`AddWarning` 帮助方法），属性面板问题行显示 Code；新增全量 Code 断言。
- CSV 类型推断：新增 `CsvTypeInference` 与 `DataWorkbenchViewModel` 首屏采样推断，字段表头展示 integer/number/bool/date/json/string；仅展示，不改变写回。
- Sampler：count 模式按 `StratifyField` 分组 reservoir，按组大小使用最大余数分配总数，输出按源行号排序；分组随机种子改为稳定 FNV-1a，避免 `HashCode.Combine` 的进程随机化。
- SFT Export：新增 `Splits`/`SplitSeed`，复用 `SamplerSplitPlan` 做比例拆片；所有分片先写临时文件，成功后统一原子替换，失败清理；`RecipeOutputTargets` 展开分片文件用于冲突校验与运行锁；面板新增比例拆片与种子编辑及本地化。
- LLM：`Streaming` 扩展至 OpenAI Responses 与 Anthropic。Responses 处理 output item、文本 delta、函数参数 delta/done、completed usage；Anthropic 处理 message_start、content_block_start/delta/stop、message_delta、message_stop 与 structured_output；两者均沿用超时/重试。
- CI/发布：新增 `nightly.yml`（性能与真实 provider gated）、`release.yml`（tag 版本校验、格式/构建/测试、发布/MSIX/Inno、GitHub Release）、`dependabot.yml`；CI 增加 `dotnet list package --vulnerable` 审计 job（当前传递依赖有上游 High 公告，暂为 advisory）。
- 依赖锁定尝试：`RestorePackagesWithLockFile` 与 Debug-only `Avalonia.Diagnostics` 冲突，Debug/Release 锁文件依赖集不一致，已撤回并记录为未完成项。

### 24.3 当前验证

- `dotnet build -c Release`：0 警告，0 错误。
- `dotnet test -c Release`：736 通过，0 失败，7 跳过；`--list-tests` 744 个用例（含 1 个 `[Explicit]`）。
- `dotnet format --verify-no-changes`：通过。
- 覆盖率（本地）：总行 77.55%，Services/Commands 行 85.07%。
- 新增测试覆盖：校验 Code、CSV 推断、count 分层、SFT 比例拆片、成功/失败/冲突路径、三种 provider 流式、采样/分层/序列化与 Headless 面板。
- `scripts/publish-win-x64.ps1 -Version 0.0.0` 发布冒烟通过。
- 未执行：GitHub Actions 实际运行、MSIX/Inno 本机构建、干净机器安装与签名。

### 24.4 本轮未做（保留至下一阶段）

- Tool Profile MCP、共享工具库/鉴权、容器级隔离、严格令牌桶、Query 随机/模型生成。
- 视图索引磁盘化与 10M 行筛选/排序、10M 质量/转换与 CSV/Parquet 1M 质量基线。
- 安装包干净机器验证与签名；双进程单实例自动化；依赖锁定（受 Debug-only 依赖阻塞）。
- 系统剪贴板、全局快捷键、分栏宽度持久化、节点分组/注释、运行日志本地化、CA1001/async void/大文件拆分。
- 输出路径存在性提示（206）、SFT DPO/Pretrain 直出、Seed 多文件/HTTP/HuggingFace 源。
