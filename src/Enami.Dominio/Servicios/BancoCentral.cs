using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Enami.Dominio.Servicios;

/// <summary>
/// Lectura de la API oficial del Banco Central de Chile (Base de Datos Estadísticos, servicio SieteRestWS).
/// El servicio responde JSON, pero con las tildes en Latin-1 (por ejemplo «ó» = byte F3) aunque no lo declare:
/// por eso se decodifica primero como UTF-8 estricto y, si no es válido, como Latin-1.
/// </summary>
public static class BancoCentral
{
    public const string Url = "https://si3.bcentral.cl/SieteRestWS/SieteRestWS.ashx";

    /// <summary>Series oficiales por código de variable de la aplicación.</summary>
    public static readonly IReadOnlyDictionary<string, (string Serie, string Nombre)> Series =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dolar"] = ("F073.TCO.PRE.Z.D", "Dólar observado"),
            ["PrecioCuLb"] = ("F019.PPB.PRE.100.D", "Cobre, Bolsa de Metales de Londres (US$/lb)"),
            ["PrecioAu"] = ("F019.PPB.PRE.44.D", "Oro (US$/onza troy)"),
            ["PrecioAg"] = ("F019.PPB.PRE.45.D", "Plata (US$/onza troy)"),
        };

    /// <summary>Dirección de la consulta de los últimos <paramref name="dias"/> días hasta <paramref name="hasta"/>.</summary>
    public static string Consulta(string usuario, string clave, string serie, DateTime hasta, int dias = 15) =>
        $"{Url}?user={Uri.EscapeDataString(usuario)}&pass={Uri.EscapeDataString(clave)}&function=GetSeries" +
        $"&timeseries={Uri.EscapeDataString(serie)}&firstdate={hasta.AddDays(-dias):yyyy-MM-dd}&lastdate={hasta:yyyy-MM-dd}";

    private static readonly UTF8Encoding Utf8Estricto = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Texto de la respuesta: UTF-8 si es válido; si no, Latin-1 (lo que envía el Banco Central).</summary>
    public static string Decodificar(byte[] bytes)
    {
        try { return Utf8Estricto.GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    /// <summary>
    /// Última observación publicada (los días sin dato vienen con statusCode «ND» o valor «NaN»).
    /// Devuelve el valor y su fecha, o el motivo si el Banco Central respondió un error (por ejemplo, usuario o clave inválidos).
    /// </summary>
    public static (double Valor, DateTime Fecha)? LeerUltimo(string json, out string? error)
    {
        error = null;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { error = "El Banco Central no respondió en formato JSON."; return null; }
        using (doc)
        {
            var raiz = doc.RootElement;
            if (raiz.TryGetProperty("Codigo", out var codigo) && codigo.TryGetInt32(out var c) && c != 0)
            {
                var desc = raiz.TryGetProperty("Descripcion", out var d) ? d.GetString() : null;
                error = $"El Banco Central respondió el código {c}: {desc ?? "sin descripción"}.";
                return null;
            }
            if (!raiz.TryGetProperty("Series", out var serie) || !serie.TryGetProperty("Obs", out var obs) || obs.ValueKind != JsonValueKind.Array)
            {
                error = "El Banco Central no entregó observaciones para la serie.";
                return null;
            }
            (double, DateTime)? ultimo = null;
            foreach (var o in obs.EnumerateArray())
            {
                if (o.TryGetProperty("statusCode", out var st) && st.ValueKind == JsonValueKind.String && st.GetString() != "OK") continue;
                var texto = o.TryGetProperty("value", out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()) : null;
                if (!double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var valor) || !double.IsFinite(valor) || valor <= 0) continue;
                if (!o.TryGetProperty("indexDateString", out var f) || !DateTime.TryParseExact(f.GetString(), "dd-MM-yyyy",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)) continue;
                ultimo = (valor, fecha);
            }
            if (ultimo is null) error = "El Banco Central no publicó valores en los últimos días.";
            return ultimo;
        }
    }
}
