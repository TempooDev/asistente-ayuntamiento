FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files
COPY ["AsistenteAyuntamiento.Worker/AsistenteAyuntamiento.Worker.csproj", "AsistenteAyuntamiento.Worker/"]
COPY ["AsistenteAyuntamiento.ApiService/AsistenteAyuntamiento.ApiService.csproj", "AsistenteAyuntamiento.ApiService/"]
COPY ["AsistenteAyuntamiento.ServiceDefaults/AsistenteAyuntamiento.ServiceDefaults.csproj", "AsistenteAyuntamiento.ServiceDefaults/"]
COPY ["AsistenteAyuntamiento.Shared/AsistenteAyuntamiento.Shared.csproj", "AsistenteAyuntamiento.Shared/"]
COPY ["Protos/", "Protos/"]

# Restore dependencies
RUN dotnet restore "AsistenteAyuntamiento.Worker/AsistenteAyuntamiento.Worker.csproj"

# Copy the rest of the source code
COPY . .

# Build and publish
WORKDIR "/src/AsistenteAyuntamiento.Worker"
RUN dotnet publish -c Release -o /app/publish

# Final runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AsistenteAyuntamiento.Worker.dll"]
