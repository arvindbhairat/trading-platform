FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY SignalStack.sln .
COPY Directory.Build.props .
COPY NuGet.Config .
COPY apps/api/ apps/api/
COPY packages/ packages/
RUN dotnet restore apps/api/SignalStack.Api.csproj
RUN dotnet publish apps/api/SignalStack.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Bootstrap config files (appconfig.local.json, keyvault.local.json, appconfig.lkg.json)
# are automatically included in publish output via csproj PreserveNewest.
# Copy explicitly for visibility and as a safety net.
COPY apps/api/bootstrap/ bootstrap/

EXPOSE 8080
ENTRYPOINT ["dotnet", "SignalStack.Api.dll"]
