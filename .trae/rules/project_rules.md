# Project Rules

## Proxy Settings

- 代理端口 `127.0.0.1:10808` 同时承载 SOCKS5 与 HTTP/HTTPS 两种协议（v2rayN 默认配置）。
- 访问 GitHub 等外网资源时需通过代理。
- **git 已在全局配置中指定 SOCKS5**（`git config --global http.proxy socks5://127.0.0.1:10808`），**无需额外设置环境变量**。
  - 如确需设置环境变量，须与 git 配置一致，使用 `socks5://` 前缀：
    - `$env:ALL_PROXY="socks5://127.0.0.1:10808"`（推荐，同时覆盖 http/https）
    - 或分别设置：`$env:HTTP_PROXY="socks5://127.0.0.1:10808"` 与 `$env:HTTPS_PROXY="socks5://127.0.0.1:10808"`
- **禁止**混用协议前缀（如 `HTTP_PROXY=http://...` 与 git 的 `socks5://` 共存），会导致 schannel SSL/TLS 握手失败。
- 若推送时遇到 `schannel: failed to receive handshake, SSL/TLS connection failed`，先检查环境变量是否覆盖了 git 的代理配置：
  - `$env:HTTP_PROXY` / `$env:HTTPS_PROXY` 为空时，git 自动使用自身配置的 `socks5://`。
  - 清除覆盖：`Remove-Item Env:HTTP_PROXY, Env:HTTPS_PROXY, Env:ALL_PROXY -ErrorAction SilentlyContinue`

## Build Commands

- Avalonia 项目构建：`cd ChildNotes && dotnet build ChildNotes\ChildNotes.csproj -v quiet --nologo`
- Web 项目构建：`cd web && npm run build`（如需要）

## Git Push 规则

- **禁止** `git push --force` / `--force-with-lease` 到 master/main，除非用户明确要求。
- 遇 non-fast-forward 优先 `git pull --rebase`，不要强制覆盖。
- 代理设置见 Proxy Settings 段；远端为 GitHub，分支/tag 推送用标准 `git push origin <name>`。

## 提交粒度与 Tag 策略（重要）

- **每个问题/bug 单独 commit**：每解决一个问题或修复一个 bug，必须独立成一个 commit，不要把多个不相关的修复打包到一个 commit 里。
  - 一个任务涉及多个相关子改动可合并为一个 commit，但不同任务/bug 必须分开。
  - commit message 遵循 Conventional Commits（如 `fix:` / `feat:` / `refactor:`），中文描述。
- **默认不打 tag**：完成开发并推送分支后，**默认不打 tag**，也不主动询问打 tag 的时机。
  - 仅当**用户明确要求**打 tag 时，才按下方"Tag 推送完整流程"操作。
  - 不要因为"重大重构"或"新版本"自行决定打 tag，一切以用户指令为准。

## 提交信息（Commit Message）正确写法

- PowerShell 不支持 heredoc，**多行 message 必须用文件方式**：写临时文件 → `git commit -F .git\COMMIT_MSG_TMP.txt` → 删除临时文件。
- 单行 message 可直接 `git commit -m "..."`。
- **禁止** `git commit -m "$(cat <<'EOF' ... EOF)"`（heredoc 解析失败）。

## Tag 推送完整流程（按需参阅）

