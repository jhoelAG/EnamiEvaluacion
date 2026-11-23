# Plataforma de Evaluación de Proyectos de Fomento (ENAMI)

Solución del Proyecto de Título (INACAP, TIHI84) para la Gerencia de Fomento de ENAMI, hecha en **.NET 10** con **Blazor**, **ASP.NET Core** y **EF Core sobre SQL Server**.

**Estado: Sprint 5.** Incluye usuarios, inicio de sesión y registro de evaluaciones de proyecto.

Observaciones del profesor guía del 12-11-2026, ya incorporadas:

- en la interfaz, **«solicitud» pasa a llamarse «evaluación de proyecto»** (menú, bandeja, formulario y mensajes; las rutas nuevas son `/evaluaciones`, y las antiguas `/solicitudes` siguen funcionando). Las tablas de la base conservan sus nombres;
- las fórmulas ya no se definen por **tipo de crédito** sino por **instrumento de fomento**: cada uno de los 9 instrumentos tiene sus propias versiones, y la Jefatura puede **agregar instrumentos** en *Instrumentos de fomento* (parten con una copia en borrador de las fórmulas de otro, que se aprueba en el editor);
- el **dólar** (y la libra de cobre) se obtiene **por API** y también se puede **ingresar a mano** en *Dólar y minerales*, junto con el oro y la plata. Vale el último valor registrado y cada cambio queda en la auditoría.

Desde la reunión con ENAMI del 20-10-2026 se agregó:

- el **formulario configurable**: cada plantilla define sus secciones y campos (números, listas y textos) y de dónde se precarga cada uno;
- la **evaluación del Anexo 2** de ENAMI por escenarios de precio: recuperación del fomento, VAN, precio crítico y precio mínimo de retorno. Con los datos del ejemplo de ENAMI da los mismos resultados que su planilla;
- el **presupuesto del Anexo 1** con el catálogo de actividades y costos de la tarificación 2026;

- la recepción de mineral, con el historial de los últimos 6 meses por faena;
- los recursos por faena;
- la búsqueda del productor por nombre;
- los montos de la evaluación en dólares (US$);
- las funciones financieras `VA` y `PAGO` en el editor de fórmulas.

---

## 1. Qué incluye

| Proyecto | Contenido |
|---|---|
| `src/Enami.Dominio` | Núcleo sin dependencias externas: <br>• las **22 entidades** del modelo en 3FN (`Modelo/`): 17 del informe (sin `TIPO_CREDITO`, que se eliminó al pasar las fórmulas a cada instrumento), `RECEPCION_MINERAL` y `RECURSO_FAENA` (Sprint 4), y `DATO_SOLICITUD`, `LABOR_TARIFADA` y `PRESUPUESTO_SOLICITUD` (Sprint 5); <br>• el **motor de fórmulas** editable y el cálculo del **VAN e IVAN** (`Calculo/`); <br>• el **lector de planillas** y las reglas de calidad (`Importacion/`); <br>• los **servicios** de usuarios, fichas, elegibilidad, solicitudes, carga de planillas, auditoría y carga inicial (`Servicios/`). |
| `src/Enami.Datos` | **EF Core + SQL Server**: <br>• `EnamiDbContext` con las 22 tablas, claves, índices únicos y restricciones CHECK; <br>• el registro de servicios (`AddPersistenciaEnami`); <br>• el hash de contraseñas con el `PasswordHasher` de ASP.NET Core Identity. |
| `src/Enami.Web` | Interfaz **Blazor**: <br>• inicio de sesión con expiración por inactividad y menú según el rol; <br>• evaluaciones de proyecto (bandeja y formulario); <br>• simulador de VAN; <br>• instrumentos de fomento; <br>• editor de fórmulas; <br>• dólar y minerales (API y edición manual); <br>• variables; <br>• usuarios; <br>• carga de planillas; <br>• auditoría. |
| `src/Enami.Api` | **API para los robots** de Power Automate. Usa la misma base de datos que la interfaz. |
| `tools/Enami.Evidencias` | Programa que ejecuta los casos de validación del informe (sección 5.4) y guarda el resultado esperado y el obtenido en un JSON. |
| `tests/Enami.Pruebas` | **116 pruebas xUnit**: motor de fórmulas, VAN, Anexo 2 contra la planilla de ENAMI, presupuesto, versionado, importación, trazabilidad y las historias de los sprints 3 a 5. |

