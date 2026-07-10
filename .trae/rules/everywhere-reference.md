# Everywhere 开发参考

## 定位（2026-07-10）

`Everywhere` 是一个**技术栈与本项目高度一致**的 Avalonia 跨平台桌面应用开源项目，作为本项目在 **Avalonia 桌面端架构 / 跨平台构建 / AI 集成 / i18n / 终端**等方向的**开发参考项目**，不引入 NuGet 包，仅在需要时通过 `mcp_gread` 工具在线查阅其源码借鉴实现思路。

> 注意：本项目正式发布平台是 Android（移动端），Everywhere 是**桌面端**应用，两者业务领域不同。Everywhere 的参考价值在于**跨平台桌面工程的工程实践**，而非业务功能。

## Everywhere 项目信息

| 维度 | 详情 |
|---|---|
| GitHub | https://github.com/Sylinko/Everywhere |
| 许可证 | MIT（可借鉴源码，无法律风险） |
| GitHub Stars | 6,100+ |
| 主要语言 | C# |
| 目标框架 | net10.0 |
| 依赖 Avalonia 版本 | 12.0.5（**与本项目完全一致**） |
| .NET SDK | 10.202 |
| MVVM 框架 | `CommunityToolkit.Mvvm` 8.4.2 |
| 响应式集合 | `DynamicData` 9.4.31 |
| 数据库 | `Microsoft.EntityFrameworkCore.Sqlite` 10.0.9 |
| AI 框架 | `Microsoft.SemanticKernel` 1.75.0 |
| Markdown 渲染 | `LiveMarkdown.Avalonia` 2.2.0 |
| 代码编辑器 | `Avalonia.AvaloniaEdit` 12.0.0 |
| 主题/控件库 | `shad-ui`（自研，见 `3rd/shad-ui`） |
| 平台支持 | Windows / macOS / Linux 三平台（已全部落地） |
| 官方文档 | https://everywhere.sylinko.com/zh-CN/ |

## 解决方案结构

Everywhere 采用**平台头部项目 + 共享核心项目 + 抽象/工具项目**的多项目结构，值得借鉴：

```
Everywhere.slnx                    # 统一入口（slnx 格式，与本项目一致）
Everywhere.Windows.slnx            # 平台专用 solution filter
Everywhere.Mac.slnx
Everywhere.Linux.slnx
src/
├── Everywhere.Abstractions/       # 抽象层（AI/Chat/Cloud/Rpc/Skills/I18N/Configuration）
├── Everywhere.Core/               # 共享核心（App.axaml / ViewModels / Views / Chat / AI / Database）
├── Everywhere.Windows/            # Windows 平台头部（Program.cs / Interop / 原生 API）
├── Everywhere.Mac/                # macOS 平台头部（AppDelegate.cs / Info.plist / Entitlements.plist）
├── Everywhere.Linux/              # Linux 平台头部（Program.cs / PKGBUILD / deb 打包脚本）
├── Everywhere.Cloud/              # 云端同步（OAuthCloudClient / CloudDbSynchronizer）
├── Everywhere.Terminal/           # 终端会话（PTY / Shell 集成 / 输出解析）
├── Everywhere.Watchdog/           # 看门狗（WindowsJobObject，进程退出清理）
├── Everywhere.I18N.Abstractions/  # i18n 抽象（LocaleManager / DynamicLocaleKey）
├── Everywhere.I18N.SourceGenerator/  # i18n 源生成器（编译期生成多语言键）
├── Everywhere.Configuration.SourceGenerator/  # 配置源生成器
└── Build.*.targets                # 构建目标（Versioning / Telemetry / Patches / Watchdog）
3rd/                               # 第三方库 fork（submodule）
├── semantic-kernel/               # Semantic Kernel（含 patch）
├── shad-ui/                       # 自研 Avalonia UI 组件库
├── Porta.Pty/                     # 跨平台 PTY
├── MessagePack-CSharp/
└── ...
patches/                           # 针对 Avalonia / AvaloniaEdit / SemanticKernel 的运行时补丁
.github/workflows/                 # 三平台发布 workflow
├── windows-release.yml
├── macos-release.yml
├── linux-release.yml
├── aur-publish.yml                # Arch Linux AUR 发布
├── sync-i18n.yml                  # i18n 同步
└── update-server-publish.yml      # 自更新服务端
```

## 可借鉴的工程实践

### 1. 多平台头部项目分层（与本项目对比）

Everywhere 的三平台头部项目（`Everywhere.Windows` / `Everywhere.Mac` / `Everywhere.Linux`）各自包含 `Program.cs` / `Global.cs` / `Interop/` / `I18N/` / `Chat/` / `Common/`，将平台差异隔离在头部项目内，共享逻辑全部在 `Everywhere.Core`。

