# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Ledger.Core/Ledger.Core.csproj Ledger.Core/
COPY Ledger.Infrastructure/Ledger.Infrastructure.csproj Ledger.Infrastructure/
COPY Ledger.Web/Ledger.Web.csproj Ledger.Web/

RUN dotnet restore Ledger.Web/Ledger.Web.csproj

COPY . .
RUN dotnet publish Ledger.Web/Ledger.Web.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN mkdir -p /app/data

ENV ASPNETCORE_URLS=http://+:8080
ENV Database__ConnectionString=Data Source=/app/data/ledger.db

COPY --from=build /app/publish .

EXPOSE 8080
VOLUME ["/app/data"]

ENTRYPOINT ["dotnet", "Ledger.Web.dll"]
