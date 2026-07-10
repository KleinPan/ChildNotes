# Tag 推送完整流程

> 本文件为低频详细流程文档，仅在**用户明确要求打 tag**时参阅。
> 主规则见 [project_rules.md](file:///e:/0_Code/5_Git/AiJi/.trae/rules/project_rules.md) 的"提交粒度与 Tag 策略"段：默认不打 tag。

## 版本号约定（SemVer + 0.x 阶段规则）

项目当前处于 0.x 阶段（未正式发布），版本号规则：

| 变更类型 | 版本号递增 | 示例 |
|----------|------------|------|
| 破坏性变更（API/DB schema 不兼容） | minor+1 | v0.3.8 → v0.4.0 |
| 新功能（向后兼容） | patch+1 | v0.4.0 → v0.4.1 |
| Bug 修复 / 小优化 | patch+1 | v0.4.0 → v0.4.1 |

**禁止**在 0.x 阶段直接跳到 v1.0.0 或更高主版本号。v1.0.0 保留给正式发布（first stable release）。

## 推送前检查

```powershell
# 1. 确认当前分支
git branch --show-current

# 2. 查看最近的 tag，判断下一个版本号
git tag --sort=-v:refname | Select-Object -First 5

# 3. 确认无代理环境变量覆盖 git 的 socks5 配置
Get-ChildItem Env: | Where-Object { $_.Name -match "PROXY|proxy" }
# 若有 HTTP_PROXY/HTTPS_PROXY 覆盖，先清除：
# Remove-Item Env:HTTP_PROXY, Env:HTTPS_PROXY, Env:ALL_PROXY -ErrorAction SilentlyContinue
```

## 创建 annotated tag

```powershell
# 1. 写 tag 信息到临时文件
# 2. 创建 annotated tag
git tag -a v0.4.0 -F .git\TAG_MSG_TMP.txt
# 3. 推送 tag
git push origin v0.4.0
# 4. 删除临时文件
```

**禁止**用轻量级 tag（`git tag v0.4.0` 不带 `-a`），必须用 annotated tag 附带说明。

## 完整推送顺序

```powershell
# 1. 暂存改动（按目录批量 add，禁止 git add . / -A）
git add ChildNotes.Backend/ChildNotes.Api/ ChildNotes.Backend/ChildNotes.Core/ ...

# 2. 提交（用 -F 文件方式）
git commit -F .git\COMMIT_MSG_TMP.txt

# 3. 推送分支
git push origin master

# 4. 打 tag（annotated）
git tag -a v0.4.0 -F .git\TAG_MSG_TMP.txt

# 5. 推送 tag
git push origin v0.4.0

# 6. 清理临时文件
```

## 删除误推的 tag

```powershell
# 删除远端 tag
git push origin :refs/tags/<tagname>
# 删除本地 tag
git tag -d <tagname>
```
