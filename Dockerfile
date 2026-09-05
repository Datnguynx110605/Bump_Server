# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["src/Bump.sln", "./"]
COPY ["src/domain/domain.csproj", "domain/"]
COPY ["src/app/app.csproj", "app/"]
COPY ["src/infra/infra.csproj", "infra/"]
COPY ["src/api/api.csproj", "api/"]
RUN dotnet restore "api/api.csproj"

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

EXPOSE 8080

COPY --from=build /app/publish .

# Railway injects PORT at runtime; 8080 is the fallback for a plain `docker run`.
# `exec` keeps dotnet as PID 1 so SIGTERM still reaches it for graceful shutdown.
ENTRYPOINT ["sh", "-c", "exec dotnet api.dll --urls \"http://0.0.0.0:${PORT:-8080}\""]
