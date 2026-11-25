// Prueba de carga (plan de pruebas TP-04, RNF-04) con k6: simula a los usuarios de las 11 oficinas entrando al sistema.
// Sube hasta 72 usuarios simultáneos durante 2 minutos sobre la pantalla de ingreso y el punto de salud.
// Umbrales: 95 % de las respuestas en menos de 2 s y menos de 1 % de errores (SLA-01 y SLA-02 del informe).
import http from "k6/http";
import { check, sleep } from "k6";

const URL = __ENV.URL_WEB;

export const options = {
  stages: [
    { duration: "30s", target: 72 },
    { duration: "1m", target: 72 },
    { duration: "30s", target: 0 },
  ],
  thresholds: {
    http_req_duration: ["p(95)<2000"],
    http_req_failed: ["rate<0.01"],
  },
};

export default function () {
  const salud = http.get(`${URL}/health/live`);
  check(salud, { "salud responde 200": (r) => r.status === 200 });
  const ingreso = http.get(`${URL}/cuenta/ingresar`);
  check(ingreso, { "ingreso responde 200": (r) => r.status === 200 });
  sleep(1);
}

export function handleSummary(datos) {
  return { "resultado-carga.json": JSON.stringify(datos, null, 2) };
}
