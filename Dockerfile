# Balance Lab w jednym obrazie: serwer gry (BalanceLab.Api) serwuje tez zbudowany front, jak w e2e.

# Front: React zbudowany przez Vite do web/dist.
FROM node:24-alpine AS web
WORKDIR /web
RUN corepack enable
COPY web/package.json web/pnpm-lock.yaml web/pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile
COPY web/ ./
RUN pnpm exec vite build

# Serwer: publikacja .NET bez testow i narzedzi.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/BalanceLab.Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /web/dist ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080 \
    Web__Root=/app/wwwroot
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "BalanceLab.Api.dll"]
