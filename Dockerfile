FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY . .

RUN apk add --no-cache \
    clang \
    build-base \
    zlib-dev

RUN dotnet restore HappyDaytime.slnx

RUN dotnet test HappyDaytime.slnx \
    --configuration Release \
    --no-restore

RUN dotnet publish HappyDaytime/HappyDaytime.csproj \
    --configuration Release \
    --runtime linux-musl-x64 \
    --self-contained true \
    /p:PublishAot=true \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine AS final
WORKDIR /app

COPY --from=build /app/publish .

# Daytime's assigned port is 13, but binding ports below 1024
# usually requires root or extra Linux capabilities.
EXPOSE 1313

ENV Daytime__ListenAddress=0.0.0.0
ENV Daytime__Port=1313
ENV Daytime__MaxConcurrentConnections=100

USER $APP_UID

ENTRYPOINT ["./HappyDaytime"]
