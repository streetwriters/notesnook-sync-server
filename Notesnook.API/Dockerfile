FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-alpine AS base
WORKDIR /app

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
ARG TARGETARCH
ARG BUILDPLATFORM
ENV DOTNET_TC_QuickJitForLoops="1" DOTNET_ReadyToRun="0" DOTNET_TieredPGO="1" DOTNET_SYSTEM_GLOBALIZATION_INVARIANT="true"

WORKDIR /src

COPY Streetwriters.Data/*.csproj ./Streetwriters.Data/
COPY Streetwriters.Common/*.csproj ./Streetwriters.Common/
COPY Notesnook.API/*.csproj ./Notesnook.API/

# restore dependencies
RUN dotnet restore -v d /src/Notesnook.API/Notesnook.API.csproj --use-current-runtime

COPY Streetwriters.Data/ ./Streetwriters.Data/
COPY Streetwriters.Common/ ./Streetwriters.Common/
COPY Notesnook.API/ ./Notesnook.API/

WORKDIR /src/Notesnook.API/

RUN dotnet build -c Release -o /app/build -a $TARGETARCH

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish \
  #--runtime alpine-x64 \
  --self-contained true \
  /p:TrimMode=partial \
  /p:PublishTrimmed=true \
  /p:PublishSingleFile=true \
  /p:JsonSerializerIsReflectionEnabledByDefault=true \
  -a $TARGETARCH

FROM --platform=$BUILDPLATFORM base AS final
ARG TARGETARCH
ARG BUILDPLATFORM

# create a new user and change directory ownership
RUN adduser --disabled-password \
  --home /app \
  --gecos '' dotnetuser && chown -R dotnetuser /app

# impersonate into the new user
USER dotnetuser
WORKDIR /app

COPY --from=publish /app/publish .
ENTRYPOINT ["./Notesnook.API"]