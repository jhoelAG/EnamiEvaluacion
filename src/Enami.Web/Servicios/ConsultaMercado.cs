using System.Globalization;
using System.Net.Security;
using System.Security.Authentication;
using System.Text.Json;
using Enami.Dominio.Calculo;
using Enami.Dominio.Servicios;
using Microsoft.Extensions.Options;

namespace Enami.Web.Servicios;

/// <summary>Opciones de la sección «Mercado» de appsettings.json (todas tienen valor por defecto).</summary>
public class OpcionesMercado
{
    /// <summary>API pública de indicadores de Chile: dólar observado (Banco Central) y libra de cobre (COCHILCO).</summary>
    public string UrlIndicadores { get; set; } = "https://mindicador.cl/api";
    /// <summary>Respaldo para el dólar, sin clave: tipo de cambio de mercado USD/CLP (se registra como API_EXTERNA).</summary>
    public string UrlDolarAlternativo { get; set; } = "https://open.er-api.com/v6/latest/USD";
    /// <summary>
    /// API oficial del Banco Central (Base de Datos Estadísticos). Se usa primero si se configuran usuario y clave
    /// (registro gratuito en si3.bcentral.cl). No los guarde en appsettings.json: use secretos de usuario o variables de entorno.
    /// </summary>
    public string UrlBancoCentral { get; set; } = "https://si3.bcentral.cl/SieteRestWS/SieteRestWS.ashx";
    public string? UsuarioBancoCentral { get; set; }
    public string? ClaveBancoCentral { get; set; }
    /// <summary>Serie del dólar observado en la Base de Datos Estadísticos del Banco Central.</summary>
    public string SerieDolarBancoCentral { get; set; } = "F073.TCO.PRE.Z.D";
    /// <summary>Si es verdadero, la aplicación consulta al iniciar y luego cada hora, si aún no hay un valor de API del día.</summary>
    public bool ConsultaAutomatica { get; set; } = true;
    public int SegundosEspera { get; set; } = 15;
}

/// <summary>Valor leído de una API para una variable de las fórmulas.</summary>
public record ValorConsultado(string Codigo, string Nombre, double Valor, DateTime FechaApi, string Fuente, string Origen);

/// <summary>
/// Resultado de una consulta: los valores registrados, las variables que no se pudieron obtener (<c>Errores</c>)
/// y las fuentes que fallaron aunque otra entregó el dato (<c>Notas</c>).
/// </summary>
public record ResultadoConsultaMercado(IReadOnlyList<ValorConsultado> Registrados, IReadOnlyList<string> Errores, IReadOnlyList<string> Notas);

/// <summary>
/// Consulta el dólar y el precio del cobre por API y los registra en PARAMETRO_ECONOMICO (observación del profesor guía:
/// el dólar se obtiene por API, pero también se puede editar a mano en «Dólar y minerales»).
/// Para el dólar prueba, en orden: el Banco Central (si hay credenciales), mindicador.cl y un tipo de cambio de mercado.
/// Si una conexión segura se corta, reintenta con TLS 1.2, porque algunas redes o antivirus cortan TLS 1.3.
/// El oro y la plata no están en estas API: los registra BOT-01 desde COCHILCO o se ingresan a mano.
/// </summary>
public class ConsultaMercado(HttpClient http, IRepositorioParametros parametros, ServicioAuditoria auditoria, IOptions<OpcionesMercado> opciones)
{
    private const string Dolar = "Dolar", Cobre = "PrecioCuLb";

    /// <summary>Variables que se obtienen por API, con su nombre para mostrar.</summary>
    public static readonly (string Codigo, string Nombre)[] Indicadores =
    [
        (Dolar, "Dólar observado"),
        (Cobre, "Precio de la libra de cobre"),
    ];

    private static readonly Lazy<HttpClient> ClienteTls12 = new(() => new HttpClient(new SocketsHttpHandler
    {
        SslOptions = new SslClientAuthenticationOptions { EnabledSslProtocols = SslProtocols.Tls12 },
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    }));

    private OpcionesMercado O => opciones.Value;

