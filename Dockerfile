# Built by GitHub Actions and run on the VPS — see deploy/docker-compose.yml.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Project files first, so the restore layer is cached until a dependency changes.
COPY ["Travel.Domain/Travel.Domain.csproj", "Travel.Domain/"]
COPY ["Travel.Repository/Travel.Repository.csproj", "Travel.Repository/"]
COPY ["Travel.Services/Travel.Services.csproj", "Travel.Services/"]
COPY ["Travel.Presentation/Travel.Presentation.csproj", "Travel.Presentation/"]
RUN dotnet restore "Travel.Presentation/Travel.Presentation.csproj"

COPY . .
RUN dotnet publish "Travel.Presentation/Travel.Presentation.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# The non-root user the aspnet image ships (uid 1654). The host folders mounted for uploads,
# logs and data-protection keys are owned by that uid.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DataProtection__KeysPath=/app/keys
EXPOSE 8080
ENTRYPOINT ["dotnet", "Travel.Presentation.dll"]