## 2. Requisitos

- **.NET 10 SDK**.
- **Visual Studio 2026** con la carga de trabajo «Desarrollo de ASP.NET y web». Esa carga trae **SQL Server Express LocalDB**, que es la base que usa la aplicación por defecto.

## 3. Cómo ejecutarlo

1. Abra `EnamiEvaluacion.sln`. Visual Studio descarga los paquetes NuGet.
2. Marque **Enami.Web** como proyecto de inicio. Agregue **Enami.Api** solo si va a probar los robots.
3. Presione **F5**. La primera vez la aplicación hace lo siguiente:
   - crea la base `EnamiEvaluacion` con las 22 tablas (en una base anterior, pasa las fórmulas de cada tipo de crédito a sus instrumentos y elimina `TIPO_CREDITO`);
   - carga los catálogos: 5 roles, 11 oficinas, 9 instrumentos de fomento y 9 causales;
   - crea el administrador y la versión 1 de las fórmulas de cada instrumento;
   - consulta el dólar observado y la libra de cobre en la API de indicadores (y luego cada hora, si aún no hay valores del día);
   - si encuentra la carpeta `Proyecto de titulo` junto a `EnamiEvaluacion` en el Escritorio, carga las cuatro planillas de ENAMI: productores, faenas, padrones y recepción de minerales. Esto toma unos 15 segundos en modo Memoria y alrededor de un minuto en SQL Server.
4. Se abre `http://localhost:5087`. Ingrese con estas cuentas:

| Correo | Rol | Contraseña |
|---|---|---|
| `admin@enami.cl` | Administrador | `Enami.2026` |
| `ingeniero.copiapo@enami.cl` | Ingeniero de Fomento (oficina de Copiapó) | `Enami.2026` |
| `jefe.copiapo@enami.cl` | Jefe de Oficina (oficina de Copiapó) | `Enami.2026` |
| `subgerente@enami.cl` | Subgerente | `Enami.2026` |
| `jefatura@enami.cl` | Jefatura de Fomento | `Enami.2026` |

Las cuentas de demostración se crean solo en el entorno de desarrollo, con `Semilla:UsuariosDemostracion` en `appsettings.Development.json`. **Cambie `Semilla:ClaveAdministrador` antes de publicar el piloto.**

**Base de prueba con datos completos:** las planillas de ENAMI vienen anonimizadas y con campos vacíos. En desarrollo (`Semilla:CompletarDatosPrueba` en `appsettings.Development.json`), al iniciar se completan con datos recreados pero coherentes:

- oficina según la comuna;
- comuna del productor según sus faenas;
- distancia según faenas cercanas;
- padrón con vencimiento;
- nombres, razones sociales y representantes ficticios;
- recursos calculados con las entregas de la faena (fuente «Dato de prueba»).

Solo se llena lo vacío y el resultado es siempre el mismo. También hay un botón en *Cargar planillas*. **No active esta opción con datos reales.**

**Sin SQL Server:** en `src/Enami.Web/appsettings.json` cambie `"Persistencia": "SqlServer"` por `"Memoria"`. Todo funciona igual, pero los datos se pierden al cerrar la aplicación.

**Otro servidor SQL:** cambie `ConnectionStrings:Enami` en los `appsettings.json` de Enami.Web y de Enami.Api.

**Planillas en otra carpeta:** cambie `Semilla:RutaPlanillas`, o súbalas desde *Cargar planillas* con el usuario administrador.

