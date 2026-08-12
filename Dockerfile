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

# Render routes to the port the app listens on; 8080 is exposed below.
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
# Honour Render's X-Forwarded-Proto so UseHttpsRedirection does not loop behind
# the TLS-terminating proxy.
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
EXPOSE 8080

ENTRYPOINT ["dotnet", "AgriERP.API.dll"]
