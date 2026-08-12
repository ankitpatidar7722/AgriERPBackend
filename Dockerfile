# ==============================================================================
#  AgriERP API - container image for Render (or any Docker host)
#  Build context = the Backend/ folder.
# ==============================================================================

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore AgriERP.API/AgriERP.API.csproj
RUN dotnet publish AgriERP.API/AgriERP.API.csproj -c Release -o /app /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production
# Honour Render's X-Forwarded-Proto so UseHttpsRedirection does not loop behind
# the TLS-terminating proxy.
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
EXPOSE 8080

# Render assigns the listening port via $PORT; bind to it (fallback 8080 locally).
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} exec dotnet AgriERP.API.dll"]
