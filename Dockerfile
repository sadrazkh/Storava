# syntax=docker/dockerfile:1.7

FROM node:24-bookworm-slim AS client
WORKDIR /source/src/Storava.Web
COPY src/Storava.Web/package.json src/Storava.Web/package-lock.json ./
RUN npm ci
COPY src/Storava.Web/tsconfig.json src/Storava.Web/vite.config.ts ./
COPY src/Storava.Web/ClientApp ./ClientApp
COPY src/Storava.Web/wwwroot ./wwwroot
RUN npm run typecheck && npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props Directory.Packages.props nuget.config ./
# Storava.Web references Storava.Contracts, so both have to be here. Restore does not need it —
# it completes quite happily with the reference dangling, which is why this was missed — and the
# publish below is where it turns into "the namespace Contracts does not exist". The image has
# not built since that reference was added, and CI never said so because the job ahead of this
# one kept failing first.
COPY src/Storava.Contracts/Storava.Contracts.csproj src/Storava.Contracts/
COPY src/Storava.Web/Storava.Web.csproj src/Storava.Web/
RUN dotnet restore src/Storava.Web/Storava.Web.csproj
COPY src/Storava.Contracts ./src/Storava.Contracts
COPY src/Storava.Web ./src/Storava.Web
COPY --from=client /source/src/Storava.Web/wwwroot/dist ./src/Storava.Web/wwwroot/dist
RUN dotnet publish src/Storava.Web/Storava.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:BuildClientAssets=false \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-bookworm-slim AS runtime
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /home/app/.aspnet/DataProtection-Keys \
    && chown -R "$APP_UID":"$APP_UID" /home/app
WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish ./
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0 \
    WebSecurity__UseHttpsRedirection=false
EXPOSE 8080
USER $APP_UID
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl --fail --silent http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["dotnet", "Storava.Web.dll"]