| 维度 | Everywhere | 本项目（ChildNotes） |
|---|---|---|
| 桌面平台 | Windows + macOS + Linux 三平台 | 仅 Windows（开发调试用） |
| 移动平台 | 无 | Android（正式发布）+ iOS（潜在） |
| 头部项目 | `Everywhere.Windows` / `.Mac` / `.Linux` | `ChildNotes.Desktop`（Windows） |
| 共享核心 | `Everywhere.Core` | `ChildNotes`（主项目） |
| 抽象层 | `Everywhere.Abstractions` | `ChildNotes.Shared`（纯 POCO） |

**借鉴点**：本项目若未来扩展 macOS/Linux 桌面端，可参考 Everywhere 的"平台头部 + 共享 Core"分层模式，将平台 P/Invoke / 原生 API 隔离在头部项目。

### 2. 跨平台构建 workflow（三平台全落地）

Everywhere 的三平台 release workflow 均已落地，可作为本项目未来扩展桌面平台的 CI 参考：

| Workflow | 平台 | 产物 | 关键点 |
|---|---|---|---|
| `windows-release.yml` | Windows | `.exe` Setup + 免安装 zip | Inno Setup（`tools/installer.iss`） |
| `macos-release.yml` | macOS | `.dmg` | 签名 + 公证（`tools/create_dev_cert.sh`） |
| `linux-release.yml` | Linux | `.AppImage` + `.deb` | `build-appimage.sh` / `packaging_deb.sh` |
| `aur-publish.yml` | Arch Linux | AUR 包 | PKGBUILD |

**对比本项目的 iOS 困境**：Everywhere 在 macOS 上成功构建（`Everywhere.Mac.csproj`），说明 .NET 10 + Avalonia 在 macOS 上的 toolchain 是可行的。本项目 iOS 的 ILLink 超时问题在 macOS 桌面端**不存在**（桌面端不需要 trimming），若未来做 macOS 桌面版可避开此问题。

### 3. 源生成器（Source Generator）应用

Everywhere 大量使用 Roslyn 源生成器，值得借鉴：

| 源生成器 | 路径 | 用途 |
|---|---|---|
| `Everywhere.I18N.SourceGenerator` | `src/Everywhere.I18N.SourceGenerator/` | 编译期生成多语言键的强类型访问类 |
| `Everywhere.Configuration.SourceGenerator` | `src/Everywhere.Configuration.SourceGenerator/` | 编译期生成配置项的强类型访问类 |

**借鉴点**：本项目当前 i18n 采用运行时字典查找，若未来需要强类型多语言键（避免字符串拼写错误），可参考 Everywhere 的 `I18NGenerator.cs` 实现编译期生成。

### 4. i18n 多语言架构

Everywhere 的 i18n 架构分离抽象与实现：

```
Everywhere.I18N.Abstractions/     # 抽象层（LocaleManager / DynamicLocaleKey / LocaleChangedMessage）
Everywhere.Core/I18N/             # 核心层实现
Everywhere.Windows/I18N/          # Windows 平台特有
Everywhere.Mac/I18N/              # macOS 平台特有
Everywhere.Linux/I18N/            # Linux 平台特有
```

支持 12+ 语言（简中/英/德/西/法/意/日/韩/俄/土/繁中/繁中港），通过 `sync-i18n.yml` workflow 自动同步翻译。

### 5. Semantic Kernel 多 LLM 集成

Everywhere 通过 Semantic Kernel 统一集成多家 LLM，架构清晰：

| 文件 | 对接的 LLM |
|---|---|
| `AI/OpenAIKernelMixin.cs` | OpenAI |
| `AI/OpenAIResponsesKernelMixin.cs` | OpenAI Responses API |
| `AI/AnthropicKernelMixin.cs` | Anthropic Claude |
| `AI/GoogleKernelMixin.cs` | Google Gemini |
| `AI/OllamaKernelMixin.cs` | Ollama（本地模型） |

通过 `IKernelMixinFactory` / `KernelMixinFactory` 工厂模式创建，`ModelAvailability` 管理模型可用性。包含 `3rd/semantic-kernel-patch/`（含 Google Connector patch）。

**借鉴点**：本项目若未来集成 AI 能力（如智能育儿建议），可参考此工厂模式。

### 6. 终端会话（PTY）实现

`Everywhere.Terminal` 项目实现了完整的跨平台终端会话，基于 `3rd/Porta.Pty/`：

| 文件 | 职责 |
|---|---|
| `TerminalSession.cs` | 终端会话生命周期 |
| `TerminalParser.cs` | 输出解析（VT100 转义序列） |
| `TerminalLineBuffer.cs` | 行缓冲 |
| `TerminalRun.cs` | 文本样式 run |
| `PtyTextDecoder.cs` | PTY 输出解码 |
| `ShellIntegrationScript.cs` | Shell 集成脚本 |
| `ShellIntegrationMarker.cs` | Shell 集成标记 |
| `OutputCleaner.cs` | 输出清理 |
| `ExecuteStrategy.cs` / `RichExecuteStrategy.cs` / `NoneExecuteStrategy.cs` | 执行策略 |
| `ShellType.cs` | Shell 类型识别 |