```bash
dotnet run --project tools/Enami.Evidencias -- "../Proyecto de titulo" evidencias.json   # casos de validación del informe
dotnet test                              # 116 pruebas
dotnet run --project src/Enami.Web       # http://localhost:5087
dotnet run --project src/Enami.Api       # http://localhost:5151
```

## 4. Qué hace cada rol

| Rol | Puede |
|---|---|
| **Ingeniero de Fomento** | Buscar al productor por RUT, por nombre (también por el nombre de una de sus faenas) o la faena por código, y ver sus datos precargados (HU-04). <br>Ver las entregas de la faena en los últimos 6 meses del reporte de recepción: lotes, toneladas secas y leyes por mes. El promedio mensual y la ley de cobre se precargan en los datos del proyecto. <br>Registrar los recursos de la faena (medidos, indicados e inferidos) con sus leyes y la fuente del dato. <br>Ver la elegibilidad de cada faena con su motivo: productor moroso, faena inactiva, padrón vencido o faena sin oficina (HU-05). <br>Registrar la solicitud con instrumento, monto, plazo y datos del proyecto, guardarla como **borrador** y retomarla (HU-06). Al guardar con los datos completos se calcula el VAN y queda guardado en `RESULTADO_EVALUACION` y `VALOR_ENTRADA`. |
| **Jefe de Oficina** | Ver las solicitudes de las faenas de su oficina. <br>Registrar recursos de faenas. |
| **Subgerente** | Ver todas las solicitudes. |
| **Jefatura de Fomento** | Ver todas las solicitudes. <br>Agregar, renombrar o desactivar **instrumentos de fomento**. <br>Consultar la API o **ingresar a mano** el dólar y los precios de los minerales. <br>Editar las fórmulas de cada instrumento en una versión nueva y **aprobarla** (solo este rol activa versiones; el núcleo también lo verifica). La creación y la aprobación de cada versión quedan en la auditoría (`VERSION_CREADA`, `VERSION_APROBADA`). <br>Ver la auditoría. |
| **Administrador** | Crear, editar, habilitar y deshabilitar usuarios con su rol y oficina (HU-01). <br>Cargar las planillas, incluida la recepción de minerales (los lotes ya cargados se omiten, así que se puede subir el reporte de cada semana). <br>Ver la auditoría. |

Sobre la sesión (HU-02):

- Ninguna página se abre sin iniciar sesión.
- La sesión se cierra tras **30 minutos sin actividad** (`Seguridad:MinutosInactividad`).
- Cada minuto la aplicación revisa que el usuario siga habilitado; si el administrador lo deshabilita, pierde el acceso aunque tenga la página abierta.

## 5. Base de datos

- Las tablas, columnas y restricciones son las del informe: sección 3.3 y Anexo A.
- Respecto del informe se agregaron dos columnas:
  - `USUARIO.clave_hash`: hash de la contraseña;
  - `VARIABLE_CALCULO.valor_referencia`: valor que se propone en el formulario y en el simulador.

  Ambas dependen solo de la clave primaria, así que el modelo sigue en 3FN.
- Las variables de mantenedor y de mercado (dólar, precio del cobre, tasa de descuento, etc.) se leen del último registro de `PARAMETRO_ECONOMICO`.
- Tablas agregadas en el Sprint 4 (hay que sumarlas al Anexo A del informe):
  - `RECEPCION_MINERAL`: un lote por fila, con faena, fecha de cierre, agencia, producto, peso seco y leyes de cobre, oro y plata. El número de lote es único.
  - `RECURSO_FAENA`: recursos de la faena por categoría (medido, indicado o inferido), con toneladas, leyes, fuente, usuario y fecha. Hay un registro por faena y categoría.
