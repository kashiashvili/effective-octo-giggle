#!/usr/bin/env bash
#
# One-time Azure setup for the Profiler demo. Creates the resource group, an App Service plan, a
# Linux container web app, and every app setting the app needs — then prints the publish profile to
# paste into GitHub as the AZURE_WEBAPP_PUBLISH_PROFILE secret.
#
# Run it yourself (it needs YOUR Azure login; the agent cannot log in as you):
#
#   az login
#   ./deploy/azure-bootstrap.sh
#
# Override any default with an env var, e.g.:
#   APP_NAME=my-profiler LOCATION=northeurope SKU=B1 ./deploy/azure-bootstrap.sh
#
set -euo pipefail

RG="${RG:-profiler-rg}"
LOCATION="${LOCATION:-westeurope}"
PLAN="${PLAN:-profiler-plan}"
# Must be globally unique — it becomes https://APP_NAME.azurewebsites.net
APP_NAME="${APP_NAME:-profiler-demo-$(head -c4 /dev/urandom | od -An -tx1 | tr -d ' \n')}"
# F1 = free (sleeps when idle, 60 CPU-min/day). B1 ~= $13/mo, always on. Change later with:
#   az appservice plan update -g $RG -n $PLAN --sku B1
SKU="${SKU:-F1}"
IMAGE="${IMAGE:-ghcr.io/kashiashvili/effective-octo-giggle:latest}"

echo "==> Resource group $RG ($LOCATION)"
az group create -n "$RG" -l "$LOCATION" -o none

echo "==> App Service plan $PLAN (sku $SKU, Linux)"
az appservice plan create -g "$RG" -n "$PLAN" --is-linux --sku "$SKU" -o none

echo "==> Web app $APP_NAME  (image: $IMAGE)"
az webapp create -g "$RG" -p "$PLAN" -n "$APP_NAME" --container-image-name "$IMAGE" -o none

# --- secrets -------------------------------------------------------------------------------------
# The pepper is PERMANENT: changing it invalidates every stored fingerprint and forces every user to
# reconnect. Generated once here; back up the values this script prints.
PEPPER="${PEPPER:-$(openssl rand -base64 32)}"
METRICS_TOKEN="${METRICS_TOKEN:-$(openssl rand -base64 24)}"

echo "==> App settings"
az webapp config appsettings set -g "$RG" -n "$APP_NAME" -o none --settings \
  ASPNETCORE_ENVIRONMENT=Production \
  WEBSITES_PORT=8080 \
  `# /home is the only persistent path on App Service. Without this the SQLite database AND the` \
  `# Data Protection key ring live on ephemeral disk: every restart wipes accounts and logs` \
  `# everyone out.` \
  WEBSITES_ENABLE_APP_SERVICE_STORAGE=true \
  "ConnectionStrings__Default=Data Source=/home/data/profiler.db" \
  DataProtection__KeyPath=/home/data/keys \
  "Fingerprint__Pepper=$PEPPER" \
  "Metrics__Token=$METRICS_TOKEN" \
  AntiAbuse__GuardRegistration=true \
  `# App Service terminates TLS in front of the container, so the app only sees the real client IP` \
  `# and https scheme via X-Forwarded-*. Enabling this WITHOUT naming trusted networks makes the app` \
  `# refuse to start, on purpose — unnamed proxies would let anyone forge their address past the` \
  `# rate limiters. These are the platform's internal ranges.` \
  ForwardedHeaders__Enabled=true \
  "ForwardedHeaders__KnownNetworks=10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,169.254.0.0/16,127.0.0.0/8,::ffff:0:0/96"

echo "==> Single instance (SQLite corrupts if App Service scales out) + health check"
az appservice plan update -g "$RG" -n "$PLAN" --number-of-workers 1 -o none 2>/dev/null || true
az webapp config set -g "$RG" -n "$APP_NAME" --generic-configurations '{"healthCheckPath":"/"}' -o none 2>/dev/null || true

echo
echo "=================================================================="
echo " App URL:  https://$APP_NAME.azurewebsites.net"
echo
echo " SAVE THESE — the pepper cannot be changed later without wiping"
echo " every stored fingerprint:"
echo "   Fingerprint__Pepper = $PEPPER"
echo "   Metrics__Token      = $METRICS_TOKEN"
echo
echo " In GitHub > Settings > Secrets and variables > Actions:"
echo "   Variables tab:  AZURE_WEBAPP_NAME            = $APP_NAME"
echo "   Secrets tab:    AZURE_WEBAPP_PUBLISH_PROFILE = (the XML printed below)"
echo
echo " Also make the GHCR package public (Packages > profiler > Package settings),"
echo " or App Service cannot pull the image. The image holds no secrets - the pepper"
echo " is injected at runtime - so publishing it exposes nothing."
echo "=================================================================="
echo
az webapp deployment list-publishing-profiles -g "$RG" -n "$APP_NAME" --xml
