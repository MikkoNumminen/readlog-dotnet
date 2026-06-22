# syntax=docker/dockerfile:1

# ---- Build stage -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first so the layer is cached unless the project files change.
COPY global.json Directory.Build.props ./
COPY src/ReadLog.Web/ReadLog.Web.csproj src/ReadLog.Web/
RUN dotnet restore src/ReadLog.Web/ReadLog.Web.csproj

# Copy the source and publish the web app (framework-dependent, no apphost).
COPY src/ src/
RUN dotnet publish src/ReadLog.Web/ReadLog.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- Runtime stage -----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# The .NET 8 image listens on 8080 by default; keep that explicit.
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Default="Data Source=/home/data/readlog.db"

COPY --from=build /app/publish .

# A persistent, writable home for the SQLite database, owned by the non-root app user.
# On Azure App Service this path lives on the mounted /home share (set
# WEBSITES_ENABLE_APP_SERVICE_STORAGE=true to persist it).
RUN mkdir -p /home/data && chown -R $APP_UID:0 /home/data && chmod -R g+rwX /home/data
USER $APP_UID

ENTRYPOINT ["dotnet", "ReadLog.Web.dll"]
