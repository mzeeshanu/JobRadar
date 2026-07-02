# ── Build ─────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY JobRadar.slnx ./
COPY JobRadar/JobRadar.csproj JobRadar/
RUN dotnet restore JobRadar/JobRadar.csproj

COPY JobRadar/ JobRadar/
RUN dotnet publish JobRadar/JobRadar.csproj -c Release -o /app/publish --no-restore

# ── Runtime ───────────────────────────────────────────────────────────────
# Playwright's own image ships Chromium + all OS deps needed for headless
# crawling. The tag's Playwright version must match the Microsoft.Playwright
# NuGet version in JobRadar.csproj — bump both together.
FROM mcr.microsoft.com/playwright/dotnet:v1.61.0-noble AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
# Railway injects PORT at runtime; Program.cs binds to it. 8080 is just the
# local default so `docker run` works without extra flags.
EXPOSE 8080

ENTRYPOINT ["dotnet", "JobRadar.dll"]
