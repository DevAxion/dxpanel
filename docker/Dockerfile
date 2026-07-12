# syntax=docker/dockerfile:1

# ---------- build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore first for layer caching
COPY SRXPanel.csproj ./
RUN dotnet restore SRXPanel.csproj

# Copy the rest and publish
COPY . ./
RUN dotnet publish SRXPanel.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---------- runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# curl is used by the container HEALTHCHECK
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

# Persisted data (SQLite DB, user homes) lives under /app/data
RUN mkdir -p /app/data
VOLUME ["/app/data"]

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:5000

EXPOSE 5000

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD curl -fsS http://localhost:5000/api/health || exit 1

ENTRYPOINT ["dotnet", "SRXPanel.dll"]