    /// <summary>
    /// Consulta los valores del día y los registra como valores automáticos (no fijados). <paramref name="idUsuario"/> es nulo
    /// cuando la consulta es automática; si <paramref name="respetarFijados"/> es verdadero, no reemplaza los valores que fijó
    /// una persona (a mano o eligiendo una fecha). Con <paramref name="soloCodigo"/> se registra solo esa variable.
    /// </summary>
    public async Task<ResultadoConsultaMercado> ConsultarYRegistrarAsync(int? idUsuario, bool respetarFijados = false, string? soloCodigo = null,
                                                                       CancellationToken ct = default)
    {
        var obtenidos = new Dictionary<string, ValorConsultado>(StringComparer.OrdinalIgnoreCase);
        var notas = new List<string>();
        var ultimos = parametros.Ultimos();
        var omitir = Indicadores.Select(i => i.Codigo)
            .Where(c => (soloCodigo is not null && !c.Equals(soloCodigo, StringComparison.OrdinalIgnoreCase))
                        || (respetarFijados && ultimos.TryGetValue(c, out var u) && u.Fijado))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 1. Banco Central (fuente oficial del dólar observado), solo si se configuraron las credenciales
        if (!string.IsNullOrWhiteSpace(O.UsuarioBancoCentral) && !string.IsNullOrWhiteSpace(O.ClaveBancoCentral))
        {
            var hoy = DateTime.Today;
            string url = $"{O.UrlBancoCentral}?user={Uri.EscapeDataString(O.UsuarioBancoCentral)}&pass={Uri.EscapeDataString(O.ClaveBancoCentral)}" +
                         $"&firstdate={hoy.AddDays(-10):yyyy-MM-dd}&lastdate={hoy:yyyy-MM-dd}&timeseries={O.SerieDolarBancoCentral}&function=GetSeries";
            using var doc = await LeerJsonAsync(url, "Banco Central", notas, ct);
            if (doc is not null && LeerBancoCentral(doc.RootElement) is { } bc)
                obtenidos[Dolar] = new(Dolar, "Dólar observado", bc.Valor, bc.Fecha, "API_BCCH", "Banco Central de Chile");
            else if (doc is not null) notas.Add("El Banco Central no entregó el dólar observado de los últimos 10 días.");
        }

        // 2. mindicador.cl: dólar observado (Banco Central) y libra de cobre (COCHILCO)
        using (var doc = await LeerJsonAsync(O.UrlIndicadores, "mindicador.cl", notas, ct))
        {
            if (doc is not null)
            {
                if (!obtenidos.ContainsKey(Dolar) && LeerIndicador(doc.RootElement, "dolar") is { } d)
                    obtenidos[Dolar] = new(Dolar, "Dólar observado", d.Valor, d.Fecha, "API_BCCH", "mindicador.cl (Banco Central)");
                if (LeerIndicador(doc.RootElement, "libra_cobre") is { } c)
                    obtenidos[Cobre] = new(Cobre, "Precio de la libra de cobre", c.Valor, c.Fecha, "RPA_COCHILCO", "mindicador.cl (COCHILCO)");
            }
        }

        // 3. Respaldo del dólar: tipo de cambio de mercado
        if (!obtenidos.ContainsKey(Dolar) && !string.IsNullOrWhiteSpace(O.UrlDolarAlternativo))
        {
            using var doc = await LeerJsonAsync(O.UrlDolarAlternativo, "tipo de cambio de mercado", notas, ct);
            if (doc is not null && doc.RootElement.TryGetProperty("rates", out var tasas) && tasas.TryGetProperty("CLP", out var clp)
                && clp.TryGetDouble(out var valor) && valor > 0)
            {
                var fecha = doc.RootElement.TryGetProperty("time_last_update_unix", out var t) && t.TryGetInt64(out var unix)
                    ? DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime : DateTime.Now;
                obtenidos[Dolar] = new(Dolar, "Dólar (tipo de cambio de mercado)", Math.Round(valor, 2), fecha, "API_EXTERNA", "open.er-api.com");
            }
        }

        var registrados = new List<ValorConsultado>();
        foreach (var v in obtenidos.Values.Where(x => !omitir.Contains(x.Codigo)))
        {
            parametros.Registrar(v.Codigo, v.Valor, v.Fuente, v.FechaApi, idUsuario: null);
            auditoria.Registrar("VARIABLE_REGISTRADA", idUsuario, robot: idUsuario is null ? "BOT-01" : null,
                detalle: $"{v.Codigo} = {v.Valor.ToString(CultureInfo.InvariantCulture)} ({v.Fuente}, {v.Origen}, valor del {v.FechaApi:dd-MM-yyyy})");
            registrados.Add(v);
        }
        var errores = Indicadores.Where(i => !omitir.Contains(i.Codigo) && !obtenidos.ContainsKey(i.Codigo))
            .Select(i => $"No se pudo obtener «{i.Nombre}» de ninguna API; se mantiene el último valor registrado y se puede ingresar a mano.").ToList();
        return new ResultadoConsultaMercado(registrados, errores, notas);
    }

    /// <summary>Indica si cada variable que se consulta ya tiene un valor de una API registrado hoy, o un valor fijado por una persona.</summary>
    public bool ActualizadoHoy()
    {
        var ultimos = parametros.Ultimos();
        return Indicadores.All(i => (ultimos.TryGetValue(i.Codigo, out var u) && u.Fijado)
                                    || parametros.Historial(i.Codigo, 5).Any(v => v.Fecha.Date == DateTime.Today && v.Fuente != "MANUAL"));
    }

