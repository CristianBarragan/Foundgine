FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/csharp/benchmarks/AgentEndToEnd/Run5/MCP.Foundgine/MCP.Foundgine.csproj -c Release -o /app/publish
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet","MCP.Foundgine.dll"]
