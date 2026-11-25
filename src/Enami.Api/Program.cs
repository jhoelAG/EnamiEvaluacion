using Enami.Datos;
using Enami.Dominio.Calculo;
using Enami.Dominio.Servicios;

// API para los robots (Power Automate) y para integraciones.
// Los robots no acceden a la base de datos: solo usan estos endpoints (ver arquitectura, sección 3.6 del informe).
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();

// Misma base de datos que la interfaz web (ConnectionStrings:Enami) y los mismos servicios de dominio.
builder.Services.AddPersistenciaEnami(builder.Configuration);
// cliente para la API oficial del Banco Central (BOT-01)
builder.Services.AddHttpClient("BancoCentral", c => { c.Timeout = TimeSpan.FromSeconds(30); c.DefaultRequestHeaders.UserAgent.ParseAdd("EnamiEvaluacion/1.0"); });

var app = builder.Build();
// La interfaz web crea la base y carga los catálogos; en modo Memoria la API tiene su propio almacén y debe prepararlo.
if (string.Equals(app.Configuration["Persistencia"], "Memoria", StringComparison.OrdinalIgnoreCase))
    app.Services.PrepararBaseDatos(app.Environment.ContentRootPath, cargarPlanillas: false);
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
app.UseHttpsRedirection();

// Clave simple para los robots (cabecera X-Api-Key). En producción se reemplaza por Microsoft Entra ID.
// Fuera de desarrollo la API no arranca sin clave (quedaría abierta) ni, con el flujo de aprobación configurado, sin la clave de firma.
string claveApi = app.Configuration["ClaveApiRobots"] ?? "";
var flujos = app.Configuration.GetSection("Flujos").Get<OpcionesFlujos>() ?? new OpcionesFlujos();
if (!app.Environment.IsDevelopment())
{
    if (string.IsNullOrWhiteSpace(claveApi))
        throw new InvalidOperationException("Falta ClaveApiRobots: sin clave la API aceptaría cualquier llamada.");
    if (flujos.FaltaClaveFirma)
        throw new InvalidOperationException("Falta Flujos:ClaveFirma: sin ella no se pueden verificar las decisiones por correo.");
}
var robots = app.MapGroup("/api").AddEndpointFilter(async (ctx, next) =>
{
    if (claveApi.Length > 0 && !FirmaDecision.Iguales(claveApi, ctx.HttpContext.Request.Headers["X-Api-Key"].ToString()))
        return Results.Unauthorized();
    return await next(ctx);
});

// Plantillas vigentes y sus variables
robots.MapGet("/plantillas", (IRepositorioPlantillas repo) =>
    repo.Listar().Select(p => new { p.Id, p.Nombre, p.IdInstrumento, p.Instrumento, p.Version, Estado = p.Estado.ToString(), p.AprobadaPor }));

// Instrumentos de fomento (los nueve iniciales y los que agregue la Jefatura)
robots.MapGet("/instrumentos", (ServicioCatalogos catalogos) =>
    catalogos.Instrumentos().Select(i => new { i.Id, i.Nombre, i.Descripcion, i.Activo }));

// Calcula una evaluación con la versión vigente de las fórmulas del instrumento (RF-02, RF-03, RF-17)
robots.MapPost("/evaluaciones/calcular", (SolicitudCalculo s, IRepositorioPlantillas repo, IRepositorioParametros parametros) =>
{
    var plantilla = s.IdInstrumento is byte id ? repo.ObtenerVigente(id) : repo.ObtenerVigente(s.Instrumento ?? "");
    if (plantilla is null) return Results.NotFound(new { error = $"No hay fórmulas vigentes para el instrumento «{s.Instrumento ?? s.IdInstrumento?.ToString()}»." });
    var valores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    foreach (var (codigo, p) in parametros.Ultimos()) valores[codigo] = p.Valor;
    foreach (var (codigo, v) in s.Valores ?? []) valores[codigo] = v;
    var r = MotorEvaluacion.Evaluar(plantilla, valores);
    return r.Exitoso
        ? Results.Ok(new { r.Plantilla, r.VersionPlantilla, r.Van, r.Ivan, Dictamen = r.Viable ? "VIABLE" : "NO_VIABLE", r.Inversion, r.Plazo })
        : Results.UnprocessableEntity(new { errores = r.Errores });
});

