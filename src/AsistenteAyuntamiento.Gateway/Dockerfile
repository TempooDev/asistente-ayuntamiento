FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files
COPY ["AsistenteAyuntamiento.Gateway/AsistenteAyuntamiento.Gateway.csproj", "AsistenteAyuntamiento.Gateway/"]
COPY ["AsistenteAyuntamiento.ServiceDefaults/AsistenteAyuntamiento.ServiceDefaults.csproj", "AsistenteAyuntamiento.ServiceDefaults/"]

# Restore dependencies
RUN dotnet restore "AsistenteAyuntamiento.Gateway/AsistenteAyuntamiento.Gateway.csproj"

# Copy the rest of the source code
COPY . .

# Build and publish
WORKDIR "/src/AsistenteAyuntamiento.Gateway"
RUN dotnet publish -c Release -o /app/publish

# Final runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AsistenteAyuntamiento.Gateway.dll"]
