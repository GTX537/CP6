# ===== .NET Backend Multi-Stage Dockerfile =====
#
# 面试要点：
# 1. 多阶段构建（Multi-Stage Build）：编译用 SDK 镜像，运行用 Runtime 镜像
#    - SDK 镜像 ~800MB，Runtime 镜像 ~100MB，最终镜像小很多
# 2. 分层缓存：先复制 csproj 还原依赖，再复制源码编译
#    - 依赖不变时，Docker 复用缓存层，构建更快
# 3. EXPOSE 声明端口，实际映射在 docker-compose 中配置

# ===== Stage 1: Build =====
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
ARG RELEASE_VERSION=0.0.0-dev
ARG GIT_SHA=unknown

# 先复制项目文件，还原依赖（利用 Docker 层缓存）
COPY CP6.Entity/CP6.Entity.csproj CP6.Entity/
COPY CP6.Core/CP6.Core.csproj CP6.Core/
COPY CP6.Space.Contracts/CP6.Space.Contracts.csproj CP6.Space.Contracts/
COPY CP6.Space.Domain/CP6.Space.Domain.csproj CP6.Space.Domain/
COPY CP6.Space.Application/CP6.Space.Application.csproj CP6.Space.Application/
COPY CP6.Space.Infrastructure/CP6.Space.Infrastructure.csproj CP6.Space.Infrastructure/
COPY CP6.WebApi/CP6.WebApi.csproj CP6.WebApi/
RUN dotnet restore CP6.WebApi/CP6.WebApi.csproj

# 再复制全部源码，编译发布
COPY . .
RUN dotnet publish CP6.WebApi/CP6.WebApi.csproj -c Release -o /app/publish --no-restore \
    -p:Version=${RELEASE_VERSION} \
    -p:InformationalVersion=${RELEASE_VERSION}+${GIT_SHA}

# ===== Stage 2: Runtime =====
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# 从构建阶段复制编译产物
COPY --from=build /app/publish .

# ASP.NET Core 默认监听 8080，我们改为 5000
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

ENTRYPOINT ["dotnet", "CP6.WebApi.dll"]
