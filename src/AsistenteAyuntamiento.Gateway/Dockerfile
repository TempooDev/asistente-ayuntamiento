# syntax=docker/dockerfile:1
# Build from the repository root: docker build -f src/AsistenteAyuntamiento.Gateway/Dockerfile .
# The Gateway serves the Angular UI from wwwroot and proxies /api, /hubs and /openapi to the ApiService. CI builds the
# same image with `dotnet publish -t:PublishContainer` after copying the Angular build (.github/workflows/deploy.yml).
FROM node:22-alpine AS ui
WORKDIR /ui
RUN npm install -g npm@11.6.2
COPY src/AsistenteAyuntamiento.Angular/package.json src/AsistenteAyuntamiento.Angular/package-lock.json ./
RUN npm ci --no-audit
COPY src/AsistenteAyuntamiento.Angular/ ./
RUN npm run build -- --configuration production

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/AsistenteAyuntamiento.ServiceDefaults/AsistenteAyuntamiento.ServiceDefaults.csproj src/AsistenteAyuntamiento.ServiceDefaults/
COPY src/AsistenteAyuntamiento.Gateway/AsistenteAyuntamiento.Gateway.csproj src/AsistenteAyuntamiento.Gateway/
RUN dotnet restore src/AsistenteAyuntamiento.Gateway/AsistenteAyuntamiento.Gateway.csproj
COPY src/AsistenteAyuntamiento.ServiceDefaults/ src/AsistenteAyuntamiento.ServiceDefaults/
COPY src/AsistenteAyuntamiento.Gateway/ src/AsistenteAyuntamiento.Gateway/
COPY --from=ui /ui/dist/AsistenteAyuntamiento.Angular/browser/ src/AsistenteAyuntamiento.Gateway/wwwroot/
RUN dotnet publish src/AsistenteAyuntamiento.Gateway/AsistenteAyuntamiento.Gateway.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "AsistenteAyuntamiento.Gateway.dll"]
