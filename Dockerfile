# syntax=docker/dockerfile:1

# ---- build ----------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore first, on just the project file, so the (slow) restore layer is cached until deps change.
COPY Profiler.Web/Profiler.Web.csproj Profiler.Web/
RUN dotnet restore Profiler.Web/Profiler.Web.csproj

# Then the sources and a Release publish.
COPY Profiler.Web/ Profiler.Web/
RUN dotnet publish Profiler.Web/Profiler.Web.csproj -c Release -o /app --no-restore

# ---- runtime --------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# The SQLite database and the Data Protection key ring live under /data — mount a volume here so they
# survive restarts and redeploys (lose the keys and every auth cookie is invalidated; lose the db and
# everyone's fingerprints are gone). Owned by the image's non-root "app" user.
RUN mkdir -p /data && chown -R app:app /data
VOLUME /data
USER app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__Default="Data Source=/data/profiler.db" \
    DataProtection__KeyPath=/data/keys
# Fingerprint__Pepper is deliberately NOT baked into the image — it is a per-deployment secret and the
# app refuses to start without one outside Development. Provide it at runtime, e.g.:
#   docker run -e Fingerprint__Pepper="$(openssl rand -base64 32)" -v profiler-data:/data -p 8080:8080 <image>

EXPOSE 8080
ENTRYPOINT ["dotnet", "Profiler.Web.dll"]
