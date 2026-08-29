# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["src/Bump.sln", "./"]
COPY ["src/domain/domain.csproj", "domain/"]
COPY ["src/app/app.csproj", "app/"]
COPY ["src/infra/infra.csproj", "infra/"]
COPY ["src/api/api.csproj", "api/"]
RUN dotnet restore "Bump.sln"

COPY ["src/domain/", "domain/"]
COPY ["src/app/", "app/"]
COPY ["src/infra/", "infra/"]
COPY ["src/api/", "api/"]

RUN dotnet publish "api/api.csproj" \
    --configuration "$BUILD_CONFIGURATION" \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "api.dll"]
