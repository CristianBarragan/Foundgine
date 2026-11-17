# Compose build context: repository root (so Directory.Build.props is included)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/csharp/benchmarks/SupplyChain.Performance/Foundgine.SupplyChain.BenchmarkApi/Foundgine.SupplyChain.BenchmarkApi.csproj
RUN dotnet publish src/csharp/benchmarks/SupplyChain.Performance/Foundgine.SupplyChain.BenchmarkApi/Foundgine.SupplyChain.BenchmarkApi.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Foundgine.SupplyChain.BenchmarkApi.dll"]
