FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore in a layer keyed only on the project + central build files, so a source-only change does
# not invalidate the (slow) restore cache (COR-11).
COPY DndMcpAICsharpFun.csproj .
COPY Directory.Build.props Directory.Build.targets Directory.Packages.props ./
COPY Build/ Build/
RUN dotnet restore DndMcpAICsharpFun.csproj

COPY . .
RUN dotnet publish DndMcpAICsharpFun.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is used by the container HEALTHCHECK below.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /books /data

COPY --from=build /app/publish .

# Run unprivileged (COR-12): the aspnet image ships a non-root 'app' user. Give it ownership of the
# writable paths so ingestion and model caches can be written.
RUN chown -R app:app /app /books /data
USER app

EXPOSE 5101

# Probe the app's readiness endpoint rather than a bare TCP connect (COR-12).
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=5 \
    CMD curl -fsS http://localhost:5101/health || exit 1

ENTRYPOINT ["dotnet", "DndMcpAICsharpFun.dll"]
