FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY GeoInsight.slnx ./
COPY src/GeoInsight.Domain/GeoInsight.Domain.csproj src/GeoInsight.Domain/
COPY src/GeoInsight.Application/GeoInsight.Application.csproj src/GeoInsight.Application/
COPY src/GeoInsight.Infrastructure/GeoInsight.Infrastructure.csproj src/GeoInsight.Infrastructure/
COPY src/GeoInsight.Api/GeoInsight.Api.csproj src/GeoInsight.Api/
COPY src/GeoInsight.Worker/GeoInsight.Worker.csproj src/GeoInsight.Worker/
COPY tests/GeoInsight.Tests/GeoInsight.Tests.csproj tests/GeoInsight.Tests/
RUN dotnet restore src/GeoInsight.Worker/GeoInsight.Worker.csproj

COPY . .
RUN dotnet publish src/GeoInsight.Worker/GeoInsight.Worker.csproj -c Release -o /app --no-restore

# No ASP.NET Core needed — the worker has no HTTP pipeline, just AddHangfireServer().
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENTRYPOINT ["dotnet", "GeoInsight.Worker.dll"]
