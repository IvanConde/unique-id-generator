# Build stage: the SDK image restores and publishes the API.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Project files first, so the restore layer is cached while only the code changes.
COPY global.json ./
COPY src/UniqueIdGenerator.Core/UniqueIdGenerator.Core.csproj src/UniqueIdGenerator.Core/
COPY src/UniqueIdGenerator.Api/UniqueIdGenerator.Api.csproj src/UniqueIdGenerator.Api/
RUN dotnet restore src/UniqueIdGenerator.Api/UniqueIdGenerator.Api.csproj

COPY src/ src/
RUN dotnet publish src/UniqueIdGenerator.Api/UniqueIdGenerator.Api.csproj -c Release -o /app --no-restore

# Runtime stage: only the ASP.NET Core runtime and the published output.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "UniqueIdGenerator.Api.dll"]
