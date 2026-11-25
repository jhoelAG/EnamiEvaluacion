using System.Security.Cryptography;
using System.Text;

namespace Enami.Dominio.Servicios;

/// <summary>
/// Código firmado de cada correo de aprobación (BOT-03). La aplicación lo calcula con una clave secreta (HMAC-SHA256) a partir
/// de la aprobación pendiente, la evaluación y el correo del aprobador asignado; la API solo registra una decisión por correo
/// si el código coincide. Así, conocer la clave de la API no basta para decidir en nombre de otro aprobador, y un código sirve
/// para una sola aprobación (una vez decidida, esa aprobación ya no está pendiente).
/// </summary>
public static class FirmaDecision
{
    public static string Generar(string clave, long idAprobacion, long idEvaluacion, string correo)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(clave));
        var firma = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{idAprobacion}|{idEvaluacion}|{correo.Trim().ToLowerInvariant()}"));
        return Convert.ToBase64String(firma).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool Verificar(string clave, long idAprobacion, long idEvaluacion, string correo, string? codigo) =>
        !string.IsNullOrEmpty(codigo) && Iguales(Generar(clave, idAprobacion, idEvaluacion, correo), codigo);

    /// <summary>Compara dos secretos en tiempo constante, para que el tiempo de respuesta no revele cuántos caracteres coinciden.</summary>
    public static bool Iguales(string esperado, string? recibido) =>
        recibido is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(esperado), Encoding.UTF8.GetBytes(recibido));
}
