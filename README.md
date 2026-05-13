# LLM Training Data Studio

LLM Training Data Studio 是一个基于 **C# / Avalonia** 的桌面端训练数据处理工作台，面向 LLM 预训练、SFT、DPO 和工具调用数据集的可视化编辑、验证、转换与流水线编排。

项目目标是为 JSONL / CSV / Parquet 等常见训练数据格式提供专业级 GUI：支持大文件流式处理、数据格式互转、DAG Recipe 编排、可视化 Tool Schema 编辑、异步执行、实时进度与结果预览。

## 当前状态

当前版本已完成应用骨架和核心服务初版：

- Avalonia 桌面应用启动框架
- 三栏式 Studio UI 布局
- Block Sheet / Canvas / Property Panel / Status Bar
- Recipe DAG 节点模型与示例流程
- 异步 Validate / Run 命令框架
- JSONL SQLite 行索引缓存服务
- OpenAI / ShareGPT / Alpaca 数据格式转换服务初版
- JSONL 语法验证与简单自动修复入口
- Tool Schema 数据模型

> 注意：当前仍是早期开发版，部分交互如真实节点拖拽、连线创建、自动布局、完整文件读写和输出预览弹窗仍在开发中。

## 技术栈

- **语言**：C#
- **框架**：Avalonia UI
- **运行时**：.NET 10（项目当前 `TargetFramework` 为 `net10.0`）
- **数据缓存**：SQLite / `Microsoft.Data.Sqlite`
- **CSV 支持**：`CsvHelper`
- **Parquet 支持**：`Parquet.Net`
- **UI 主题**：Avalonia Fluent Theme + Inter Font
- **架构模式**：MVVM

## 主要功能规划

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
- Validator Block：Schema / 代码 / 数据质量校验
- Sampler Block：确定性采样与数据切分
- Tool Profile Block：OpenAI-compatible Tool Schema / MCP 工具配置

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
LMTrainingDataStudio2/                      # 主项目目录
├── Program.cs                              # 应用入口，配置 Avalonia AppBuilder
├── App.axaml / App.axaml.cs                # Application 定义，主题与资源加载
├── app.manifest                            # Windows 应用清单
├── LMTrainingDataStudio2.csproj            # 项目文件（Avalonia 11.3 / .NET 10）
│
├── Models/                                 # 数据模型层
│   ├── AppSettings.cs                      # 全局设置（缓存容量、主题、网格吸附等）
│   ├── ChatMessage.cs                      # 规范化聊天消息、ToolDefinition、ToolCall、SftSample
│   ├── DatasetFormat.cs                    # DatasetFormat / DataFileType 枚举
│   ├── RecipeBlock.cs                      # Recipe DAG 节点、端口、边、Recipe 容器
│   ├── ToolSchema.cs                       # Tool Schema 树形节点模型、ToolProfile
│   └── ValidationResult.cs                 # 验证结果与 Issue 模型
│
├── Services/                               # 业务服务层（无 UI 依赖）
│   ├── DatasetFormatConverter.cs           # OpenAI / ShareGPT / Alpaca 格式互转
│   ├── DatasetValidationService.cs         # JSONL 逐行语法验证 + 自动修复
│   ├── IndexCacheService.cs                # SQLite 行偏移索引缓存（大文件随机访问）
│   └── RecipeValidationService.cs          # Recipe DAG 验证（环检测、配置检查）
│
├── ViewModels/                             # MVVM ViewModel 层
│   ├── MainWindowViewModel.cs              # 主窗口 VM（Recipe 管理、Run/Validate 命令）
│   ├── BlockNodeViewModel.cs               # 画布节点 VM + PortViewModel
│   ├── BlockTemplateViewModel.cs           # Block Sheet 模板项 VM
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
- .NET SDK 10 或与项目 `TargetFramework` 匹配的 SDK

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

### 运行项目

```powershell
dotnet run --project .\LMTrainingDataStudio2\LMTrainingDataStudio2.csproj
```

## UI 说明

### Recipe Header

用于编辑 Recipe 名称、切换 Editor / Executions 视图，以及访问全局设置或缓存清理入口。

### Block Sheet

展示可添加的 Block 模板，包括 Seed、LLM、Expression、Validator、Sampler 和 Tool Profile。

### Canvas Area

用于展示 Recipe DAG。当前版本包含示例节点和示例连接曲线，后续将支持：

- 节点拖拽
- Port 连线
- 多选与框选
- 缩放和平移
- 自动布局
- Fit to View
- Undo / Redo

### Property Panel

根据当前选中的 Block 显示配置项，包括：

- Jinja 引用名
- Streaming 配置
- Index Cache 配置
- Prompt / Jinja 编辑器占位
- OpenAI Tool Schema 树形编辑器占位
- Validation 摘要
- Progress / Logs

### Status Bar

显示当前状态、行数、文件路径、进度条和内存占用。

## 数据格式示例

### OpenAI-compatible

```json
{"messages":[{"role":"system","content":"You are helpful."},{"role":"user","content":"Hello"},{"role":"assistant","content":"Hi!"}]}
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

- [ ] 文件打开 / 保存 / 另存为
- [ ] JSONL / CSV / Parquet 流式读取与分页预览
- [ ] 大文件随机行跳转
- [ ] 数据增删查改
- [ ] 去重、混洗、切分文件
- [ ] 正则搜索与替换
- [ ] OpenAI / ShareGPT / Alpaca 完整批量互转
- [ ] DPO / Pretrain 格式支持
- [ ] 语法错误弹窗与一键修复交互
- [ ] Tool Schema 可视化增删改
- [ ] Canvas 节点拖拽与 Port 连线
- [ ] Edge 动画与样本预览弹窗
- [ ] Mini-map 真实视口同步
- [ ] Command Pattern 撤销 / 重做
- [ ] 亮色 / 暗色主题切换
- [ ] LiveCharts2 或 OxyPlot 进度图表
- [ ] 执行完成后的 output 目录摘要预览
- [ ] 单元测试与集成测试

## 编码约定

- 使用 MVVM 分层，避免在 View 中写业务逻辑。
- 所有 IO / 解析 / 写入操作使用 `Task`、`async` / `await` 和 `IProgress<T>`。
- 禁止在 UI 线程执行长耗时操作。
- 大文件处理必须采用流式读取，不允许一次性加载完整文件。
- 服务类放在 `Services/`，数据模型放在 `Models/`，视图模型放在 `ViewModels/`。
- 命名遵循 C# 标准 PascalCase / camelCase 规范。

## License

当前项目尚未声明许可证。发布前请根据实际用途添加 LICENSE 文件。
