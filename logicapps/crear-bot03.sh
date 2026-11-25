#!/bin/bash
# Crea BOT-03 (correo de aprobación) en Azure Logic Apps, plan de consumo.
# Uso:
#   bash logicapps/crear-bot03.sh conexion              -> crea la conexión de Outlook (luego se autoriza en el portal)
#   bash logicapps/crear-bot03.sh estado                -> muestra si la conexión ya está autorizada
#   bash logicapps/crear-bot03.sh flujo [correoPruebas] -> crea el flujo y conecta la web y la API con él
# La clave de la API y la conexión se leen desde Azure: este archivo no guarda secretos.
set -euo pipefail
RG=rg-enami-jhoel
LOC=brazilsouth
WEB_APP=enami-jhoel
API_APP=enami-jhoel-api
URL_WEB=https://enami-jhoel-gygah4fhd7dqfsfn.brazilsouth-01.azurewebsites.net
URL_API=https://enami-jhoel-api.azurewebsites.net
SUB=$(az account show --query id -o tsv)
BASE="https://management.azure.com/subscriptions/$SUB/resourceGroups/$RG/providers"
API_OUTLOOK="/subscriptions/$SUB/providers/Microsoft.Web/locations/$LOC/managedApis/outlook"

case "${1:-}" in
  conexion)
    az rest --method put --url "$BASE/Microsoft.Web/connections/outlook?api-version=2016-06-01" \
      --body "{\"location\":\"$LOC\",\"properties\":{\"displayName\":\"Outlook BOT-03\",\"api\":{\"id\":\"$API_OUTLOOK\"}}}" -o none
    echo "Conexión creada. Ahora autorícela en el portal de Azure con la cuenta de Outlook."
    ;;
  estado)
    az rest --method get --url "$BASE/Microsoft.Web/connections/outlook?api-version=2016-06-01" --query "properties.statuses[0].status" -o tsv
    ;;
  flujo)
    export CORREO_PRUEBAS="${2:-}"
    export CLAVE=$(az webapp config appsettings list --resource-group $RG --name $API_APP --query "[?name=='ClaveApiRobots'].value" -o tsv)
    export ID_CONEXION="/subscriptions/$SUB/resourceGroups/$RG/providers/Microsoft.Web/connections/outlook" API_OUTLOOK LOC
    CUERPO=$(mktemp)
    python3 - "$CUERPO" <<'PY'
import json, os, sys
d = json.load(open("logicapps/BOT-03_aprobacion.json", encoding="utf-8"))["definition"]
body = {"location": os.environ["LOC"], "properties": {"state": "Enabled", "definition": d, "parameters": {
    "$connections": {"value": {"outlook": {"connectionId": os.environ["ID_CONEXION"], "connectionName": "outlook", "id": os.environ["API_OUTLOOK"]}}},
    "claveApi": {"value": os.environ["CLAVE"]},
    "correoPruebas": {"value": os.environ["CORREO_PRUEBAS"]}}}}
json.dump(body, open(sys.argv[1], "w", encoding="utf-8"), ensure_ascii=False)
PY
    az rest --method put --url "$BASE/Microsoft.Logic/workflows/bot03-aprobacion?api-version=2016-06-01" --body @"$CUERPO" -o none
    rm -f "$CUERPO"
    URL=$(az rest --method post --url "$BASE/Microsoft.Logic/workflows/bot03-aprobacion/triggers/Aviso_de_la_aplicacion/listCallbackUrl?api-version=2016-06-01" --query value -o tsv)
    for app in $WEB_APP $API_APP; do
      az webapp config appsettings set --resource-group $RG --name $app \
        --settings "Flujos__UrlAprobacion=$URL" "Flujos__UrlWeb=$URL_WEB" "Flujos__UrlApi=$URL_API" -o none
    done
    echo "BOT-03 creado y conectado con la web y la API (correo de pruebas: ${CORREO_PRUEBAS:-ninguno})."
    ;;
  *)
    echo "Uso: bash logicapps/crear-bot03.sh conexion | estado | flujo [correoPruebas]"; exit 1 ;;
esac
