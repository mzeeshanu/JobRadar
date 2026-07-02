# ── Build ─────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY JobRadar.slnx ./
COPY JobRadar/JobRadar.csproj JobRadar/
RUN dotnet restore JobRadar/JobRadar.csproj

COPY JobRadar/ JobRadar/
RUN dotnet publish JobRadar/JobRadar.csproj -c Release -o /app/publish --no-restore

# In .NET 9/10 the Blazor framework files (_framework/blazor.web.js, etc.)
# live inside the SDK's shared framework pack and are not automatically copied
# into the publish output when targeting a self-contained=false deployment.
# Find them under the AspNetCore App pack and copy them so the runtime image
# has everything the browser needs to boot Blazor interactivity.
RUN FRAMEWORK_DIR=$(find /usr/share/dotnet/packs/Microsoft.AspNetCore.App.Ref \
        -type d -name "_framework" 2>/dev/null | head -n 1) && \
    if [ -n "$FRAMEWORK_DIR" ]; then \
        mkdir -p /app/publish/_framework && \
        cp -r "$FRAMEWORK_DIR"/. /app/publish/_framework/; \
    fi

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
