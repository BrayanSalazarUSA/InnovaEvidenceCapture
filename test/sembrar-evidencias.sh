#!/usr/bin/env bash
#
# Siembra evidencias de prueba en el backend, como si las hubiera capturado
# la app de Windows desde varios PCs de monitoreo.
#
#   ./sembrar-evidencias.sh                              -> http://localhost:8080
#   ./sembrar-evidencias.sh https://innova-dashboard.com -> backend desplegado
#
set -u

BASE="${1:-http://localhost:8080}"
IMG="${2:-$(dirname "$0")/evidencia-prueba.png}"
URL="$BASE/api/evidence-captures"

if [ ! -f "$IMG" ]; then
  echo "No encuentro la imagen: $IMG"
  exit 1
fi

echo "Backend: $URL"
echo "Imagen:  $IMG"
echo

# Estaciones y minutos hacia atras, para que salgan agrupadas por PC y por hora.
ENTRIES=(
  "INNMONITOR20|Mesa 1|2"
  "INNMONITOR20|Mesa 1|9"
  "INNMONITOR20|Mesa 1|24"
  "INNMONITOR21|Mesa 2|6"
  "INNMONITOR21|Mesa 2|41"
  "INNMONITOR22|Mesa 3|17"
)

ok=0
fail=0

for entry in "${ENTRIES[@]}"; do
  IFS='|' read -r station name ago <<< "$entry"

  # Hora de captura = ahora menos N minutos, con offset local.
  if date -v -1M >/dev/null 2>&1; then
    captured=$(date -v -"${ago}"M "+%Y-%m-%dT%H:%M:%S%z")   # macOS
  else
    captured=$(date -d "-${ago} minutes" "+%Y-%m-%dT%H:%M:%S%z")  # Linux
  fi
  captured="${captured:0:22}:${captured:22:2}"

  id="seed-$(date +%s)-$RANDOM"

  meta=$(cat <<JSON
{"clientCaptureId":"$id","stationCode":"$station","stationName":"$name","captureType":"IMAGE","capturedAt":"$captured","mimeType":"image/png","width":1324,"height":735,"windowsUser":"agente.turno","appVersion":"0.1.0"}
JSON
)

  : > /tmp/seed-resp.txt
  code=$(curl -s -o /tmp/seed-resp.txt -w "%{http_code}" \
    -F "file=@${IMG};type=image/png" \
    -F "metadata=$meta" \
    "$URL")

  if [ "$code" = "200" ]; then
    echo "  OK   $station  hace ${ago} min  -> $(cat /tmp/seed-resp.txt)"
    ok=$((ok+1))
  else
    echo "  FALLO $station  HTTP $code"
    echo "        $(head -c 300 /tmp/seed-resp.txt)"
    fail=$((fail+1))
  fi
done

echo
echo "Subidas: $ok    Fallidas: $fail"
echo
echo "Ahora revisa:"
echo "  1. $BASE/evidence-wall.html"
echo "  2. En la app movil: Evidencias -> Evidencias del PC"
echo
echo "Estaciones que ve el backend:"
curl -s --max-time 15 "$BASE/api/evidence-captures/stations"; echo
