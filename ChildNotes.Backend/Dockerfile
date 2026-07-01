# 多阶段构建：SDK 阶段编译 → Runtime 阶段运行
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 先复制 csproj/nuget.config，利用 Docker 缓存层加速依赖还原
COPY ChildNotes.Api/ChildNotes.Api.csproj ./ChildNotes.Api/
COPY ChildNotes.Core/ChildNotes.Core.csproj ./ChildNotes.Core/
COPY ChildNotes.Infrastructure/ChildNotes.Infrastructure.csproj ./ChildNotes.Infrastructure/
RUN dotnet restore ChildNotes.Api/ChildNotes.Api.csproj

# 复制源码并发布
COPY . .
RUN dotnet publish ChildNotes.Api/ChildNotes.Api.csproj \
    -c Release -o /app/publish --nologo -v q

# 运行时镜像（alpine 更小）
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=build /app/publish .

# 上传文件持久化目录
RUN mkdir -p /app/uploads

EXPOSE 8080
ENTRYPOINT ["dotnet", "ChildNotes.Api.dll"]
