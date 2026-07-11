# ChildNotes 前端

本目录包含 ChildNotes 的 Avalonia 前端项目，覆盖共享 UI/业务逻辑以及 Desktop、Android、iOS 平台入口。

## 项目结构

```text
ChildNotes/
├── ChildNotes.slnx              # 前端解决方案入口
├── Directory.Packages.props     # 前端 NuGet 中央版本管理
├── ChildNotes/                  # 共享 Avalonia 应用、ViewModels、Services、Controls、Assets
├── ChildNotes.Desktop/          # 桌面平台入口
├── ChildNotes.Android/          # Android 平台入口
├── ChildNotes.iOS/              # iOS 平台入口
├── ChildNotes.Tests/            # 前端测试
└── ChildNotes.UiDesignCheck/    # UI 设计检查工具
```

## 常用命令

```bash
# 还原前端依赖
dotnet restore ChildNotes/ChildNotes.slnx

# 构建前端解决方案
dotnet build ChildNotes/ChildNotes.slnx

# 运行桌面端
dotnet run --project ChildNotes/ChildNotes.Desktop/ChildNotes.Desktop.csproj

# 运行前端测试
dotnet test ChildNotes/ChildNotes.Tests/ChildNotes.Tests.csproj
```

## 开发边界

- 跨平台共享页面、控件、ViewModel、Service 优先放在 `ChildNotes/ChildNotes/`。
- 平台特定启动逻辑和平台能力适配放在对应平台项目中。
- 与后端共享的 DTO、常量和同步协议放在仓库根级 `ChildNotes.Shared/`。
- 前端专项复盘文档可放在 `ChildNotes/ChildNotes/docs/`，并同步更新 `../docs/README.md`。
