FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
WORKDIR /source
COPY . .
RUN dotnet publish src/CityPortal.Api/CityPortal.Api.csproj --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "CityPortal.Api.dll"]
