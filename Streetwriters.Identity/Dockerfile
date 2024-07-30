FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# # restore all project dependencies
RUN dotnet restore -v d Notesnook.sln

# build
ENV DOTNET_TC_QuickJitForLoops="1" DOTNET_ReadyToRun="0" DOTNET_TieredPGO="1" DOTNET_SYSTEM_GLOBALIZATION_INVARIANT="true"
RUN dotnet publish /Streetwriters.Identity/Streetwriters.Identity.csproj -c Release -o /out --use-current-runtime --self-contained false --no-restore

# final stage/image
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "Streetwriters.Identity.dll"]