    /// <summary>
    /// Busca el valor de una variable (dólar o libra de cobre) en una fecha, sin registrarlo. Si ese día no hubo valor
    /// (fin de semana o feriado), entrega el del último día hábil anterior; su fecha viene en <see cref="ValorConsultado.FechaApi"/>.
    /// </summary>
    public async Task<(ValorConsultado? Valor, IReadOnlyList<string> Notas)> ConsultarFechaAsync(string codigo, DateTime fecha, CancellationToken ct = default)
    {
        var notas = new List<string>();
        fecha = fecha.Date;
        if (fecha > DateTime.Today) return (null, new List<string> { "La fecha no puede ser posterior a hoy." });
        var indicador = Indicadores.FirstOrDefault(i => i.Codigo.Equals(codigo, StringComparison.OrdinalIgnoreCase));
        if (indicador.Codigo is null) return (null, new List<string> { "Solo el dólar y la libra de cobre se pueden buscar por fecha." });

        // 1. Banco Central (dólar observado), si hay credenciales
        if (indicador.Codigo == Dolar && !string.IsNullOrWhiteSpace(O.UsuarioBancoCentral) && !string.IsNullOrWhiteSpace(O.ClaveBancoCentral))
        {
            string url = $"{O.UrlBancoCentral}?user={Uri.EscapeDataString(O.UsuarioBancoCentral)}&pass={Uri.EscapeDataString(O.ClaveBancoCentral)}" +
                         $"&firstdate={fecha.AddDays(-10):yyyy-MM-dd}&lastdate={fecha:yyyy-MM-dd}&timeseries={O.SerieDolarBancoCentral}&function=GetSeries";
            using var doc = await LeerJsonAsync(url, "Banco Central", notas, ct);
            if (doc is not null && LeerBancoCentral(doc.RootElement) is { } bc)
                return (new ValorConsultado(Dolar, "Dólar observado", bc.Valor, bc.Fecha, "API_BCCH", "Banco Central de Chile"), notas);
        }

        // 2. mindicador.cl: serie del año (y la del año anterior, si la fecha cae en los primeros días hábiles de enero)
        string serie = indicador.Codigo == Dolar ? "dolar" : "libra_cobre";
        for (int anio = fecha.Year; anio >= fecha.Year - 1; anio--)
        {
            using var doc = await LeerJsonAsync($"{O.UrlIndicadores.TrimEnd('/')}/{serie}/{anio}", "mindicador.cl", notas, ct);
            if (doc is null) break;
            if (LeerSerie(doc.RootElement, fecha) is { } v)
            {
                var encontrado = indicador.Codigo == Dolar
                    ? new ValorConsultado(Dolar, "Dólar observado", v.Valor, v.Fecha, "API_BCCH", "mindicador.cl (Banco Central)")
                    : new ValorConsultado(Cobre, "Precio de la libra de cobre", v.Valor, v.Fecha, "RPA_COCHILCO", "mindicador.cl (COCHILCO)");
                return (encontrado, notas);
            }
        }
        notas.Add($"No se encontró «{indicador.Nombre}» para el {fecha:dd-MM-yyyy} ni para los días anteriores.");
        return (null, notas);
    }

    /// <summary>Formato de mindicador.cl por año: { "serie": [ { "fecha": "...", "valor": 950.12 }, ... ] }. Toma el último valor hasta la fecha.</summary>
    private static (double Valor, DateTime Fecha)? LeerSerie(JsonElement raiz, DateTime hasta)
    {
        if (!raiz.TryGetProperty("serie", out var serie) || serie.ValueKind != JsonValueKind.Array) return null;
        (double Valor, DateTime Fecha)? mejor = null;
        foreach (var o in serie.EnumerateArray())
        {
            if (!o.TryGetProperty("valor", out var v) || !v.TryGetDouble(out var valor) || valor <= 0) continue;
            if (!o.TryGetProperty("fecha", out var f) || !DateTime.TryParse(f.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var fa)) continue;
            var dia = fa.ToLocalTime().Date;
            if (dia <= hasta && (mejor is null || dia > mejor.Value.Fecha)) mejor = (valor, dia);
        }
        return mejor;
    }

    /// <summary>
    /// Descarga y lee un JSON. Si la conexión segura falla (por ejemplo, «se ha forzado la interrupción de una conexión»),
    /// reintenta una vez con TLS 1.2. Si no se puede, agrega una nota y devuelve nulo.
    /// </summary>
    private async Task<JsonDocument?> LeerJsonAsync(string url, string nombre, List<string> notas, CancellationToken ct)
    {
        try { return await DescargarAsync(http, url, ct); }
        catch (HttpRequestException) when (!ct.IsCancellationRequested)
        {
            try { return await DescargarAsync(ClienteTls12.Value, url, ct); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                notas.Add($"No respondió {nombre}: {Mensaje(ex)}");
                return null;
            }
        }
        catch (Exception ex) when (ex is TaskCanceledException or JsonException)
        {
            notas.Add($"No respondió {nombre}: {Mensaje(ex)}");
            return null;
        }
    }

