FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so Docker can cache packages between builds.
COPY src/StudyHub.Api/StudyHub.Api.csproj src/StudyHub.Api/
RUN dotnet restore src/StudyHub.Api/StudyHub.Api.csproj

COPY src/ src/
RUN dotnet publish src/StudyHub.Api/StudyHub.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Run as the non-root user that comes with the official image.
USER $APP_UID
ENTRYPOINT ["dotnet", "StudyHub.Api.dll"]
