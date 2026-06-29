# Project Rules

## Proxy Settings

- HTTP/HTTPS 代理地址：`127.0.0.1:10808`
- 访问 GitHub 等外网资源时需通过此代理
- 使用方式：环境变量 `HTTP_PROXY=http://127.0.0.1:10808` 和 `HTTPS_PROXY=http://127.0.0.1:10808`

## Build Commands

- Avalonia 项目构建：`cd ChildNotes && dotnet build ChildNotes\ChildNotes.csproj -v quiet --nologo`
- Web 项目构建：`cd web && npm run build`（如需要）
