# Keincheck MCP 调试指南

## 前置条件

- 项目已配置 `Keincheck` NuGet 包
- `Program.cs` 中已添加 `.UseMcpServer()`（仅在 DEBUG 模式下启用）
- Trae IDE 的 MCP 连接已配置好（指向 `http://127.0.0.1:3001`）

## 标准调试流程（AI 主动模式）

> **新流程**：AI 完成代码修改后，提示用户启动 Visual Studio 调试；用户启动后通知 AI；AI 再连接 MCP 验证效果。
> AI **不自动启动程序**，也不等待程序就绪，避免浪费时间或因连接问题反复重试。

### 步骤 1: AI 完成代码修改

AI 执行：
```powershell
dotnet build ChildNotes\ChildNotes\ChildNotes.csproj -v quiet --nologo
```
构建成功后，**提示用户**：

> ✅ 代码已修改并构建成功。请启动 Visual Studio 调试（F5），启动完成后告诉我"好了"，我将通过 MCP 连接验证界面效果。

### 步骤 2: 用户启动 Visual Studio 调试

用户操作：
1. 在 Visual Studio 中按 **F5** 启动调试
2. 等待程序窗口显示
3. 回复 AI：**"好了"** 或 **"已启动"**

### 步骤 3: AI 连接 MCP 验证

收到用户确认后，AI 执行：
```javascript
// 1. 列出窗口
mcp_keincheck.list_windows({})

// 2. 截图查看效果
mcp_keincheck.screenshot_window({"target": "ctl-1", "scale": 1})

// 3. 如需进一步检查控件属性/布局
mcp_keincheck.query_controls({ selector: 'Border.vf-card' })
mcp_keincheck.get_visual_tree({ handle: "ctl-1", maxDepth: 6, visibleOnly: true })
```

## 备选方案：命令行启动（如需）

如果用户不想用 Visual Studio，可手动启动：

```powershell
# 关闭旧进程
Stop-Process -Name "ChildNotes.Desktop" -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

# 启动程序（必须用 Start-Process）
Start-Process "E:\0_Code\5_Git\AiJi\ChildNotes\ChildNotes.Desktop\bin\Debug\net10.0\ChildNotes.Desktop.exe"
```

**注意**：
- PowerShell 不支持 `&&`，命令分隔用 `;`
- 必须使用 `Start-Process`，不能用 `dotnet run`（会占用终端导致程序被中断）

## 常见问题排查

### MCP 连接失败 (`list tools failed`)

- 检查程序是否正在运行：`Get-Process -Name "ChildNotes.Desktop"`
- 检查端口是否监听：`Get-NetTCPConnection -LocalPort 3001 -State Listen`
- 如果端口未监听，重新启动程序

### 程序启动后立即退出

- **不要**在同一终端执行其他命令，这会发送 CTRL+C 给子进程
- 必须使用 `Start-Process` 启动程序
- 如果用 `dotnet run`，程序会在终端执行新命令时退出（退出码 -1073741510）

### visual tree 返回空节点

- 这是正常现象，某些 styled control 的 visual tree 可能不完整
- 改用 `get_properties` + `query_controls` 组合来分析布局
- 或使用 `screenshot_control` / `screenshot_marked` 来可视化查看

## 快速导航到疫苗记录页面

```javascript
// 1. 列出窗口获取 ctl-1
list_windows()

// 2. 查找"补记"按钮 (Content="补记")
query_controls({ selector: 'Button[Content=补记]' })

// 3. 点击按钮中心点（根据 globalBounds 计算）
click_at({ handle: 'ctl-1', x: centerX, y: centerY })

// 4. 等待页面加载
wait_for_idle()
```

## Keincheck 常用工具速查

| 工具 | 用途 | 关键参数 |
|------|------|----------|
| `list_windows` | 列出所有窗口 | 无 |
| `screenshot_window` | 窗口截图 | target, scale |
| `screenshot_marked` | 标注截图（带编号框） | target, maxMarks |
| `screenshot_control` | 单个控件截图 | target, scale |
| `query_controls` | CSS 选择器查找控件 | selector, scopeHandle |
| `get_properties` | 获取控件详细属性 | handle |
| `get_visual_tree` | 可视化树（含 bounds） | handle, maxDepth, visibleOnly |
| `click_at` | 模拟点击 | x, y, handle |
| `hit_test` | 点位命中测试 | x, y, handle |
| `wait_for_idle` | 等待布局稳定 | 无 |
| `describe_screen` | AI 描述屏幕内容 | 返回图片+描述 |

## 注意事项

- 程序在前台运行显示 UI，MCP 在后台通过 HTTP 访问，互不影响
- Android 平台无法在本地编译，CI 通过 GitHub Actions 构建
- Windows 端用于开发调试，可以集成各种调试开关
- 调试专用代码应通过 `#if DEBUG` 或平台条件隔离

## 参考项目（小程序源码）

本项目 Avalonia 版本参考自微信小程序版本，样式对齐时以小程序为准：

| 项目 | 说明 | 地址 |
|------|------|------|
| **前端 (小程序)** | 微信小程序前端源码（含 wxss 样式） | https://github.com/yczZz/child-notes-front-z |
| **后端** | 后端 API 源码 | https://github.com/yczZz/child-notes-backend-z |
| **本地参考副本** | 小程序源码本地备份（样式对照用） | `E:\0_Code\5_Git\AiJi参考\child-notes-front-z-master` |

### 样式对照关键文件

- **时间轴疫苗卡片**: `components/vaccine-form/index.wxss` → `.vf-time-dose` 系列
- **疫苗卡片网格**: `components/vaccine-form/index.wxss` → `.dose-chip` 系列
- **注意**: 时间轴视图用 `.vf-time-dose`，卡片网格视图用 `.dose-chip`，两者基础样式不同！
