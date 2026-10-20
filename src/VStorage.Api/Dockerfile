# VStorage API. Build context: the solution folder (D:\Family\Developer\VStorageAPI), e.g.
#   docker build -f src/VStorage.Api/Dockerfile -t vstorage-api .
# Started by D:\Family\Developer\VDocker\docker-compose.yml (blobs and Data Protection keys are mounted).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first: the restore layer is reused while only code changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/VStorage.Domain/VStorage.Domain.csproj src/VStorage.Domain/
COPY src/VStorage.Application/VStorage.Application.csproj src/VStorage.Application/
COPY src/VStorage.Infrastructure/VStorage.Infrastructure.csproj src/VStorage.Infrastructure/
COPY src/VStorage.Api/VStorage.Api.csproj src/VStorage.Api/
RUN dotnet restore src/VStorage.Api/VStorage.Api.csproj

COPY src/ src/
RUN dotnet publish src/VStorage.Api/VStorage.Api.csproj -c Release --no-restore -o /app/publish -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Mount points (blobs, keys) and the log folder belong to the image's non-root user.
RUN mkdir -p /app/logs /data /keys && chown "$APP_UID" /app/logs /data /keys
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Docker
EXPOSE 8080
ENTRYPOINT ["dotnet", "VStorage.Api.dll"]
