FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /app

# copy projects
COPY Streetwriters.Data/*.csproj ./Streetwriters.Data/
COPY Streetwriters.Common/*.csproj ./Streetwriters.Common/
COPY Streetwriters.Messenger/*.csproj ./Streetwriters.Messenger/

# restore dependencies
RUN dotnet restore -v d /app/Streetwriters.Messenger/Streetwriters.Messenger.csproj --use-current-runtime

# copy everything else
COPY Streetwriters.Data/ ./Streetwriters.Data/
COPY Streetwriters.Common/ ./Streetwriters.Common/
COPY Streetwriters.Messenger/ ./Streetwriters.Messenger/

WORKDIR /app/Streetwriters.Messenger/
ENV DOTNET_TC_QuickJitForLoops="1" DOTNET_ReadyToRun="0" DOTNET_TieredPGO="1" DOTNET_SYSTEM_GLOBALIZATION_INVARIANT="true"
RUN dotnet publish -c Release -o /app/out --use-current-runtime --self-contained false --no-restore

# final stage/image
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/out .
ENTRYPOINT ["dotnet", "Streetwriters.Messenger.dll"]