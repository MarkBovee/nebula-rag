FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    NUGET_XMLDOC_MODE=skip

ARG NEBULARAG_REPO_URL=https://github.com/MarkBovee/NebulaRAG.git
ARG NEBULARAG_REPO_REF=main
ARG BUILD_VERSION=dev

RUN apt-get update \
    && apt-get install -y --no-install-recommends git ca-certificates nodejs npm \
    && rm -rf /var/lib/apt/lists/*

RUN echo "nebula-rag build version: ${BUILD_VERSION}" \
    && git clone --depth 1 --branch "${NEBULARAG_REPO_REF}" "${NEBULARAG_REPO_URL}" /src/NebulaRAG \
    && git -C /src/NebulaRAG rev-parse HEAD > /tmp/nebularag_commit.txt
WORKDIR /src/NebulaRAG

RUN test -f "src/NebulaRAG.AddonHost/NebulaRAG.AddonHost.csproj"
RUN test -f "dashboard/package.json"

# Build the dashboard so static assets are included in AddonHost publish output.
RUN cd dashboard \
    && npm install --no-audit --no-fund \
    && npm run build

# Home Assistant builders can be resource-constrained; disable parallel restore and retry transient failures.
RUN set -eux; \
        for attempt in 1 2 3; do \
            dotnet restore "src/NebulaRAG.AddonHost/NebulaRAG.AddonHost.csproj" --disable-parallel --verbosity minimal && break; \
            echo "dotnet restore failed (attempt ${attempt}/3), retrying..."; \
            sleep $((attempt * 5)); \
            if [ "${attempt}" -eq 3 ]; then exit 1; fi; \
        done

RUN dotnet publish "src/NebulaRAG.AddonHost/NebulaRAG.AddonHost.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends jq libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./
COPY --from=build /src/NebulaRAG/src/NebulaRAG.Cli/ragsettings.json /app/ragsettings.json
COPY --from=build /tmp/nebularag_commit.txt /app/nebularag_commit.txt
COPY run.sh /run.sh
RUN chmod +x /run.sh

CMD ["/run.sh"]
