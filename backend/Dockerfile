FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/CamGuard.Api/CamGuard.Api.csproj src/CamGuard.Api/
RUN dotnet restore src/CamGuard.Api/CamGuard.Api.csproj
COPY src/ src/
RUN dotnet publish src/CamGuard.Api/CamGuard.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data && chown "$APP_UID" /data
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    App__DataDirectory=/data
VOLUME /data
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "CamGuard.Api.dll"]