- Tablas agregadas en el Sprint 5:
  - `DATO_SOLICITUD`: valor de cada campo del formulario de una solicitud, numérico o de texto. Así un borrador incompleto conserva lo escrito.
  - `LABOR_TARIFADA`: catálogo de actividades del presupuesto, con unidad, costo medio y porcentaje que financia ENAMI.
  - `PRESUPUESTO_SOLICITUD`: actividades del Anexo 1 de cada solicitud.

  Además, `PLANTILLA_CALCULO` tiene `modo_calculo`, y `VARIABLE_CALCULO` tiene `seccion`, `orden`, `tipo`, `fuente` y `opciones`.
- **Bases creadas en un sprint anterior:** al iniciar, la aplicación agrega las tablas y columnas nuevas sin borrar datos, registra el Anexo 2 como versión nueva del tipo «Reconocimiento de recursos y reservas» y carga la recepción si encuentra la planilla. Las fórmulas de ejemplo de los otros tipos siguen como estaban; para tenerlas en US$ y con precarga, borre la base y vuelva a ejecutar.

### Formulario configurable y Anexo 2 (Sprint 5)

En **Editor de fórmulas**, cada versión de plantilla tiene dos pestañas:

- **Campos del formulario y variables:** para cada campo se define la sección, el orden, el tipo (número, lista, texto o texto largo), el origen (formulario, mantenedor o robot), la fuente de precarga, las opciones de la lista y los límites. Lo que se cambia aquí cambia el formulario de **Nueva solicitud**.
- **Fórmulas:** el cálculo, con las mismas reglas de versiones y aprobación de la Jefatura.

Al elegir el productor y la faena, el formulario se completa solo con lo que ya está en las planillas:

- **ficha del productor:** domicilio, comuna y región, teléfono, correo y representante legal;
- **ficha de la faena:** ubicación, comuna y región, coordenadas UTM, mano de obra y distancia al poder de compra;
- **recepción de los últimos 6 meses:** producción y leyes de la faena, meses con entregas, y producción y leyes de todas las faenas del productor;
- **recursos de la faena:** toneladas por categoría y leyes ponderadas;
- **profesional de apoyo:** el usuario que registra la solicitud;
- **totales del presupuesto:** se calculan siempre y no se editan.

El ingeniero puede corregir cualquier valor precargado. Los campos marcados con * son los que usan las fórmulas y son necesarios para calcular; el resto es informativo.

Las planillas que entregó ENAMI vienen anonimizadas: no traen domicilio, teléfono, correo ni coordenadas. Con los reportes reales se cargan solos (columnas «Domicilio Particular», «Fono…», «Correo…», «Norte» y «Este»). En la base de prueba los completa `CompletarDatosPrueba` con datos ficticios (correos con el dominio reservado `ejemplo.cl`).

El tipo **Reconocimiento de recursos y reservas** usa la plantilla **Anexo 2** (modo *por escenarios de precio*):

1. Las fórmulas se calculan para 8 precios alrededor del precio proyectado, con un paso de 20 (`PasoPrecio`), y para el precio proyectado. El identificador `Precio` es el precio de cada fila.
2. La **recuperación** y el **VAN** son los del precio proyectado.
3. El **precio crítico** es donde la utilidad vale cero, y el **precio mínimo de retorno**, donde la capacidad de pago cubre el financiamiento. Ambos se calculan con regresión lineal, igual que `INTERSECCION.EJE` en la planilla.
4. El dictamen es **viable** si la recuperación cubre lo que financia ENAMI y el VAN no es negativo.
5. La tarifa de compra de ENAMI (oro y cobre por flotación) está escrita como fórmulas. Sus parámetros (cargos, descuentos y recuperaciones) son variables de mantenedor.

El **presupuesto (Anexo 1)** se arma con las actividades del catálogo, que trae los costos de la tarificación ENAMI 2026:

- **Reconocimiento:** ENAMI aporta el porcentaje indicado como capital de riesgo.
- **Crédito para el desarrollo:** ENAMI presta la mitad del costo y el beneficiario lo devuelve.
- **Aporte propio:** lo paga el beneficiario.

