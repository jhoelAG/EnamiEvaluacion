#!/bin/bash
# Crea o actualiza BOT-01 (dólar, cobre, oro y plata todos los días a las 09:00) en Azure Logic Apps, plan de consumo.
# Fuente principal: API oficial del Banco Central de Chile (dólar observado, cobre BML, oro y plata); respaldo: mindicador.cl.
# Usa la conexión de Outlook de BOT-03 para el correo de alerta (créela antes con crear-bot03.sh conexion).
# Uso:
#   bash logicapps/crear-bot01.sh credenciales            -> guarda en Azure el usuario y la clave de la API del Banco Central
#                                                            (la clave se escribe sin que aparezca en pantalla)
#   bash logicapps/crear-bot01.sh flujo <correoAlertas>   -> crea o actualiza el flujo
#   bash logicapps/crear-bot01.sh ejecutar                -> lo ejecuta ahora y muestra el resultado de cada paso
#   bash logicapps/crear-bot01.sh probar-alerta           -> ejecuta una vez simulando una falla, para probar el aviso
# La clave de la API se lee desde Azure: este archivo no guarda secretos.
set -euo pipefail
RG=rg-enami-jhoel
LOC=brazilsouth
API_APP=enami-jhoel-api
WEB_APP=enami-jhoel
URL_API=https://enami-jhoel-api.azurewebsites.net
SUB=$(az account show --query id -o tsv)
BASE="https://management.azure.com/subscriptions/$SUB/resourceGroups/$RG/providers"
FLUJO="$BASE/Microsoft.Logic/workflows/bot01-mercado"
V="api-version=2016-06-01"

publicar() {   # $1 correo de alertas, $2 true/false para simular una falla
  export CORREO="$1" PROBAR="$2" URL_API LOC
  export CLAVE=$(az webapp config appsettings list --resource-group $RG --name $API_APP --query "[?name=='ClaveApiRobots'].value" -o tsv)
  export BCCH_USUARIO=$(az webapp config appsettings list --resource-group $RG --name $WEB_APP --query "[?name=='Mercado__UsuarioBancoCentral'].value" -o tsv)
  export BCCH_CLAVE=$(az webapp config appsettings list --resource-group $RG --name $WEB_APP --query "[?name=='Mercado__ClaveBancoCentral'].value" -o tsv)
  if [ -z "$BCCH_USUARIO" ] || [ -z "$BCCH_CLAVE" ]; then
    echo "Faltan las credenciales del Banco Central. Ejecute primero: bash logicapps/crear-bot01.sh credenciales"; exit 1
  fi
  export ID_CONEXION="/subscriptions/$SUB/resourceGroups/$RG/providers/Microsoft.Web/connections/outlook"
  export API_OUTLOOK="/subscriptions/$SUB/providers/Microsoft.Web/locations/$LOC/managedApis/outlook"
  CUERPO=$(mktemp)
  python3 - "$CUERPO" <<'PY'
import json, os, sys
d = json.load(open("logicapps/BOT-01_mercado.json", encoding="utf-8"))["definition"]
body = {"location": os.environ["LOC"], "properties": {"state": "Enabled", "definition": d, "parameters": {
    "$connections": {"value": {"outlook": {"connectionId": os.environ["ID_CONEXION"], "connectionName": "outlook", "id": os.environ["API_OUTLOOK"]}}},
    "urlApi": {"value": os.environ["URL_API"]}, "claveApi": {"value": os.environ["CLAVE"]},
    "bcchUsuario": {"value": os.environ["BCCH_USUARIO"]}, "bcchClave": {"value": os.environ["BCCH_CLAVE"]},
    "correoAlertas": {"value": os.environ["CORREO"]}, "probarAlerta": {"value": os.environ["PROBAR"] == "true"}}}}
json.dump(body, open(sys.argv[1], "w", encoding="utf-8"), ensure_ascii=False)
PY
  az rest --method put --url "$FLUJO?$V" --body @"$CUERPO" -o none
  rm -f "$CUERPO"
}

correo_actual() { az rest --method get --url "$FLUJO?$V" --query "properties.parameters.correoAlertas.value" -o tsv; }

ejecutar() {
  az rest --method post --url "$FLUJO/triggers/Todos_los_dias_a_las_9/run?$V" -o none
  echo "Ejecutando BOT-01..."; sleep 40
  RUN=$(az rest --method get --url "$FLUJO/runs?$V" --query "value[0].name" -o tsv)
  echo "Resultado general: $(az rest --method get --url "$FLUJO/runs/$RUN?$V" --query properties.status -o tsv)"
  az rest --method get --url "$FLUJO/runs/$RUN/actions?$V" --query "sort_by(value, &name)[].{paso:name, estado:properties.status}" -o table
}

case "${1:-}" in
  credenciales)
    read -r -p "Usuario (correo) de la API del Banco Central: " USUARIO
    read -r -s -p "Clave de la API del Banco Central (no se verá al escribir): " CLAVE_BCCH; echo
    [ -n "$USUARIO" ] && [ -n "$CLAVE_BCCH" ] || { echo "Usuario y clave son obligatorios."; exit 1; }
    # se guardan en la web (la usa también el botón «Actualizar» de «Dólar y minerales»); BOT-01 las lee desde ahí
    az webapp config appsettings set --resource-group $RG --name $WEB_APP \
      --settings "Mercado__UsuarioBancoCentral=$USUARIO" "Mercado__ClaveBancoCentral=$CLAVE_BCCH" -o none
    echo "Credenciales del Banco Central guardadas en Azure." ;;
  flujo)
    [ -n "${2:-}" ] || { echo "Indique el correo de alertas: bash logicapps/crear-bot01.sh flujo correo@ejemplo.cl"; exit 1; }
    publicar "$2" false
    echo "BOT-01 actualizado (alertas a $2). Se ejecuta todos los días a las 09:00 (hora de Chile)." ;;
  ejecutar) ejecutar ;;
  probar-alerta)
    CORREO=$(correo_actual)
    publicar "$CORREO" true
    ejecutar
    publicar "$CORREO" false
    echo "Prueba terminada: debería llegar un correo de alerta a $CORREO. BOT-01 quedó otra vez en modo normal." ;;
  *) echo "Uso: bash logicapps/crear-bot01.sh credenciales | flujo <correoAlertas> | ejecutar | probar-alerta"; exit 1 ;;
esac
