FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/csharp/benchmarks/SupplyChain.Performance/HotChocolate.SupplyChain.BenchmarkApi/HotChocolate.SupplyChain.BenchmarkApi.csproj
RUN dotnet publish src/csharp/benchmarks/SupplyChain.Performance/HotChocolate.SupplyChain.BenchmarkApi/HotChocolate.SupplyChain.BenchmarkApi.csproj -c Release -o /app/publish --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:4410
EXPOSE 4410
ENTRYPOINT ["dotnet", "HotChocolate.SupplyChain.BenchmarkApi.dll"]