Se suma el impuesto de timbres. El monto de la solicitud pasa a ser el capital de riesgo más el crédito.

Diferencias con la planilla de ENAMI, por validar:

- solo están las tarifas de oro y cobre por flotación;
- los costos «Otros» y «Maquila» se suman por tonelada;
- el precio proyectado es un parámetro, en vez de calcularse con los escenarios pesimistas ponderados por año.

### Montos en dólares

Como en la planilla de evaluación de ENAMI, el monto solicitado, el VAN y el flujo de caja están en **US$**. El dólar observado (`Dolar`) solo se usa para mostrar el equivalente en pesos como referencia.

### Funciones del editor de fórmulas

Además de `SI`, `MIN`, `MAX`, `ABS`, `REDONDEAR`, `RAIZ`, `POTENCIA`, `Y` y `O`, el editor acepta:

| Función | Igual que en Excel |
|---|---|
| `VA(tasa; nper; pago; [vf]; [tipo])` | `VA` (valor actual); se usa para la capacidad de pago del Anexo 2. |
| `PAGO(tasa; nper; va; [vf]; [tipo])` | `PAGO` (cuota periódica). |

Para trabajar con migraciones en lugar de `EnsureCreated`:

```bash
dotnet tool install --global dotnet-ef
cd src/Enami.Datos
dotnet ef migrations add Inicial
dotnet ef database update
```

Para empezar de cero, borre la base `EnamiEvaluacion` desde el *Explorador de objetos de SQL Server* de Visual Studio y vuelva a ejecutar.

## 6. API para los robots (Power Automate)

Todas las llamadas llevan la cabecera `X-Api-Key`; la clave se configura en `ClaveApiRobots`. Hay ejemplos en `src/Enami.Api/Enami.Api.http`.

| Método | Ruta | Uso |
|---|---|---|
| `POST` | `/api/variables/{codigo}` | BOT-01 registra el valor del día; queda en `PARAMETRO_ECONOMICO` y en la auditoría. |
| `GET` | `/api/variables` | Últimos valores registrados. |
| `GET` | `/api/instrumentos` | Instrumentos de fomento (los nueve iniciales y los que agregue la Jefatura). |
| `POST` | `/api/evaluaciones/calcular` | Calcula el VAN con la versión vigente de las fórmulas del instrumento (`idInstrumento` o `instrumento`). |
| `POST` | `/api/formulas/validar` | Valida una expresión. |
| `POST` | `/api/evaluaciones/{id}/decision` | El flujo de aprobación (BOT-03) registra la decisión del correo: `{ "correo", "decision": "Aprobar" \| "Observar" \| "Rechazar" }`. |
| `GET` | `/api/evaluaciones/{id}/aprobaciones` | Cadena de aprobación de una evaluación. |

## 7. Publicar en Azure (piloto)

1. **Base de datos:** cree una *Azure SQL Database* con la oferta gratuita (sin servidor). En *Redes* active «Permitir que los servicios y recursos de Azure accedan a este servidor». Copie la cadena de conexión ADO.NET.
2. **Aplicación web:** cree un *App Service* con .NET 10 (Windows o Linux). En *Configuración → Configuración general* active **Web Sockets** y **Afinidad de ARR** (Blazor Server los necesita).
3. **Variables de la aplicación** (*Variables de entorno* del App Service). Así la cadena y la clave no quedan en el código:

| Nombre | Valor |
|---|---|
| `ConnectionStrings__Enami` | la cadena de Azure SQL, con la contraseña |
| `Semilla__ClaveAdministrador` | una clave nueva y segura |
| `Semilla__UsuariosDemostracion` | `true` solo si quiere las cuentas de prueba en el piloto |
| `Flujos__UrlAprobacion` | la URL del desencadenador HTTP de la Logic App de aprobación (BOT-03); sin ella, la aprobación se hace solo en la aplicación |
| `Flujos__UrlWeb` y `Flujos__UrlApi` | las direcciones públicas de la web y de la API (para el enlace del correo y para registrar la decisión) |

