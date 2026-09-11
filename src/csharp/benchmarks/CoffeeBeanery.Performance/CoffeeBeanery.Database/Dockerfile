FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /src

COPY . .

RUN dotnet restore \
    src/csharp/benchmarks/CoffeeBeanery.Performance/CoffeeBeanery.Database/CoffeeBeanery.Database.csproj

RUN dotnet publish \
    src/csharp/benchmarks/CoffeeBeanery.Performance/CoffeeBeanery.Database/CoffeeBeanery.Database.csproj \
    -c Release \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0

WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "CoffeeBeanery.Database.dll"]