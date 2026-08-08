# syntax=docker/dockerfile:1

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore from the .csproj files alone so editing source doesn't invalidate the
# NuGet layer. The solution file name has a space in it; publishing the startup
# project directly sidesteps that entirely.
COPY Travel.Domain/Travel.Domain.csproj             Travel.Domain/
COPY Travel.Repository/Travel.Repository.csproj     Travel.Repository/
COPY Travel.Services/Travel.Services.csproj         Travel.Services/
COPY Travel.Presentation/Travel.Presentation.csproj Travel.Presentation/
RUN dotnet restore Travel.Presentation/Travel.Presentation.csproj

COPY . .
RUN dotnet publish Travel.Presentation/Travel.Presentation.csproj \
        -c Release -o /publish --no-restore


# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final

# ClosedXML's AdjustToContents() measures text to autofit columns; the base
# image ships no fonts, so the .xlsx exports would throw without these.
RUN apt-get update \
 && apt-get install -y --no-install-recommends fontconfig fonts-liberation \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    HOME=/home/app

# Serilog writes ./Logs relative to WORKDIR and IFileStorageService writes
# wwwroot/Files; both, plus the Data Protection keyring, must be writable by the
# non-root user. Serilog's file sink fails silently, so a missing chown here
# shows up as an empty Logs directory rather than an error.
RUN mkdir -p /app/Logs /app/wwwroot/Files /home/app/.aspnet/DataProtection-Keys \
 && chown -R $APP_UID:$APP_UID /app/Logs /app/wwwroot/Files /home/app
USER $APP_UID

EXPOSE 8080
ENTRYPOINT ["dotnet", "Travel.Presentation.dll"]


# ---------- dev (dotnet watch) ----------
# Deliberately stays root: a bind-mounted Windows volume plus a non-root uid is
# a permissions fight with no upside in development.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS dev

RUN apt-get update \
 && apt-get install -y --no-install-recommends fontconfig fonts-liberation \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /src/Travel.Presentation

# inotify doesn't propagate across a Windows bind mount — watch needs polling.
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Development \
    DOTNET_USE_POLLING_FILE_WATCHER=1

EXPOSE 8080
# --no-launch-profile is load-bearing: launchSettings.json binds
# http://localhost:5145, and a localhost binding inside the container is
# unreachable from the host.
ENTRYPOINT ["dotnet", "watch", "run", "--no-launch-profile", "--urls", "http://+:8080"]
