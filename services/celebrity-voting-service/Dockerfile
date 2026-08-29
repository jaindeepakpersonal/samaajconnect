# Build context is the repo root (see docker-compose.yml) so the shared
# Directory.Build.props / Directory.Packages.props are available - without them
# a service builds with different package versions inside the image than out.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

ARG SERVICE_DIR=services/celebrity-voting-service

COPY Directory.Build.props Directory.Packages.props ./
COPY ${SERVICE_DIR}/src/Sangam.CelebrityVoting.Domain/*.csproj          ${SERVICE_DIR}/src/Sangam.CelebrityVoting.Domain/
COPY ${SERVICE_DIR}/src/Sangam.CelebrityVoting.Application/*.csproj     ${SERVICE_DIR}/src/Sangam.CelebrityVoting.Application/
COPY ${SERVICE_DIR}/src/Sangam.CelebrityVoting.Infrastructure/*.csproj  ${SERVICE_DIR}/src/Sangam.CelebrityVoting.Infrastructure/
COPY ${SERVICE_DIR}/src/Sangam.CelebrityVoting.Api/*.csproj             ${SERVICE_DIR}/src/Sangam.CelebrityVoting.Api/

# Restore before copying sources so a source-only change reuses the layer.
RUN dotnet restore ${SERVICE_DIR}/src/Sangam.CelebrityVoting.Api/Sangam.CelebrityVoting.Api.csproj

COPY ${SERVICE_DIR}/src/ ${SERVICE_DIR}/src/

RUN dotnet publish ${SERVICE_DIR}/src/Sangam.CelebrityVoting.Api/Sangam.CelebrityVoting.Api.csproj \
      --no-restore -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Non-root: nothing in this service needs to write to the image.
USER $APP_UID

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Sangam.CelebrityVoting.Api.dll"]
