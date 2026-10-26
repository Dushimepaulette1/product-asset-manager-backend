FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY src/ProductAssetManager.Api.csproj src/
RUN dotnet restore src/ProductAssetManager.Api.csproj

COPY src/ src/
RUN dotnet publish src/ProductAssetManager.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

USER $APP_UID

ARG APP_VERSION=dev
ARG GIT_SHA=unknown
ENV Build__Version=$APP_VERSION \
    Build__CommitSha=$GIT_SHA

HEALTHCHECK --interval=10s --timeout=6s --start-period=30s --retries=5 \
    CMD curl --fail --silent http://localhost:8080/health/ready || exit 1

ENTRYPOINT ["dotnet", "ProductAssetManager.Api.dll"]
