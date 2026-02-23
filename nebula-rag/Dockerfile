FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ARG NEBULARAG_REPO_URL=https://github.com/MarkBovee/NebulaRAG.git
ARG NEBULARAG_REPO_REF=main

RUN apt-get update \
    && apt-get install -y --no-install-recommends git ca-certificates \
    && rm -rf /var/lib/apt/lists/*

RUN git clone --depth 1 --branch "${NEBULARAG_REPO_REF}" "${NEBULARAG_REPO_URL}" /src/NebulaRAG
WORKDIR /src/NebulaRAG

RUN dotnet restore "src/NebulaRAG.Cli/NebulaRAG.Cli.csproj"
RUN dotnet publish "src/NebulaRAG.Cli/NebulaRAG.Cli.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends jq \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./
COPY run.sh /run.sh
RUN chmod +x /run.sh

CMD ["/run.sh"]
