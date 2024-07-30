FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /app

ADD . /app

# restore all project dependencies
RUN dotnet restore -v d ./Notesnook.sln

WORKDIR /app/Notesnook.API/
# build
ENV DOTNET_TC_QuickJitForLoops="1" DOTNET_ReadyToRun="0" DOTNET_TieredPGO="1" DOTNET_SYSTEM_GLOBALIZATION_INVARIANT="true"
RUN dotnet publish -c Release -o /app/out --use-current-runtime --self-contained false --no-restore

# final stage/image
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/out .
ENTRYPOINT ["dotnet", "Notesnook.API.dll"]