# Project Rules

## Proxy Settings

- HTTP/HTTPS 代理地址：`127.0.0.1:10808`
- 访问 GitHub 等外网资源时需通过此代理
- 使用方式：环境变量 `HTTP_PROXY=http://127.0.0.1:10808` 和 `HTTPS_PROXY=http://127.0.0.1:10808`

## Build Commands

- Avalonia 项目构建：`cd ChildNotes && dotnet build ChildNotes\ChildNotes.csproj -v quiet --nologo`
- Web 项目构建：`cd web && npm run build`（如需要）

## Git Push 规则（重要）

- 本仓库 `remote.origin.push` 已配置为 `refs/heads/*:refs/for/*`（Gerrit 风格 review 推送），但远端是 GitHub（非 Gerrit），直接 `git push` 会被拒绝（non-fast-forward 到 `refs/for/master`）。
- **推送分支必须显式指定 refspec**，例如：
  - `git push origin master:refs/heads/master`
  - `git push origin dev:refs/heads/dev`
- 推送 tag 不受影响，可直接 `git push origin <tagname>` 或 `git push origin --tags`。
- 推送前必须先设置代理环境变量（见 Proxy Settings 段）。
- **禁止**使用 `git push --force` / `--force-with-lease` 推送到 master/main 分支，除非用户明确要求。
- 推送前如遇 non-fast-forward，优先用 `git pull --rebase` 整合远端改动，不要强制覆盖。

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
- 重大重构打 tag（如 `v0.2.0`），annotated tag，附简短说明。

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
