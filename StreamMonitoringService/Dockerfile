FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["StreamMonitoringService/StreamMonitoringService.csproj", "StreamMonitoringService/"]
RUN dotnet restore "StreamMonitoringService/StreamMonitoringService.csproj"
COPY . .
WORKDIR "/src/StreamMonitoringService"
RUN dotnet build "StreamMonitoringService.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "StreamMonitoringService.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "StreamMonitoringService.dll"]
