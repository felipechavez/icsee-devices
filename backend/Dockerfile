# syntax=docker/dockerfile:1
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
    && mkdir -p /data /app/models && chown "$APP_UID" /data
# Detector de personas y animales (YOLOX-nano, Apache-2.0); el hash fija la versión. La carpeta va
# creada antes: si la crea ADD, le aplica --chmod y /app queda sin permiso de entrada.
ADD --checksum=sha256:c789161ed43c8269fcd4e67c67eeeb4e80c622da2eb296a20bc6007bd18a0b7d --chmod=644 \
    https://github.com/Megvii-BaseDetection/YOLOX/releases/download/0.1.1rc0/yolox_nano.onnx /app/models/yolox_nano.onnx
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    App__DataDirectory=/data
VOLUME /data
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "CamGuard.Api.dll"]
