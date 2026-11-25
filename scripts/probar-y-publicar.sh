#!/bin/bash
# Prueba, guarda en Git y publica la web y la API en Azure.
# Uso (desde la carpeta del proyecto):  bash scripts/probar-y-publicar.sh "mensaje del commit"
# Se detiene en el primer error: si las pruebas fallan, no guarda ni publica nada.
set -euo pipefail
cd "$(dirname "$0")/.."
eval "$(/opt/homebrew/bin/brew shellenv zsh 2>/dev/null || true)"
RG=rg-enami-jhoel
MENSAJE="${1:-}"

echo "== 1/4 Pruebas =="
dotnet test

echo "== 2/4 Git =="
if [ -n "$(git status --porcelain)" ]; then
  if [ -z "$MENSAJE" ]; then echo "Hay cambios sin guardar: indique el mensaje del commit entre comillas."; exit 1; fi
  git add -A
  git commit -m "$MENSAJE"
fi
git push

publicar() {  # $1 proyecto, $2 aplicación de Azure
  local salida=/tmp/publicar-$2 zip=/tmp/publicar-$2.zip
  rm -rf "$salida" "$zip"
  dotnet publish "src/$1/$1.csproj" -c Release -o "$salida"
  (cd "$salida" && zip -qr "$zip" .)
  az webapp deploy --resource-group $RG --name "$2" --src-path "$zip" --type zip --query "properties.status" -o tsv
}
echo "== 3/4 Publicar la web =="
publicar Enami.Web enami-jhoel
echo "== 4/4 Publicar la API =="
publicar Enami.Api enami-jhoel-api
echo "Listo: pruebas aprobadas, cambios en GitHub y web y API publicadas."
