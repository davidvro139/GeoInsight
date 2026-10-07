FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY GeoInsight.slnx ./
COPY src/GeoInsight.Domain/GeoInsight.Domain.csproj src/GeoInsight.Domain/
COPY src/GeoInsight.Application/GeoInsight.Application.csproj src/GeoInsight.Application/
COPY src/GeoInsight.Infrastructure/GeoInsight.Infrastructure.csproj src/GeoInsight.Infrastructure/
COPY src/GeoInsight.Api/GeoInsight.Api.csproj src/GeoInsight.Api/
COPY tests/GeoInsight.Tests/GeoInsight.Tests.csproj tests/GeoInsight.Tests/
RUN dotnet restore src/GeoInsight.Api/GeoInsight.Api.csproj

COPY . .
RUN dotnet publish src/GeoInsight.Api/GeoInsight.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "GeoInsight.Api.dll"]
