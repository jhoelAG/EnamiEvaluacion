namespace Enami.Dominio.Servicios;

/// <summary>
/// Validación de rango de los valores que registra BOT-01 (proceso 4 del informe: «validación de rangos antes de guardar los precios»).
/// Un valor que difiere del vigente en más de <see cref="Tolerancia"/> se rechaza: casi siempre es un error de unidad o de la fuente
/// (por ejemplo, el cobre por tonelada en vez de por libra), y es preferible mantener el valor vigente y alertar.
/// </summary>
public static class ValidacionMercado
{
    /// <summary>Variación máxima aceptada respecto del valor vigente (50 %).</summary>
    public const double Tolerancia = 0.5;

    /// <summary>Motivo del rechazo, o null si el valor es aceptable.</summary>
    public static string? Revisar(string codigo, double nuevo, double? vigente)
    {
        if (double.IsNaN(nuevo) || double.IsInfinity(nuevo) || nuevo <= 0)
            return $"«{codigo}» = {nuevo}: el valor debe ser un número mayor que cero.";
        if (vigente is double v && v > 0 && Math.Abs(nuevo - v) / v > Tolerancia)
            return $"«{codigo}» = {nuevo}: difiere en más de {Tolerancia:P0} del valor vigente ({v}); se mantiene el vigente.";
        return null;
    }
}
