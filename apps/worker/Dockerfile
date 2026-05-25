FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY SignalStack.sln .
COPY Directory.Build.props .
COPY NuGet.Config .
COPY apps/worker/ apps/worker/
COPY apps/api/ apps/api/
COPY packages/ packages/
RUN dotnet restore apps/worker/SignalStack.Worker.csproj

# Remove API config files before publish to avoid NETSDK1152 duplicates
# Worker references API project, but only needs its assemblies — not its content
RUN rm -f apps/api/appsettings.json apps/api/appsettings.Development.json \
    && rm -rf apps/api/bootstrap
RUN dotnet publish apps/worker/SignalStack.Worker.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Bootstrap config files (appconfig.local.json, keyvault.local.json, appconfig.lkg.json)
# are automatically included in publish output via csproj PreserveNewest.
# Copy explicitly for visibility and as a safety net.
COPY apps/worker/bootstrap/ bootstrap/

ENTRYPOINT ["dotnet", "SignalStack.Worker.dll"]
