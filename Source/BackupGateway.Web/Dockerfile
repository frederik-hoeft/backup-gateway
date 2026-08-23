# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY Directory.Build.props .editorconfig BackupGateway.slnx ./
COPY BackupGateway.Web/BackupGateway.Web.csproj BackupGateway.Web/
RUN dotnet restore BackupGateway.Web/BackupGateway.Web.csproj

COPY . .
RUN dotnet publish BackupGateway.Web/BackupGateway.Web.csproj \
    --configuration "$BUILD_CONFIGURATION" \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update \
    && apt-get install --yes --no-install-recommends openssh-client \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build --chown=app:app /app/publish .
USER app
ENTRYPOINT ["dotnet", "BackupGateway.Web.dll"]