    private async Task<JsonDocument> DescargarAsync(HttpClient cliente, string url, CancellationToken ct)
    {
        using var espera = CancellationTokenSource.CreateLinkedTokenSource(ct);
        espera.CancelAfter(TimeSpan.FromSeconds(Math.Max(3, O.SegundosEspera)));
        using var pedido = new HttpRequestMessage(HttpMethod.Get, url);
        pedido.Headers.UserAgent.ParseAdd("EnamiEvaluacion/1.0");
        pedido.Headers.Accept.ParseAdd("application/json");
        using var respuesta = await cliente.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, espera.Token);
        respuesta.EnsureSuccessStatusCode();
        // el Banco Central envía las tildes en Latin-1: se decodifica con BancoCentral.Decodificar (UTF-8 si es válido)
        var bytes = await respuesta.Content.ReadAsByteArrayAsync(espera.Token);
        return JsonDocument.Parse(BancoCentral.Decodificar(bytes));
    }

    /// <summary>Mensaje legible: el de la excepción más interna (por ejemplo, la del socket), no «see inner exception».</summary>
    private static string Mensaje(Exception ex)
    {
        if (ex is TaskCanceledException) return "no contestó a tiempo.";
        while (ex.InnerException is not null) ex = ex.InnerException;
        return ex.Message;
    }

    /// <summary>Formato de mindicador.cl: { "dolar": { "fecha": "2026-11-12T03:00:00.000Z", "valor": 950.12 }, ... }.</summary>
    private static (double Valor, DateTime Fecha)? LeerIndicador(JsonElement raiz, string indicador)
    {
        if (!raiz.TryGetProperty(indicador, out var dato) || !dato.TryGetProperty("valor", out var v)
            || !v.TryGetDouble(out var valor) || valor <= 0) return null;
        var fecha = dato.TryGetProperty("fecha", out var f) && DateTime.TryParse(f.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var fa) ? fa.ToLocalTime() : DateTime.Now;
        return (valor, fecha);
    }

    /// <summary>
    /// Formato del Banco Central: { "Codigo": 0, "Series": { "Obs": [ { "indexDateString": "12-11-2026", "value": "950.12" } ] } }.
    /// Los días sin dato (fines de semana) vienen con «NaN»: se toma la última observación con valor.
    /// </summary>
    private static (double Valor, DateTime Fecha)? LeerBancoCentral(JsonElement raiz)
    {
        if (raiz.TryGetProperty("Codigo", out var codigo) && codigo.TryGetInt32(out var c) && c != 0) return null;
        if (!raiz.TryGetProperty("Series", out var serie) || !serie.TryGetProperty("Obs", out var obs) || obs.ValueKind != JsonValueKind.Array) return null;
        (double Valor, DateTime Fecha)? ultimo = null;
        foreach (var o in obs.EnumerateArray())
        {
            var texto = o.TryGetProperty("value", out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()) : null;
            if (!double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var valor) || !double.IsFinite(valor) || valor <= 0) continue;
            var fecha = o.TryGetProperty("indexDateString", out var d) && DateTime.TryParseExact(d.GetString(), "dd-MM-yyyy", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var fd) ? fd : DateTime.Today;
            ultimo = (valor, fecha);
        }
        return ultimo;
    }
}

/// <summary>
/// Consulta automática: al iniciar la aplicación y luego cada hora, si aún no hay valores de API del día, consulta y registra.
/// Si ninguna API responde, deja un aviso en el registro; los usuarios pueden ingresar los valores a mano.
/// </summary>
public class ConsultaMercadoAutomatica(IServiceScopeFactory scopes, IOptions<OpcionesMercado> opciones, ILogger<ConsultaMercadoAutomatica> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!opciones.Value.ConsultaAutomatica) return;
        using var reloj = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var consulta = scope.ServiceProvider.GetRequiredService<ConsultaMercado>();
                if (!consulta.ActualizadoHoy())
                {
                    var r = await consulta.ConsultarYRegistrarAsync(null, respetarFijados: true, ct: ct);
                    foreach (var v in r.Registrados) log.LogInformation("Mercado: {Codigo} = {Valor} ({Origen}).", v.Codigo, v.Valor, v.Origen);
                    foreach (var n in r.Notas) log.LogInformation("Mercado: {Nota}", n);
                    foreach (var e in r.Errores) log.LogWarning("Mercado: {Error}", e);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                log.LogWarning("Mercado: no se pudo completar la consulta automática: {Mensaje}", ex.Message);
            }
        }
        while (await reloj.WaitForNextTickAsync(ct));
    }
}
