# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY HomeLedger.Core/HomeLedger.Core.csproj HomeLedger.Core/
COPY HomeLedger.Infrastructure/HomeLedger.Infrastructure.csproj HomeLedger.Infrastructure/
COPY HomeLedger.Web/HomeLedger.Web.csproj HomeLedger.Web/

RUN dotnet restore HomeLedger.Web/HomeLedger.Web.csproj

COPY . .
RUN dotnet publish HomeLedger.Web/HomeLedger.Web.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN mkdir -p /app/data

ENV ASPNETCORE_URLS=http://+:8080
ENV Database__ConnectionString=Data Source=/app/data/homeledger.db

COPY --from=build /app/publish .

EXPOSE 8080
VOLUME ["/app/data"]

ENTRYPOINT ["dotnet", "HomeLedger.Web.dll"]
