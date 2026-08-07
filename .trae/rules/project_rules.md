# Project Rules

## Proxy Settings

- 代理端口 `127.0.0.1:10808` 同时承载 SOCKS5 与 HTTP/HTTPS 两种协议（v2rayN 默认配置）。
- 访问 GitHub 等外网资源时需通过代理。
- **git 代理在本地仓库配置中指定 SOCKS5**（`.git/config` 中 `http.proxy = socks5://127.0.0.1:10808`），**无需额外设置环境变量，也无需配置全局 gitconfig**。
  - 全局 `C:\Users\59902081\.gitconfig` 不含代理配置；本仓库代理仅作用于本仓库，避免影响其他仓库。
  - **禁止**修改全局 gitconfig 的代理配置（Sandbox 也会拦截 `~/.gitconfig` 写入）；如需调整，改 `.git/config`。
  - 如确需设置环境变量，须与 git 配置一致，使用 `socks5://` 前缀：
    - `$env:ALL_PROXY="socks5://127.0.0.1:10808"`（推荐，同时覆盖 http/https）
    - 或分别设置：`$env:HTTP_PROXY="socks5://127.0.0.1:10808"` 与 `$env:HTTPS_PROXY="socks5://127.0.0.1:10808"`
- **协议前缀必须是 `socks5://`**，**禁止**写成 `http://127.0.0.1:10808`。
  - 实测：`.git/config` 中 `http.proxy = http://127.0.0.1:10808` 会导致 `fatal: unable to access ... Send failure: Bad access`，改为 `socks5://` 后正常。
- **禁止**混用协议前缀（如 `HTTP_PROXY=http://...` 与 git 的 `socks5://` 共存），会导致 schannel SSL/TLS 握手失败。
- 若推送时遇到 `schannel: failed to receive handshake, SSL/TLS connection failed` 或 `Send failure: Bad access`，先检查：
  - `.git/config` 中 `http.proxy` 是否为 `socks5://127.0.0.1:10808`（不是 `http://`）。
  - 环境变量是否覆盖了 git 的代理配置：`$env:HTTP_PROXY` / `$env:HTTPS_PROXY` 为空时，git 自动使用 `.git/config` 的 `socks5://`。
  - 清除覆盖：`Remove-Item Env:HTTP_PROXY, Env:HTTPS_PROXY, Env:ALL_PROXY -ErrorAction SilentlyContinue`
  - 单次命令临时覆盖代理（不修改配置文件）：`git -c http.proxy=socks5://127.0.0.1:10808 -c https.proxy=socks5://127.0.0.1:10808 <command>`

### GitHub HTTPS 认证方案（禁用 GCM，用 token-in-URL）

- **GCM 是什么**：GitCredentialManager，微软出的独立凭据管理程序，git 需要账号密码时调用它存取 Windows 凭据管理器。正常环境很好用，**但在 socks5 代理下它走 .NET `ServicePointManager` 不支持 socks5，会卡死或报 `ServicePointManager 不支持具有 socks5 方案的代理`**，并反复弹浏览器授权。
- **方案**：彻底禁用 GCM + 在 `remote "origin".url` 嵌入 token，git 直接用 URL 凭据，走自身 socks5 代理，不调用任何外部凭据程序。
- **当前配置**（`.git/config`）：
  ```
  [http]
      proxy = socks5://127.0.0.1:10808
  [credential]
      helper =                          # 空值，清空全局继承的 GCM helper 链
  [remote "origin"]
      url = https://<token>@github.com/KleinPan/ChildNotes.git
  ```
- **为什么 `helper =` 空值能禁用 GCM**：全局 `~/.gitconfig` 有 `[credential] helper = manager`，会被本仓库继承。本地 `.git/config` 写 `helper =`（空值）会清空 helper 链，git 就不会调用 GCM。这是只改本地、不动全局的禁用方式。
- **token 失效后**：用户生成新 token 后，用 `git remote set-url origin https://<new-token>@github.com/KleinPan/ChildNotes.git` 更新；或直接编辑 `.git/config`。
- **禁止**依赖 GCM 浏览器授权流程（socks5 环境下不可用）。
- **禁止**把 token 写入项目规则、文档、提交到仓库；token 仅存在于本地 `.git/config`（不入版本控制）。
- 验证连通性：`git push origin master:master` 或 `git fetch origin master`（应秒级返回，不弹窗、不卡死）。

## Build Commands

- Avalonia 项目构建：`cd ChildNotes && dotnet build ChildNotes\ChildNotes.csproj -v quiet --nologo`
- Web 项目构建：`cd web && npm run build`（如需要）

## Git Push 规则

