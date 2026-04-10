# ── Build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["PriceTracker.csproj", "."]
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /out

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app

# Install Node.js (used once to download Playwright Chromium + system deps)
RUN apt-get update && apt-get install -y --no-install-recommends \
        curl ca-certificates \
    && curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /out .

# Download Playwright Chromium browser and install all required system libraries
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN npx -y playwright@1.58.0 install --with-deps chromium \
    && npm cache clean --force

ENV ASPNETCORE_ENVIRONMENT=Production

# Railway injects $PORT at runtime; default to 8080
CMD ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet PriceTracker.dll"]
