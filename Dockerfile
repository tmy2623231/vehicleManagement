# ── base：运行时镜像 ──
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 80

# ── build：SDK 编译发布 ──
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# 先复制项目文件并还原依赖，利用 Docker 层缓存加速后续构建
COPY ["vehicleManagement.csproj", "."]
RUN dotnet restore "vehicleManagement.csproj"

COPY . .
RUN dotnet publish "vehicleManagement.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# ── final：最终运行镜像 ──
FROM base AS final
WORKDIR /app

# 健康检查依赖 curl，仅运行镜像安装
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:80

# AI 配置回退值（部署时用 --build-arg 或 docker run -e 指向真实 Ollama 主机）
ARG OLLAMA_ENDPOINT=http://localhost:11434
ARG OLLAMA_MODEL=gemma4:e2b
ENV AiServer__OllamaEndpoint=${OLLAMA_ENDPOINT}
ENV AiServer__OllamaModel=${OLLAMA_MODEL}

# 持久化目录：数据库、日志、上传文件（通过 docker run -v 挂载）
VOLUME ["/app/data", "/app/logs", "/app/Folder"]

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl -f http://localhost/health || exit 1

ENTRYPOINT ["dotnet", "vehicleManagement.dll"]
