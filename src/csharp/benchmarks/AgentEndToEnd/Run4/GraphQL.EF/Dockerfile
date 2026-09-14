FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN test -f src/csharp/benchmarks/CoffeeBeanery.Performance/CoffeeBeanery.Database/CoffeeBeanery.Database.csproj
RUN dotnet restore src/csharp/benchmarks/AgentEndToEnd/Run4/GraphQL.EF/GraphQL.EF.csproj
RUN dotnet publish src/csharp/benchmarks/AgentEndToEnd/Run4/GraphQL.EF/GraphQL.EF.csproj -c Release -o /app/publish --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:9.0

# The compose healthcheck for this service runs `curl` inside the container
# (see docker-compose.yml). The aspnet runtime image is a slim Debian image
# that does not include curl by default, so every healthcheck probe fails
# with "executable file not found in $PATH" (exit 127) even though the app
# itself is up and answering fine - Docker reports the container as
# unhealthy regardless. Install curl here so the in-container healthcheck
# can actually reach the app.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "GraphQL.EF.dll"]
