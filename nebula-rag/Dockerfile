FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ARG NEBULARAG_REPO_URL=https://github.com/MarkBovee/NebulaRAG.git
ARG NEBULARAG_REPO_REF=main

RUN apt-get update \
    && apt-get install -y --no-install-recommends git ca-certificates \
    && rm -rf /var/lib/apt/lists/*

RUN git clone --depth 1 --branch "${NEBULARAG_REPO_REF}" "${NEBULARAG_REPO_URL}" /src/NebulaRAG
WORKDIR /src/NebulaRAG

RUN dotnet restore "src/NebulaRAG.AddonHost/NebulaRAG.AddonHost.csproj"
RUN dotnet publish "src/NebulaRAG.AddonHost/NebulaRAG.AddonHost.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends jq libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./
COPY --from=build /src/NebulaRAG/src/NebulaRAG.Cli/ragsettings.json /app/ragsettings.json
COPY run.sh /run.sh
RUN chmod +x /run.sh

CMD ["/run.sh"]