- **禁止** `git push --force` / `--force-with-lease` 到 master/main，除非用户明确要求。
- 遇 non-fast-forward 优先 `git pull --rebase`，不要强制覆盖。
- **远端为 GitHub**（`https://github.com/KleinPan/ChildNotes.git`），代理走 `127.0.0.1:10808`（SOCKS5），认证用 token-in-URL（见上文"GitHub HTTPS 认证方案"）。
- 推送分支：`git push origin master:master`（带显式 refspec，稳妥）。
- 推送 tag：`git push origin refs/tags/vX.Y.Z:refs/tags/vX.Y.Z`。
- 全局 `C:\Users\59902081\.gitconfig` 不含 `remote.origin.push` 误配（早期规则提到的 Gerrit 风格误配已不存在），裸 `git push origin master` 也可用，但仍推荐显式 refspec。

## 提交粒度与 Tag 策略（重要）

- **每个问题/bug 单独 commit**：每解决一个问题或修复一个 bug，必须独立成一个 commit，不要把多个不相关的修复打包到一个 commit 里。
  - 一个任务涉及多个相关子改动可合并为一个 commit，但不同任务/bug 必须分开。
  - commit message 遵循 Conventional Commits（如 `fix:` / `feat:` / `refactor:`），中文描述。
- **默认不打 tag**：完成开发并推送分支后，**默认不打 tag**，也不主动询问打 tag 的时机。
  - 仅当**用户明确要求**打 tag 时，才按下方"Tag 推送完整流程"操作。
  - 不要因为"重大重构"或"新版本"自行决定打 tag，一切以用户指令为准。
  - **禁止在用户只说"提交代码"时连带打 tag**：用户说"提交"只授权 commit + push，不授权 tag。
    即使前一次任务用户授权过打 tag，**下一次打 tag 必须重新提问并经用户明确确认**，不能沿用上次的授权。
  - **打 tag 前必须提问**：如果 AI 认为需要打 tag，必须先用 AskUserQuestion 或文字提问"是否需要打 tag vX.Y.Z？"，等用户回复"打"/"是"/"确认"等明确肯定后才能打。
    禁止在未提问或用户未确认的情况下自行打 tag。

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
  - 前端构建（避开 Android Java 编译问题）：`dotnet build ChildNotes\ChildNotes\ChildNotes.csproj -v quiet --nologo`

## 新增页面/ViewModel 必须注册到 ViewLocator（重要）

项目使用显式 switch 的 `ViewLocator`（[ViewLocator.cs](file:///e:/0_Code/5_Git/AiJi/ChildNotes/ChildNotes/ViewLocator.cs)）做 ViewModel → View 映射，**不使用反射**，以兼容 AOT / Trimming（iOS Release、Android Release AOT）。

**新增任何独立导航的 ViewModel + View 时，必须同时：**

1. 在 `ViewLocator.cs` 的 `switch` 表达式中追加一条分支：
   ```csharp
   XxxViewModel => new XxxView(),
   ```
2. 在 `MainShellViewModel` 中注册 Overlay（如果是设置类弹层页面）：
   - 声明 `[ObservableProperty] private bool _isXxxOpen;` 和 `[ObservableProperty] private XxxViewModel _xxx;`
   - 构造函数中 `_xxx = new XxxViewModel();`（**容易漏，会触发 RegisterOverlay NPE**）
   - `RegisterOverlay(Xxx, () => IsXxxOpen = false, () => IsXxxOpen);`
   - 添加 `public void OpenXxx() { Xxx.Activate(); IsXxxOpen = true; }`
3. 在 `MainShellView.axaml` 添加 `ContentControl` 绑定（如果是 Overlay 页面）
4. 在 `MineView.axaml`（或其他入口页）添加跳转入口 + `MineView.axaml.cs` 事件处理

**遗漏 ViewLocator 注册的症状**：页面打不开，ContentControl 显示 "View Not Mapped: XxxViewModel"。
**遗漏 MainShellViewModel 实例化的症状**：`RegisterOverlay(xxx, ...)` 抛 `NullReferenceException`。

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
- **Everywhere**（[https://github.com/Sylinko/Everywhere](https://github.com/Sylinko/Everywhere)）：上下文感知的桌面 AI 助手开源项目，技术栈与本项目完全一致（.NET 10 + Avalonia 12.0.5），6.1K+ Stars。作为 Avalonia 跨平台桌面应用的工程架构参考（三平台构建 / 多 LLM 集成 / i18n 源生成器 / 终端 PTY / 运行时补丁等），详见 [everywhere-reference.md](file:///e:/0_Code/5_Git/AiJi/.trae/rules/everywhere-reference.md)。