4. **Publicar:** en Visual Studio, clic derecho en **Enami.Web** → *Publicar* → *Azure* → *Azure App Service* → elija el App Service → *Publicar*.
5. Abra la URL del App Service, ingrese como administrador y cargue las planillas en *Cargar planillas*.

La primera vez la aplicación crea las 22 tablas en Azure SQL. Si la base gratuita estaba en pausa, espera hasta un minuto y medio a que despierte. La API se publica igual en otro App Service (puede usar el mismo plan) con las mismas variables más `ClaveApiRobots`.

## 8. Pendiente y supuestos por validar con ENAMI

- **Fórmulas:** son las de ejemplo. Hay que reemplazarlas por las de la planilla real de evaluación y comparar los resultados con evaluaciones históricas.
- **Instrumentos de fomento:** los 9 instrumentos iniciales se tomaron de los publicados por ENAMI. Hay que confirmarlos; la Jefatura puede renombrarlos, desactivarlos o agregar otros en *Instrumentos de fomento*.
- **Dólar y minerales:** el dólar se consulta, en orden, en el Banco Central (solo si se configuran `Mercado:UsuarioBancoCentral` y `Mercado:ClaveBancoCentral`, con registro gratuito en si3.bcentral.cl; guárdelos con *Administrar secretos de usuario* en Visual Studio, no en `appsettings.json`), en `https://mindicador.cl/api` y, si ninguno responde, en un tipo de cambio de mercado (`open.er-api.com`, fuente `API_EXTERNA`). La libra de cobre viene de mindicador.cl. Si una conexión segura se corta, se reintenta con TLS 1.2. `Mercado:ConsultaAutomatica` = `false` desactiva la consulta automática. En *Dólar y minerales* se puede **buscar el dólar o el cobre de una fecha** (si ese día no hubo valor, se usa el del último día hábil anterior) y usarlo en las evaluaciones; ese valor, igual que uno ingresado a mano, queda **fijado** (columna `id_usuario` de `PARAMETRO_ECONOMICO`) y ni la consulta automática ni BOT-01 lo reemplazan hasta que se presiona «Volver al valor de hoy». El oro y la plata no están en estas API: los registra BOT-01 o se ingresan a mano en *Dólar y minerales*. Siempre vale el último valor registrado.
- **Nivel de cumplimiento:** el de la ficha se interpretó así: «Alto» y «Medio» = al día, «Bajo» = moroso, sin dato = al día.
- **Faenas de subgerencias:** las faenas asignadas a una subgerencia (por ejemplo, «Subg. Fomento y Minería Centro») quedan con esa unidad como oficina.
- **Recepción:** los RUT y nombres del reporte de recepción vienen abreviados, así que los lotes se asocian a la faena por el código de mina. 3.306 de 66.550 lotes tienen un código que no está en la ficha de faenas y quedan rechazados (se ven en *Cargar planillas*).
- **Recursos:** la ficha de faena no trae los recursos medidos, indicados e inferidos, así que por ahora se registran a mano. Falta que ENAMI indique el reporte de origen.
- **Pendiente:**
  - los escenarios de precio por año (para calcular el precio proyectado);
  - las demás tarifas de compra;
  - la matriz de puntuación técnica;
  - el dictamen en PDF (BOT-04). La aprobación en tres niveles (BOT-03) ya está: el ingeniero envía la evaluación a revisión, la decide el Jefe de Oficina de la faena y luego el Subgerente (aprobar, observar o rechazar con causal), en la aplicación o en el correo de aprobación de la Logic App, que registra la decisión en `/api/evaluaciones/{id}/decision`. Los correos de los usuarios aprobadores deben ser buzones reales para recibir el correo.
