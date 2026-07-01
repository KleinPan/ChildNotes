# 多阶段构建：SDK 阶段编译 → Runtime 阶段运行
# 构建上下文为仓库根目录，以便引用 ChildNotes.Shared
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 先复制所有 csproj，利用 Docker 缓存层加速依赖还原
COPY ChildNotes.Shared/ChildNotes.Shared.csproj ./ChildNotes.Shared/
COPY ChildNotes.Backend/ChildNotes.Api/ChildNotes.Api.csproj ./ChildNotes.Backend/ChildNotes.Api/
COPY ChildNotes.Backend/ChildNotes.Core/ChildNotes.Core.csproj ./ChildNotes.Backend/ChildNotes.Core/
COPY ChildNotes.Backend/ChildNotes.Infrastructure/ChildNotes.Infrastructure.csproj ./ChildNotes.Backend/ChildNotes.Infrastructure/
RUN dotnet restore ChildNotes.Backend/ChildNotes.Api/ChildNotes.Api.csproj

# 复制源码并发布（只需后端 + Shared 的源码）
COPY ChildNotes.Shared/ ./ChildNotes.Shared/
COPY ChildNotes.Backend/ ./ChildNotes.Backend/
RUN dotnet publish ChildNotes.Backend/ChildNotes.Api/ChildNotes.Api.csproj \
    -c Release -o /app/publish --nologo -v q

# 运行时镜像（alpine 更小）
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=build /app/publish .

# 上传文件持久化目录
RUN mkdir -p /app/uploads

EXPOSE 8080
ENTRYPOINT ["dotnet", "ChildNotes.Api.dll"]
