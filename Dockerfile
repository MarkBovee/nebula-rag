FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["NebulaRAG.sln", "./"]
COPY ["src/NebulaRAG.Core/NebulaRAG.Core.csproj", "src/NebulaRAG.Core/"]
COPY ["src/NebulaRAG.Mcp/NebulaRAG.Mcp.csproj", "src/NebulaRAG.Mcp/"]
RUN dotnet restore "src/NebulaRAG.Mcp/NebulaRAG.Mcp.csproj"

COPY src/ src/
RUN dotnet publish "src/NebulaRAG.Mcp/NebulaRAG.Mcp.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
	&& apt-get install -y --no-install-recommends libgssapi-krb5-2 \
	&& rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./
COPY container/ragsettings.container.json /app/ragsettings.json
COPY container/entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/entrypoint.sh

ENTRYPOINT ["/app/entrypoint.sh"]
