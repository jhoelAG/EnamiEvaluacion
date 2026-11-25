using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Enami.Dominio.Servicios;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Enami.Datos;

/// <summary>Sección «Flujos» de la configuración (variables de entorno Flujos__… en Azure).</summary>
public class OpcionesFlujos
{
    /// <summary>
    /// URL del desencadenador «Cuando se recibe una solicitud HTTP» de la Logic App de aprobación (BOT-03).
    /// Vacía = sin flujo: la aprobación se hace solo dentro de la aplicación.
    /// </summary>
    public string? UrlAprobacion { get; set; }
    /// <summary>Dirección pública de la aplicación web, para el enlace del correo (https://enami-web-….azurewebsites.net).</summary>
    public string? UrlWeb { get; set; }
    /// <summary>Dirección pública de la API, donde el flujo registra la decisión (https://enami-api-….azurewebsites.net).</summary>
    public string? UrlApi { get; set; }
}

/// <summary>Envía los avisos de aprobación a la Logic App con un POST JSON. El flujo responde 202 y continúa por su cuenta.</summary>
public class NotificadorFlujosHttp(HttpClient http, IOptions<OpcionesFlujos> opciones, ILogger<NotificadorFlujosHttp> log) : INotificadorFlujos
{
    public async Task<string?> EnviarAsync(EventoFlujo evento, CancellationToken ct = default)
    {
        var o = opciones.Value;
        if (string.IsNullOrWhiteSpace(o.UrlAprobacion))
            return "El flujo de aprobación (BOT-03) no está configurado: el aprobador verá la evaluación en su bandeja de la aplicación.";
        var e = evento with
        {
            UrlEvaluacion = string.IsNullOrWhiteSpace(o.UrlWeb) ? null : $"{o.UrlWeb.TrimEnd('/')}/evaluaciones/{evento.IdEvaluacion}",
            UrlDecision = string.IsNullOrWhiteSpace(o.UrlApi) ? null : $"{o.UrlApi.TrimEnd('/')}/api/evaluaciones/{evento.IdEvaluacion}/decision",
        };
        try
        {
            using var espera = CancellationTokenSource.CreateLinkedTokenSource(ct);
            espera.CancelAfter(TimeSpan.FromSeconds(20));
            // Se envía el JSON completo con su largo (Content-Length): el desencadenador HTTP de Logic Apps
            // recibe vacío un cuerpo enviado en trozos (Transfer-Encoding: chunked), como el de PostAsJsonAsync.
            using var contenido = new StringContent(JsonSerializer.Serialize(e, JsonSerializerOptions.Web), Encoding.UTF8, "application/json");
            using var r = await http.PostAsync(o.UrlAprobacion, contenido, espera.Token);
            if (r.IsSuccessStatusCode) return null;
            log.LogWarning("Flujo de aprobación: respondió {Codigo} al aviso {Tipo} de la evaluación {Id}.", (int)r.StatusCode, e.Tipo, e.IdEvaluacion);
            return $"El flujo de aprobación respondió {(int)r.StatusCode}; el aprobador igual la ve en su bandeja de la aplicación.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning("Flujo de aprobación: no se pudo enviar el aviso {Tipo} de la evaluación {Id}: {Mensaje}", e.Tipo, e.IdEvaluacion, ex.Message);
            return "No se pudo avisar al flujo de aprobación; el aprobador igual la ve en su bandeja de la aplicación.";
        }
    }
}
