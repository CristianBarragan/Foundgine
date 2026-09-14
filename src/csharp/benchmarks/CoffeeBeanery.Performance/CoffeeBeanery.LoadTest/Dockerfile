FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /src

COPY . .

# Restore and publish in the same step. This avoids using stale/incomplete
# assets from a previous restore and lets MSBuild resolve all project references
# from the repository-root build context.
RUN dotnet publish     src/csharp/benchmarks/CoffeeBeanery.Performance/CoffeeBeanery.LoadTest/CoffeeBeanery.LoadTest.csproj     -c Release     -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:9.0

WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "CoffeeBeanery.LoadTest.dll"]
