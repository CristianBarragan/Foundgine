FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/csharp/benchmarks/AgentEndToEnd/Fixtures/SupplyChain.Layered/Api/Foundgine.SupplyChain.Api.csproj
RUN dotnet publish src/csharp/benchmarks/AgentEndToEnd/Fixtures/SupplyChain.Layered/Api/Foundgine.SupplyChain.Api.csproj -c Release -o /app/publish /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet","Foundgine.SupplyChain.Api.dll"]
