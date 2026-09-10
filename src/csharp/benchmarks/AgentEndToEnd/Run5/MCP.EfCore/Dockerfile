FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/csharp/benchmarks/AgentEndToEnd/Run5/MCP.EfCore/MCP.EfCore.csproj
RUN dotnet publish src/csharp/benchmarks/AgentEndToEnd/Run5/MCP.EfCore/MCP.EfCore.csproj -c Release -o /app/publish --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:9.0
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MCP.EfCore.dll"]