仅在**用户明确要求打 tag**时参阅 [git-tag-procedure.md](file:///e:/0_Code/5_Git/AiJi/.trae/rules/git-tag-procedure.md)（含版本号约定、annotated tag 命令、推送顺序、删除误推 tag）。

始终遵守：必须用 annotated tag（`git tag -a`），禁止轻量级 tag。

## 共享代码契约（ChildNotes.Shared）

- 前后端共享的纯 POCO / 常量 / DTO / 协议契约 / 实体核心字段基类统一放在 `ChildNotes.Shared/` 项目（net10.0，不依赖任何 UI 或 ORM 框架）。
- 命名空间约定：
  - 常量：`ChildNotes.Shared.Constants`
  - DTO：`ChildNotes.Shared.Dtos`
  - 同步协议：`ChildNotes.Shared.Sync`
  - 实体基类：`ChildNotes.Shared.Entities`
- **禁止**在前后端项目中重复定义已存在于 Shared 的类型；新增共享类型时优先放入 Shared。
- 前端实体继承 Shared 基类后，保留前端独有成员（`DeviceId`/`SyncedAt`/UI 计算属性等）；后端实体继承 Shared 基类并实现 `IAuditable` 接口，保留后端独有字段（如 `AppUser.ReferrerUserId`）。
- 前端为兼容历史命名，可使用 `using` 别名（如 `using BabyFamilyItem = ChildNotes.Shared.Dtos.BabyFamilyDto;`），避免大范围调用方改动。
- 前端命名空间 `ChildNotes.Infrastructure`（本地服务定位器）与后端 `ChildNotes.Infrastructure` 项目（EF Core/服务实现）内容完全无关，迁移时不要混淆。

## 解决方案结构

- 仓库根级 `AiJi.slnx` 为统一入口，包含 `ChildNotes.Shared` + 前后端主项目（非测试）。
- 因 slnx 格式要求项目名唯一，前后端各有一个 `ChildNotes.Tests` 项目，未纳入根 slnx；测试项目通过各自子 slnx 打开：
  - 前端测试：`ChildNotes/ChildNotes.slnx`
  - 后端测试：`ChildNotes.Backend/ChildNotes.Backend.slnx`
- 构建/测试命令：
  - 后端构建：`dotnet build ChildNotes.Backend\ChildNotes.Backend.slnx -v quiet --nologo`
  - 后端测试：`dotnet test ChildNotes.Backend\ChildNotes.Backend.slnx --no-build -v quiet --nologo`
  - 前端构建（避开 Android Java 编码问题）：`dotnet build ChildNotes\ChildNotes\ChildNotes.csproj -v quiet --nologo`

## 提交前自检

- 修改共享代码或实体后，必须同时验证前后端构建均 0 错误，后端测试全通过，再提交。
- 提交信息遵循 Conventional Commits（如 `refactor(shared):` / `feat:` / `fix:`），中文描述。
- 不要在提交中包含 `.env`、凭据文件、`bin/`、`obj/`、运行产物（如 `ui-check-reports/`）。
- **Tag 策略遵循"提交粒度与 Tag 策略"段**：默认不打 tag，需用户明确要求时才打。

## 版本号管理（.NET SDK Git 后缀问题）

### 问题

.NET SDK 的 `GenerateAssemblyInfo` 目标会自动将 Git 提交哈希追加到 `InformationalVersion`，
形成 `0.3.0+14a6b2c` 格式。Android 的 `android:versionName` 不支持此格式，
前端 UI 读取的版本号也会显示带后缀的值。

### 解决方案

在仓库根目录 [Directory.Build.targets](file:///e:/0_Code/5_Git/AiJi/Directory.Build.targets) 中设置官方属性：

```xml
<PropertyGroup>
  <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
</PropertyGroup>
```

**为什么不用其他方式：**
- `<SourceRevisionId></SourceRevisionId>` — 在某些 SDK 版本中不可靠
- MSBuild Target 正则替换 — 时机太晚，程序集源码已生成
- `IncludeSourceRevisionInInformationalVersion` — 从源头关闭，SDK 生成 AssemblyInfo.cs 时就不写入后缀

### 相关文件

| 文件 | 作用 |
|------|------|
| [Directory.Build.targets](file:///e:/0_Code/5_Git/AiJi/Directory.Build.targets) | 全局关闭 Git 后缀追加 |
| [Directory.Build.props](file:///e:/0_Code/5_Git/AiJi/Directory.Build.props) | 统一版本号默认值（回退到 0.0.0） |
| [ChildNotes.Android.csproj](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes.Android/ChildNotes.Android.csproj) | Android 专用版本属性 |
| [release.yml](file:///e:/0_Code/5_Git/AiJi/.github/workflows/release.yml) | CI Release 构建时用 tag 覆盖版本号 |

### CI Release 构建命令示例

```bash
dotnet publish ChildNotes.Android/ChildNotes.Android.csproj \
  -c Release \
  -p:SourceRevisionId= \
  -p:Version=0.3.0 \
  -p:InformationalVersion=0.3.0 \
  -p:ApplicationDisplayVersion=0.3.0
```

## 移动端 Release 构建注意事项（.NET 10 SDK）

- **Android**：`Microsoft.Android.Sdk` 在 Release 配置下默认启用 `RunAOTCompilation`，但该属性要求 `PublishTrimmed=true`。本项目未启用 trimming，必须在 csproj 显式设置 `<RunAOTCompilation>false</RunAOTCompilation>`，否则报 `XA1030` 错误。
- **iOS**：`Microsoft.iOS.Sdk` 强制要求 `PublishTrimmed=true`，不能像 Android 那样关掉。理论上要禁用实际 trimming，需在 publish 命令同时传三个参数：
  - `-p:PublishTrimmed=true`（满足 SDK 强制要求）
  - `-p:MtouchLink=None`（跳过 Xamarin.iOS 专用 linker）
  - `-p:TrimMode=copy`（让 .NET ILLink trimmer 仅复制程序集不做裁剪分析）

  **但实测在 GitHub Actions macOS runner 上无效**：`TrimMode=copy` 没生效，ILLink 仍在 `IL stripping assemblies` 阶段做全量分析，45 分钟 timeout 杀进程后报 `The operation was canceled`（注意：这个报错文案误导，实际是超时，不是真取消）。各种属性组合（csproj 持久化、命令行传入）均无效。

  **决策（v0.2.4 起）**：iOS 不在 CI 构建矩阵中。原因：
  1. ILLink 超时问题在 CI 环境下无解；
  2. CI 产物是未签名 `.app`，用户仍需 Mac + 开发者证书重签才能安装，能重签的用户也能自行 `dotnet publish`。

  如需本地构建 iOS，命令见 [release.yml](file:///e:/0_Code/5_Git/AiJi/.github/workflows/release.yml) 中的注释段。
- 这两个平台都受 `SQLitePCLRaw.lib.e_sqlite3` 高危漏洞警告（NU1903），暂未升级，关注后续版本。
- release workflow 触发条件：推送 `v*` 或 `release-*` tag。修复构建问题应递增 patch 版本打新 tag（如 `v0.2.1` → `v0.2.2`），不要删除重打已发布的 tag。

## 平台开发规则（重要）

各平台定位与功能要求存在差异，团队成员必须知晓并严格遵守：

### 1. 平台定位

| 平台 | 定位 | 用途 |
| --- | --- | --- |
| Windows | 开发调试平台 | 仅用于开发阶段的调试工作，无需依赖安卓模拟器或物理设备 |
| Android | 正式发布平台 | 作为正式发布并供用户实际使用的平台 |
| iOS | 潜在扩展平台 | 作为未来可能扩展发布的潜在平台 |

### 2. 功能与性能要求

- **Windows 端**：可集成各类调试开关、埋点系统及日志输出功能，以最大化调试便利性。不受性能优化约束，优先保证调试信息完整可见。
- **Android 端**：
  - 非必要情况下，应最小化影响性能的日志输出、埋点及调试功能；
  - 所有性能相关代码必须经过优化处理；
  - 发布构建中应通过条件编译或运行时开关关闭调试专用逻辑，避免影响用户体验。

### 3. 开发环境限制

- **Android 端当前无法在开发电脑上进行编译操作**，原因是加密系统会干扰文件解析过程。
- 开发团队后续无需尝试在当前环境中编译 Android 平台代码；Android 构建通过 GitHub Actions Release workflow 在 CI 环境完成。
- 本地开发调试一律在 Windows 平台进行。

### 4. 实施约定

- 调试专用代码（日志、埋点、调试开关）应通过编译符号（如 `DEBUG`）或平台条件（`#if WINDOWS` / 运行时平台判断）隔离，避免泄漏到 Android Release 产物。
- 严禁在 Android Release 路径上保留未受控的 `Console.WriteLine` / `Debug.WriteLine` / 详细日志输出。
- 涉及平台差异的实现应集中放置，便于后续维护与平台扩展（如未来 iOS 接入）。

## 第三方库参考

- **Ursa.Avalonia**：作为开发参考项目（未引入 NuGet 包），新需求或优化时可借鉴其控件实现思路，详见 [ursa-avalonia-reference.md](file:///e:/0_Code/5_Git/AiJi/.trae/rules/ursa-avalonia-reference.md)。
