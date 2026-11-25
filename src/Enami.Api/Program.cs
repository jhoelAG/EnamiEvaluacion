using Enami.Datos;
using Enami.Dominio.Calculo;
using Enami.Dominio.Servicios;

// API para los robots (Power Automate) y para integraciones.
// Los robots no acceden a la base de datos: solo usan estos endpoints (ver arquitectura, sección 3.6 del informe).
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();

// Misma base de datos que la interfaz web (ConnectionStrings:Enami) y los mismos servicios de dominio.
builder.Services.AddPersistenciaEnami(builder.Configuration);

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
    try
    {
        // un valor que fijó una persona en «Dólar y minerales» (a mano o eligiendo una fecha) no lo reemplaza el robot
        if (parametros.Ultimos().TryGetValue(codigo, out var actual) && actual.Fijado)
            return Results.Conflict(new { error = $"«{codigo}» está fijado por un usuario en «Dólar y minerales»; no se reemplaza.", vigente = actual });
        parametros.Registrar(codigo, v.Valor, string.IsNullOrWhiteSpace(v.Fuente) ? "BOT-01" : v.Fuente);
        auditoria.Registrar("VARIABLE_REGISTRADA", null, robot: "BOT-01", detalle: $"{codigo} = {v.Valor} ({RepositorioParametros.NormalizarFuente(v.Fuente ?? "BOT-01")})");
        return Results.Ok(parametros.Ultimos()[codigo]);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

robots.MapGet("/variables", (IRepositorioParametros parametros) => parametros.Ultimos().Values);

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
    try
    {
        // con clave de firma, la decisión debe traer el código firmado de la aprobación pendiente y venir del aprobador asignado
        var pendiente = aprobaciones.Pendiente(id);
        if (!string.IsNullOrWhiteSpace(flujos.ClaveFirma) && pendiente is not null
            && (!string.Equals(pendiente.Correo, d.Correo?.Trim(), StringComparison.OrdinalIgnoreCase)
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

app.Run();

/// <summary>Datos para calcular: el instrumento (por clave o por nombre) y los valores de las variables.</summary>
public record SolicitudCalculo(byte? IdInstrumento, string? Instrumento, Dictionary<string, double>? Valores);
public record RegistroVariable(double Valor, string? Fuente);
public record ValidarFormula(string Expresion);
/// <summary>Decisión que envía el flujo: el correo del aprobador y la opción que eligió en el correo de aprobación.</summary>
/// <summary>Token: el código firmado que la aplicación incluyó en el aviso de esa aprobación (FirmaDecision).</summary>
public record DecisionFlujo(string Correo, string Decision, string? Comentario, string? Token = null);
public partial class Program;
