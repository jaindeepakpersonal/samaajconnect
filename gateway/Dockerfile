# Build context is the repo root (see docker-compose.yml) so the shared
# Directory.Build.props / Directory.Packages.props are available.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY gateway/src/Sangam.Gateway/*.csproj gateway/src/Sangam.Gateway/

RUN dotnet restore gateway/src/Sangam.Gateway/Sangam.Gateway.csproj

COPY gateway/src/ gateway/src/

RUN dotnet publish gateway/src/Sangam.Gateway/Sangam.Gateway.csproj \
      --no-restore -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

USER $APP_UID

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Sangam.Gateway.dll"]
