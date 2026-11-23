using System.Globalization;
using System.Text.RegularExpressions;

namespace Enami.Dominio.Importacion;

/// <summary>
/// Reglas de limpieza y validación que corrigen los problemas detectados en las planillas de ENAMI (RF-07 y RF-26):
/// distancias en 0 o imposibles, leyes de cobre de 100 %, fechas 01-01-1900, espacios de relleno y códigos con formatos mezclados.
/// </summary>
public static partial class ReglasCalidad
{
    public const double DistanciaMaximaKm = 1000;

    [GeneratedRegex(@"\s+")]
    private static partial Regex RegexEspacios();

    [GeneratedRegex(@"^\d{1,8}-[\dkK]$")]
    private static partial Regex RegexRut();

    /// <summary>Quita espacios de relleno y espacios repetidos. Devuelve nulo si queda vacío.</summary>
    public static string? LimpiarTexto(string? valor)
    {
        if (valor is null) return null;
        var limpio = RegexEspacios().Replace(valor.Trim(), " ");
        return limpio.Length == 0 ? null : limpio;
    }

    /// <summary>Normaliza un RUT a «12345678-9» (sin puntos, con guion y dígito verificador en mayúscula).</summary>
    public static string? NormalizarRut(string? valor)
    {
        var t = LimpiarTexto(valor);
        if (t is null) return null;
        t = t.Replace(".", "").Replace(" ", "").ToUpperInvariant();
        if (!t.Contains('-') && t.Length >= 2) t = t[..^1] + "-" + t[^1];
        return t;
    }

    /// <summary>Formato de RUT válido (no verifica el dígito, porque los datos del piloto vienen anonimizados).</summary>
    public static bool RutConFormatoValido(string? rut) => rut is not null && RegexRut().IsMatch(rut);

    /// <summary>Dígito verificador según módulo 11 (se activa en producción con RUT reales).</summary>
    public static bool DigitoVerificadorValido(string rut)
    {
        if (!RutConFormatoValido(rut)) return false;
        var partes = rut.Split('-');
        int suma = 0, factor = 2;
        for (int i = partes[0].Length - 1; i >= 0; i--)
        {
            suma += (partes[0][i] - '0') * factor;
            factor = factor == 7 ? 2 : factor + 1;
        }
        int resto = 11 - suma % 11;
        char dv = resto == 11 ? '0' : resto == 10 ? 'K' : (char)('0' + resto);
        return char.ToUpperInvariant(partes[1][0]) == dv;
    }

    /// <summary>Normaliza el código de faena a 5 caracteres (la fuente mezcla 4 y 5 dígitos y guiones).</summary>
    public static string? NormalizarCodigoFaena(string? valor)
    {
        var t = LimpiarTexto(valor)?.Replace("-", "").ToUpperInvariant();
        if (string.IsNullOrEmpty(t)) return null;
        return t.Length < 5 ? t.PadLeft(5, '0') : t;
    }

    /// <summary>Lee un número aceptando coma o punto decimal. Devuelve nulo si está vacío o no es número.</summary>
    public static double? LeerNumero(string? valor)
    {
        var t = LimpiarTexto(valor);
        if (t is null) return null;
        t = t.Replace(" ", "");
        if (t.Contains(',') && t.Contains('.')) t = t.Replace(".", "").Replace(',', '.');
        else t = t.Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>Lee una fecha en número de serie de Excel o en texto. Las fechas 01-01-1900 se tratan como «sin dato».</summary>
    public static DateTime? LeerFecha(string? valor)
    {
        var t = LimpiarTexto(valor);
        if (t is null) return null;
        DateTime? f = null;
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var serie) && serie > 0 && serie < 2_958_466)
            f = DateTime.FromOADate(serie);
        else if (DateTime.TryParse(t, CultureInfo.GetCultureInfo("es-CL"), DateTimeStyles.None, out var d))
            f = d;
        if (f is null || f.Value.Year <= 1900) return null;
        return f;
    }

    /// <summary>Valida la distancia al poder de compra: mayor que 0 y menor que 1.000 km.</summary>
    public static string? ValidarDistancia(double? km) => km switch
    {
        null => null,
        <= 0 => "La distancia al poder de compra es 0 o negativa; se deja sin dato hasta que el ingeniero la complete.",
        >= DistanciaMaximaKm => $"La distancia de {km:0.##} km es imposible (máximo {DistanciaMaximaKm:0} km).",
        _ => null
    };

    /// <summary>Valida la ley de cobre: mayor que 0 % y menor que 100 %.</summary>
    public static string? ValidarLeyCobre(double? ley) => ley switch
    {
        null => null,
        <= 0 => "La ley de cobre debe ser mayor que 0 %.",
        >= 100 => $"La ley de cobre de {ley:0.##} % no es posible (debe ser menor que 100 %).",
        _ => null
    };
}
