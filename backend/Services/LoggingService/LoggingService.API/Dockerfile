# Build context = repository root (see docker-compose.yml / .csproj DockerfileContext)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER root
RUN mkdir -p /var/log/microservices && chown -R app /var/log/microservices
USER app
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY Directory.Build.props ./
COPY backend/ backend/
RUN dotnet restore "backend/Services/LoggingService/LoggingService.API/LoggingService.API.csproj"
RUN dotnet build "backend/Services/LoggingService/LoggingService.API/LoggingService.API.csproj" -c $BUILD_CONFIGURATION --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "backend/Services/LoggingService/LoggingService.API/LoggingService.API.csproj" -c $BUILD_CONFIGURATION --no-build -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LoggingService.API.dll"]