**借鉴点**：本项目若未来需要内嵌终端（如调试日志查看），可参考此实现。

### 7. 运行时补丁机制（patches/）

Everywhere 通过 `patches/` 目录对 Avalonia / AvaloniaEdit / SemanticKernel 进行运行时补丁，包含 `Everywhere.Patches.Avalonia.Controls` / `Everywhere.Patches.Avalonia.Native` / `Everywhere.Patches.AvaloniaEdit` / `Everywhere.Patches.SemanticKernel`，通过 `Everywhere.BuildTask.Patcher` 在构建期注入。

**借鉴点**：本项目若遇到 Avalonia 框架级 bug 且无法等官方修复，可参考此补丁机制。

### 8. 看门狗进程（Watchdog）

`Everywhere.Watchdog` 通过 `WindowsJobObject` 实现进程退出清理，确保主进程崩溃时子进程不残留。

### 9. 自更新服务

`update-server-publish.yml` 发布自更新服务端，配合 `Everywhere.Cloud/CloudDbSynchronizer.cs` 实现云端同步。

### 10. NuGet 包版本集中管理

Everywhere 使用 `Directory.Packages.props` 集中管理所有 NuGet 包版本（`ManagePackageVersionsCentrally=true`），通过属性变量复用版本号：

```xml
<PropertyGroup>
  <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  <MicrosoftPackageVersion>10.0.9</MicrosoftPackageVersion>
  <AvaloniaVersion>12.0.5</AvaloniaVersion>
  <SemanticKernelVersion>1.75.0</SemanticKernelVersion>
  ...
</PropertyGroup>
```

**与本项目一致**：本项目也使用 `Directory.Packages.props` 集中管理，可参考 Everywhere 的属性变量复用模式（如统一 `MicrosoftPackageVersion`）。

## 何时参考 Everywhere

| 场景 | 可参考的内容 |
|---|---|
| 扩展 macOS / Linux 桌面端 | 平台头部项目结构 + CI workflow |
| 集成 AI 能力 | Semantic Kernel 多 LLM 工厂模式 |
| 强类型 i18n | `I18N.SourceGenerator` 编译期生成 |
| 强类型配置 | `Configuration.SourceGenerator` 编译期生成 |
| 内嵌终端 | `Everywhere.Terminal` PTY 实现 |
| Avalonia 框架级 bug 修复 | `patches/` 运行时补丁机制 |
| 跨平台原生 API 调用 | 各平台 `Interop/` 目录组织 |
| 看门狗进程 | `Everywhere.Watchdog` / `WindowsJobObject` |
| 云端同步 | `Everywhere.Cloud` / `OAuthCloudClient` |
| Markdown 渲染 | `LiveMarkdown.Avalonia` 集成方式 |
| 代码编辑器 | `AvaloniaEdit` 集成方式 |

## 参考方式

- **在线浏览**：https://github.com/Sylinko/Everywhere
- **官方文档**：https://everywhere.sylinko.com/zh-CN/
- **mcp_gread 工具查阅**（推荐）：
  - `view_repo`：查看仓库基本信息和目录树
  - `list_tree`：查看指定路径的目录结构
  - `read_code`：读取指定文件的源码
  - `search_code`：按关键词搜索源码
- **不引入 NuGet 包**：Everywhere 的组件（如 `shad-ui`）未发布为独立 NuGet 包，仅作源码参考。

## 与 Ursa.Avalonia 参考的定位差异

| 维度 | Ursa.Avalonia | Everywhere |
|---|---|---|
| 参考类型 | 控件库 | 完整应用 |
| 参考粒度 | 单个控件的实现思路 | 整体工程架构与跨平台实践 |
| 技术栈重叠 | Avalonia（部分重叠） | .NET 10 + Avalonia 12.0.5（**完全一致**） |
| 业务相关性 | 无关（通用控件） | 无关（AI 助手 vs 育儿记录） |
| 借鉴场景 | UI 控件实现 | 工程架构 / CI / AI 集成 / i18n / 终端 |

## 相关本地文件

| 文件 | 说明 |
|---|---|
| [project_rules.md](file:///e:/0_Code/5_Git/AiJi/.trae/rules/project_rules.md) | 项目规则（"第三方库参考"段已登记 Everywhere） |
| [ursa-avalonia-reference.md](file:///e:/0_Code/5_Git/AiJi/.trae/rules/ursa-avalonia-reference.md) | Ursa.Avalonia 参考文档（控件库参考） |
| [Directory.Packages.props](file:///e:/0_Code/5_Git/AiJi/ChildNotes/Directory.Packages.props) | 本项目 NuGet 包版本管理（可对比 Everywhere 的集中管理） |
| [release.yml](file:///e:/0_Code/5_Git/AiJi/.github/workflows/release.yml) | 本项目 CI Release workflow（可对比 Everywhere 三平台 workflow） |
