# ─────────────────────────────────────────────────────────────────
# Stage 1: Build
# ─────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project file and restore (layer caching)
COPY ["LoxoneSolarForecast.csproj", "."]
RUN dotnet restore "LoxoneSolarForecast.csproj" -r linux-x64

# Copy source and build
COPY . .
RUN dotnet publish "LoxoneSolarForecast.csproj" \
    -c Release \
    -o /app/publish \
    -r linux-x64 \
    --no-restore \
    --self-contained false \
    /p:UseAppHost=false

# ─────────────────────────────────────────────────────────────────
# Stage 2: Runtime
# ─────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install curl for healthcheck
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Create config directory
RUN mkdir -p /config && chmod 777 /config

# Copy published app
COPY --from=build /app/publish .

# Non-root user for security
RUN adduser --disabled-password --gecos "" appuser \
    && chown -R appuser:appuser /app /config
USER appuser

# Port
EXPOSE 5000

# Environment
ENV ASPNETCORE_URLS=http://0.0.0.0:5000
ENV ASPNETCORE_ENVIRONMENT=Production
ENV Storage__ConfigPath=/config

# Healthcheck
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:5000/health || exit 1

ENTRYPOINT ["dotnet", "LoxoneSolarForecast.dll"]
