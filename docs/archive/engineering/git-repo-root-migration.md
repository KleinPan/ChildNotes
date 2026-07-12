# Git 仓库根目录迁移操作记录

> 执行日期：2026-06-29
> 目的：将 Git 仓库根目录从 `ChildNotes/` 上移到 `AiJi/`，使 `ChildNotes/`（桌面端）与 `ChildNotes.Backend/`（后端）成为同一仓库根下的并列子目录，并修复 GitHub Actions 工作流无法触发的问题。

---

## 一、背景与问题诊断

### 1.1 现象
- GitHub Actions 工作流（`.github/workflows/release.yml`）已编写，但推送 tag 后并未触发 Release 构建。

### 1.2 根因
- 仓库的 `.git` 目录位于 `E:\0_Code\5_Git\AiJi\ChildNotes\.git`，即 Git 仓库根目录是 `ChildNotes/`。
- 而 `.github/workflows/release.yml` 物理上位于 `E:\0_Code\5_Git\AiJi\.github\workflows\release.yml`，处于仓库根目录的**上一级**，Git 完全无法感知，自然也不会被推送到远程、更不会被 GitHub Actions 识别。
- 同时 `ChildNotes.Backend/`（后端 API）也位于 `AiJi/` 下，同样未被纳入版本控制。

### 1.3 期望目录结构
```
E:\0_Code\5_Git\AiJi\            ← 新的 Git 仓库根
├── .git\                         ← .git 上移到此
├── .github\workflows\release.yml ← 工作流（关键，必须在仓库根下）
├── .gitignore                    ← 根级 .gitignore（新增）
├── global.json                   ← .NET SDK 版本锁定
├── ChildNotes\                   ← 桌面端（原仓库根内容，现为子目录）
│   ├── .gitignore                ← 桌面端项目级 .gitignore（保留）
│   ├── ChildNotes.slnx
│   ├── ChildNotes\               ← Avalonia 应用主项目
│   ├── ChildNotes.Desktop\
│   ├── ChildNotes.Android\
│   ├── ChildNotes.iOS\
│   ├── ChildNotes.Tests\
│   └── ChildNotes.UiDesignCheck\
├── ChildNotes.Backend\           ← 后端 API（新纳入版本控制）
│   ├── ChildNotes.Backend.slnx
│   ├── ChildNotes.Api\
│   ├── ChildNotes.Core\
│   ├── ChildNotes.Infrastructure\
│   └── ChildNotes.Tests\
├── Docs\                         ← 设计与迁移文档
└── .trae\rules\project_rules.md  ← IDE 规则
```

---

## 二、迁移方案选择

| 方案 | 说明 | 历史保留 | 文件路径重写 | 风险 |
| ---- | ---- | -------- | ------------ | ---- |
| **A. git filter-repo 重写历史（采用）** | 使用 `git filter-repo --to-subdirectory-filter` 将原仓库所有路径前置 `ChildNotes/` 前缀，再把 `.git` 上移 | ✅ 保留全部提交/分支/tag | ✅ 历史提交路径统一变为 `ChildNotes/...` | 中（需强推远程） |
| B. 直接移动 .git + 重新提交 | 仅把 `.git` 上移，新增提交把后端加入仓库 | ✅ 保留旧历史（路径不变） | ❌ 历史提交路径仍是顶层 | 低 |
| C. 新建仓库重新初始化 | 删除 `.git`，重新 `git init` | ❌ 丢弃全部历史 | — | 高 |

**选定方案 A**：完整保留历史记录与分支/tag 结构，并使历史提交的文件路径与新结构一致（历史中 `ChildNotes.slnx` 会变为 `ChildNotes/ChildNotes.slnx`），避免后续 checkout 旧 tag 时路径错乱。

---

## 三、前置准备

### 3.1 环境要求
- Git for Windows（本机版本 2.50.1.windows.1）
- Python 3 + pip（用于安装 `git-filter-repo`）
- HTTP/HTTPS 代理 `127.0.0.1:10808`（用于访问 GitHub）

### 3.2 安装 git-filter-repo
```powershell
pip install git-filter-repo
# 验证
git filter-repo --version
```

