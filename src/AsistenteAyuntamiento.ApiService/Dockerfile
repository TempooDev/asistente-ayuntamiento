# syntax=docker/dockerfile:1
# Build from the repository root: docker build -f src/AsistenteAyuntamiento.ApiService/Dockerfile .
# CI publishes the same service with `dotnet publish -t:PublishContainer` (.github/workflows/deploy.yml); this file
# builds an equivalent image with Docker alone, without the .NET SDK on the host.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/AsistenteAyuntamiento.Domain/AsistenteAyuntamiento.Domain.csproj src/AsistenteAyuntamiento.Domain/
COPY src/AsistenteAyuntamiento.Shared/AsistenteAyuntamiento.Shared.csproj src/AsistenteAyuntamiento.Shared/
COPY src/AsistenteAyuntamiento.Application/AsistenteAyuntamiento.Application.csproj src/AsistenteAyuntamiento.Application/
COPY src/AsistenteAyuntamiento.Infrastructure/AsistenteAyuntamiento.Infrastructure.csproj src/AsistenteAyuntamiento.Infrastructure/
COPY src/AsistenteAyuntamiento.ServiceDefaults/AsistenteAyuntamiento.ServiceDefaults.csproj src/AsistenteAyuntamiento.ServiceDefaults/
COPY src/AsistenteAyuntamiento.ApiService/AsistenteAyuntamiento.ApiService.csproj src/AsistenteAyuntamiento.ApiService/
RUN dotnet restore src/AsistenteAyuntamiento.ApiService/AsistenteAyuntamiento.ApiService.csproj
COPY src/Protos/ src/Protos/
COPY src/AsistenteAyuntamiento.Domain/ src/AsistenteAyuntamiento.Domain/
COPY src/AsistenteAyuntamiento.Shared/ src/AsistenteAyuntamiento.Shared/
COPY src/AsistenteAyuntamiento.Application/ src/AsistenteAyuntamiento.Application/
COPY src/AsistenteAyuntamiento.Infrastructure/ src/AsistenteAyuntamiento.Infrastructure/
COPY src/AsistenteAyuntamiento.ServiceDefaults/ src/AsistenteAyuntamiento.ServiceDefaults/
COPY src/AsistenteAyuntamiento.ApiService/ src/AsistenteAyuntamiento.ApiService/
RUN dotnet publish src/AsistenteAyuntamiento.ApiService/AsistenteAyuntamiento.ApiService.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "AsistenteAyuntamiento.ApiService.dll"]