// BOT-01: registra el valor del día de una variable (dólar, precio del cobre, etc.) (RF-05 y RF-25)
robots.MapPost("/variables/{codigo}", (string codigo, RegistroVariable v, IRepositorioParametros parametros, ServicioAuditoria auditoria) =>
{
    DateTime? fechaValor = DateTime.TryParseExact(v.FechaValor, "dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.None, out var f) ? f : null;
    return RegistrarVariable(codigo, v.Valor, v.Fuente, fechaValor, parametros, auditoria);
});

// BOT-01: consulta la API oficial del Banco Central (dólar observado, cobre, oro y plata) y registra la última observación publicada.
// La consulta la hace la API y no Logic Apps porque el Banco Central responde con tildes en Latin-1, que Logic Apps no acepta;
// así, además, la clave del Banco Central queda solo en la configuración de la API.
robots.MapPost("/bancocentral/{codigo}", async (string codigo, IConfiguration config, IHttpClientFactory clientes,
    IRepositorioParametros parametros, ServicioAuditoria auditoria, CancellationToken ct) =>
{
    if (!BancoCentral.Series.TryGetValue(codigo, out var serie))
        return Results.NotFound(new { error = $"«{codigo}» no tiene serie del Banco Central. Válidas: {string.Join(", ", BancoCentral.Series.Keys)}." });
    string usuario = config["Mercado:UsuarioBancoCentral"] ?? "", clave = config["Mercado:ClaveBancoCentral"] ?? "";
    if (usuario.Length == 0 || clave.Length == 0)
        return Results.Problem("Faltan Mercado:UsuarioBancoCentral y Mercado:ClaveBancoCentral en la configuración de la API.", statusCode: 503);
    string texto;
    try
    {
        using var respuesta = await clientes.CreateClient("BancoCentral").GetAsync(BancoCentral.Consulta(usuario, clave, serie.Serie, DateTime.Today), ct);
        if (!respuesta.IsSuccessStatusCode)
            return Results.Problem($"El Banco Central respondió HTTP {(int)respuesta.StatusCode}.", statusCode: 502);
        texto = BancoCentral.Decodificar(await respuesta.Content.ReadAsByteArrayAsync(ct));
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        return Results.Problem($"No respondió el Banco Central: {ex.GetBaseException().Message}", statusCode: 502);
    }
    if (BancoCentral.LeerUltimo(texto, out var error) is not { } dato)
        return Results.Problem(error, statusCode: 502);
    return RegistrarVariable(codigo, dato.Valor, "API_BCCH", dato.Fecha, parametros, auditoria);
});

robots.MapGet("/variables", (IRepositorioParametros parametros) => parametros.Ultimos().Values);

// BOT-01 (u otro robot) informa que no pudo actualizar algún valor con ninguna de sus fuentes: queda en la auditoría (RNF-02)
robots.MapPost("/robots/incidencias", (IncidenciaRobot i, ServicioAuditoria auditoria) =>
{
    var robot = string.IsNullOrWhiteSpace(i.Robot) ? "ROBOT" : i.Robot.Trim();
    if (robot.Length > 10) robot = robot[..10];
    auditoria.Registrar("ROBOT_FALLA", null, robot: robot, detalle: string.IsNullOrWhiteSpace(i.Detalle) ? "Falla sin detalle" : i.Detalle.Trim());
    return Results.Ok(new { registrada = true });
});

// Valida una expresión (lo usa el editor de fórmulas u otras aplicaciones)
robots.MapPost("/formulas/validar", (ValidarFormula f) =>
{
    var error = MotorFormulas.ValidarSintaxis(f.Expresion);
    return error is null ? Results.Ok(new { valida = true }) : Results.Ok(new { valida = false, error.Mensaje, error.Posicion });
});

// BOT-03: el flujo de aprobación (Logic App) registra aquí la decisión que el aprobador tomó en el correo
// («Aprobar», «Observar» o «Rechazar»). Si hay un siguiente nivel, la API avisa al flujo para que envíe el próximo correo.
robots.MapPost("/evaluaciones/{id:long}/decision", async (long id, DecisionFlujo d, ServicioAprobaciones aprobaciones, INotificadorFlujos notificador,
                                                          ServicioAuditoria auditoria) =>
{
    if (string.IsNullOrWhiteSpace(d.Correo) || string.IsNullOrWhiteSpace(d.Decision))
        return Results.BadRequest(new { error = "Faltan el correo del aprobador o la decisión." });
    try
    {
        // con clave de firma, la decisión debe traer el código firmado de la aprobación pendiente y venir del aprobador asignado
        var pendiente = aprobaciones.Pendiente(id);
        if (!string.IsNullOrWhiteSpace(flujos.ClaveFirma) && pendiente is not null
            && (!string.Equals(pendiente.Correo, d.Correo.Trim(), StringComparison.OrdinalIgnoreCase)
                || !FirmaDecision.Verificar(flujos.ClaveFirma, pendiente.Id, id, pendiente.Correo, d.Token)))
        {
            auditoria.Registrar("DECISION_NO_AUTORIZADA", null, id, $"Decisión por correo rechazada: código inválido o aprobador distinto ({d.Correo})", robot: "BOT-03");
            return Results.Json(new { error = "La decisión no trae un código válido para esta aprobación." }, statusCode: StatusCodes.Status403Forbidden);
        }
        var r = aprobaciones.DecidirPorCorreo(id, d.Correo, d.Decision, d.Comentario);
        var avisos = new List<string>();
        foreach (var e in r.Eventos)
            if (await notificador.EnviarAsync(e) is string aviso) avisos.Add(aviso);
        return Results.Ok(new { estado = r.Estado, mensaje = r.Mensaje, avisos });
    }
    catch (ErrorNegocio ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

// Cadena de aprobación de una evaluación (para consultar desde el flujo o para pruebas)
robots.MapGet("/evaluaciones/{id:long}/aprobaciones", (long id, ServicioAprobaciones aprobaciones) => aprobaciones.Historial(id));

// Punto de salud para el monitoreo (sección 6.1 del informe): responde sin consultar la base de datos.
app.MapGet("/health/live", () => Results.Ok(new { estado = "ok", servicio = "Enami.Api", hora = DateTime.Now })).AllowAnonymous();

app.Run();

// Registro común de BOT-01: respeta los valores fijados por un usuario y la validación de rango.
static IResult RegistrarVariable(string codigo, double valor, string? fuente, DateTime? fechaValor,
    IRepositorioParametros parametros, ServicioAuditoria auditoria)
{
    try
    {
        // un valor que fijó una persona en «Dólar y minerales» (a mano o eligiendo una fecha) no lo reemplaza el robot
        var vigentes = parametros.Ultimos();
        if (vigentes.TryGetValue(codigo, out var actual) && actual.Fijado)
            return Results.Conflict(new { error = $"«{codigo}» está fijado por un usuario en «Dólar y minerales»; no se reemplaza.", vigente = actual });
        // validación de rango: un valor muy distinto del vigente (error de unidad o de la fuente) no se guarda; el robot prueba su respaldo
        if (ValidacionMercado.Revisar(codigo, valor, vigentes.TryGetValue(codigo, out var previo) ? previo.Valor : null) is string motivo)
        {
            auditoria.Registrar("VARIABLE_RECHAZADA", null, robot: "BOT-01", detalle: motivo);
            return Results.UnprocessableEntity(new { error = motivo });
        }
        // fechaValor: día al que corresponde el valor según la fuente (el Banco Central no publica los metales todos los días)
        parametros.Registrar(codigo, valor, string.IsNullOrWhiteSpace(fuente) ? "BOT-01" : fuente, fechaValor);
        auditoria.Registrar("VARIABLE_REGISTRADA", null, robot: "BOT-01", detalle: $"{codigo} = {valor} ({RepositorioParametros.NormalizarFuente(fuente ?? "BOT-01")})");
        return Results.Ok(parametros.Ultimos()[codigo]);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}

/// <summary>Datos para calcular: el instrumento (por clave o por nombre) y los valores de las variables.</summary>
public record SolicitudCalculo(byte? IdInstrumento, string? Instrumento, Dictionary<string, double>? Valores);
/// <summary>FechaValor (dd-MM-yyyy, opcional): día al que corresponde el valor según la fuente.</summary>
public record RegistroVariable(double Valor, string? Fuente, string? FechaValor = null);
/// <summary>Falla de un robot: qué robot y qué no pudo hacer (se registra como ROBOT_FALLA en la auditoría).</summary>
public record IncidenciaRobot(string? Robot, string? Detalle);
public record ValidarFormula(string Expresion);
/// <summary>Decisión que envía el flujo: el correo del aprobador y la opción que eligió en el correo de aprobación.</summary>
/// <summary>Token: el código firmado que la aplicación incluyó en el aviso de esa aprobación (FirmaDecision).</summary>
public record DecisionFlujo(string Correo, string Decision, string? Comentario, string? Token = null);
public partial class Program;