### 3.3 完整备份
在动手前，对整个 `ChildNotes/` 目录（含 `.git`）做一次物理拷贝备份：
```powershell
$backup = "E:\0_Code\5_Git\_backup_AiJi_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Copy-Item -Recurse "E:\0_Code\5_Git\AiJi\ChildNotes" "$backup\ChildNotes" -Force
```
> 备份已验证：`.git`、28 条提交记录、tag `v1.0.0` 均完好。
> 若 `.vs\` 被进程占用导致个别文件跳过，可忽略（`.vs` 已被 gitignore，不属于版本库）。

---

## 四、迁移执行步骤

### 4.1 第 1 步：用 git filter-repo 重写历史路径
在**原仓库目录** `E:\0_Code\5_Git\AiJi\ChildNotes\` 中执行：
```powershell
cd E:\0_Code\5_Git\AiJi\ChildNotes
git filter-repo --to-subdirectory-filter ChildNotes --force
```
**作用**：把仓库中所有历史提交里的文件路径前面统一加上 `ChildNotes/` 前缀。
- 例如历史中 `ChildNotes.slnx` → `ChildNotes/ChildNotes.slnx`
- 全部 28 条提交、`master` 分支、`v1.0.0` tag 均保留，但 commit hash 会改变（因为内容路径变了）。
- `git filter-repo` 出于安全考虑会**移除 origin 远程**，后续需手动加回。

### 4.2 第 2 步：把 .git 上移到 AiJi 根目录
```powershell
cd E:\0_Code\5_Git\AiJi
Move-Item "ChildNotes\.git" ".git" -Force
```
此时 `AiJi\` 成为新的仓库根，工作区中 `ChildNotes\` 子目录下的文件恰好对应重写后的索引路径 `ChildNotes/...`。

### 4.3 第 3 步：重新添加 origin 远程
`git filter-repo` 会移除远程，需手动恢复：
```powershell
cd E:\0_Code\5_Git\AiJi
git remote add origin https://github.com/KleinPan/ChildNotes.git
```

### 4.4 第 4 步：恢复工作区与索引一致性
`filter-repo` 在 `.git` 仍在 `ChildNotes/` 时已重写索引，移动 `.git` 后可能出现"已删除"的假象。执行一次硬重置使工作区对齐索引：
```powershell
git reset --hard HEAD
```
> 如有重复嵌套的 `ChildNotes/ChildNotes/` 残留目录（filter-repo 物理移动文件时产生），可手动删除——**但删除前务必用 `git ls-files` 确认该路径未被跟踪**。本次操作中误删过一次，已通过 `git checkout -- .` 从对象库恢复。教训：清理前先核对 `git ls-files`。

### 4.5 第 5 步：新增根级 .gitignore
在 `E:\0_Code\5_Git\AiJi\.gitignore` 新建根级忽略规则，覆盖：
- IDE 缓存：`.vs/`、`.idea/`、`*.user`
- .NET 构建产物：`**/[Bb]in/`、`**/[Oo]bj/`、`artifacts/`、`*.log`
- 后端运行时内容：`ChildNotes.Backend/ChildNotes.Api/uploads/`
- 本地密钥覆盖：`appsettings.*.Local.json`、`secrets.json`

> 桌面端原 `ChildNotes/.gitignore` 保留不动，继续负责桌面端项目级忽略。

### 4.6 第 6 步：暂存并提交新增内容
```powershell
cd E:\0_Code\5_Git\AiJi
git add -A
# 用文件提交，避免 PowerShell heredoc 问题
git commit -F .git/COMMIT_MSG.txt
```
本次提交新增 112 个文件，包括：
- `.github/workflows/release.yml`（关键：修复 Actions 无法触发）
- `.gitignore`（根级）
- `global.json`（锁定 .NET SDK 10.0.301）
- `ChildNotes.Backend/`（后端全部源码：Api/Core/Infrastructure/Tests）
- `Docs/`（设计与迁移文档）
- `.trae/rules/project_rules.md`

### 4.7 第 7 步：强推到远程
历史已被重写，必须强推。先 fetch 刷新远程引用：
```powershell
$env:HTTP_PROXY="http://127.0.0.1:10808"
$env:HTTPS_PROXY="http://127.0.0.1:10808"
git fetch origin
# 显式指定 refspec，避免被旧的 Gerrit 风格 push refspec（refs/for/*）劫持
git push --force origin master:refs/heads/master
git push --force origin v1.0.0
```
> ⚠️ 本仓库 `remote.origin.push` 原本配置了 `refs/heads/*:refs/for/*`（Gerrit 代码审查流），导致 `git push origin master` 实际推到 `refs/for/master` 而非 `refs/heads/master`，表现为 "stale info / non-fast-forward"。处理方式：用显式 `master:refs/heads/master` 绕过，并随后 `git config --unset-all remote.origin.push` 清理该配置。

---

## 五、验证

### 5.1 本地验证
```powershell
cd E:\0_Code\5_Git\AiJi
git rev-parse --show-toplevel      # 应为 E:/0_Code/5_Git/AiJi
git log --oneline -5               # 29 条提交，最新为迁移 commit
git tag                            # v1.0.0 存在
git ls-files | Measure-Object       # 321 个文件
git status                         # clean，无未提交改动
```

### 5.2 远程验证
```powershell
git fetch origin
git rev-parse master               # 7d40bad...
git rev-parse origin/master        # 7d40bad...（与本地一致）
git ls-remote --tags origin        # refs/tags/v1.0.0 存在
```

### 5.3 工作流路径校验
`release.yml` 中两处 `working-directory`：
- `ChildNotes.Backend` → 解析为 `AiJi/ChildNotes.Backend/`，存在 `ChildNotes.Api/ChildNotes.Api.csproj` ✓
- `ChildNotes` → 解析为 `AiJi/ChildNotes/`，存在 `ChildNotes.Desktop/ChildNotes.Desktop.csproj` ✓
- `global.json` 位于仓库根，`actions/setup-dotnet` 与 `dotnet` CLI 会自动识别 ✓

### 5.4 GitHub Actions 触发验证
- **普通 push 到 master 不会触发 release.yml**：该工作流的 `on:` 仅监听 `tags: v*` 与 `release-*` 以及 `workflow_dispatch`。这是设计如此，Release 仅在发版 tag 时构建。
- **触发 Release 工作流的正确方式**：
  ```powershell
  git tag v1.0.1
  git push origin v1.0.1
  ```
  随后到 GitHub 仓库 **Actions** 页面查看 "Release Build" 运行情况。
- **手动触发**：在 Actions 页面选择 "Release Build" → "Run workflow"，可选填 `ref` 输入。

### 5.5 仓库根在 GitHub 上的目录结构
迁移后 GitHub 仓库根应能看到：`.github/`、`.gitignore`、`global.json`、`ChildNotes/`、`ChildNotes.Backend/`、`Docs/`、`.trae/`。

---

## 六、回滚方案

若迁移出现问题需回滚：
1. 停止任何推送（如已强推，远程历史已被覆盖，但旧提交对象在 GitHub 仍可短时间通过 hash 访问）。
2. 删除 `AiJi\.git`。
3. 从备份恢复：将 `E:\0_Code\5_Git\_backup_AiJi_<时间戳>\ChildNotes\` 完整拷回 `E:\0_Code\5_Git\AiJi\ChildNotes\`。
4. 在 `ChildNotes\` 目录重新 `git remote add origin https://github.com/KleinPan/ChildNotes.git`。
5. 若远程已被强推覆盖，需联系 GitHub 支持 or 用备份的 `master`/`v1.0.0` 重新强推回去。

---

## 七、后续注意事项

1. **clone 地址不变**：仍是 `https://github.com/KleinPan/ChildNotes.git`，但 clone 后根目录直接是 `ChildNotes/` 与 `ChildNotes.Backend/` 并列。
2. **本地构建命令更新**：
   - 桌面端：`cd ChildNotes && dotnet build ChildNotes\ChildNotes.csproj -v quiet --nologo`
   - 后端：`cd ChildNotes.Backend && dotnet build ChildNotes.Api\ChildNotes.Api.csproj`
3. **CI 构建矩阵**：`release.yml` 的 4 个矩阵项（windows/macos/linux/backend）在工作流文件路径修复后会正常触发，无需改 workflow。
4. **tag 策略**：建议保持 `v*` 前缀触发 Release。日常开发推送 master 不会触发构建，避免浪费 CI 额度。
5. **敏感配置**：`appsettings.json` 仅含占位符，可入库；真实数据库密码、JWT Secret、DeepSeek ApiKey 等请通过部署环境变量或 `appsettings.Production.json`（已 gitignore）注入。
6. **备份保留期**：建议保留 `E:\0_Code\5_Git\_backup_AiJi_*` 至少 2 周，确认远程与 CI 一切正常后再清理。
