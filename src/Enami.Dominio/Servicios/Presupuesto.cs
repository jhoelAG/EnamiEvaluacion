using Enami.Dominio.Modelo;

namespace Enami.Dominio.Servicios;

/// <summary>Actividad del presupuesto tal como se edita en el formulario.</summary>
public class DatosLineaPresupuesto
{
    public short IdLabor { get; set; }
    public string Programa { get; set; } = ProgramasPresupuesto.Reconocimiento;
    public decimal? Cantidad { get; set; }
    public decimal? CostoUnitario { get; set; }
    /// <summary>Fracción que financia ENAMI (0,8 = 80 %). En el crédito para el desarrollo, la fracción que se presta.</summary>
    public decimal? PorcentajeFinanciamiento { get; set; }
}

/// <summary>Montos de una actividad del presupuesto.</summary>
public record MontoLinea(decimal Total, decimal CapitalRiesgo, decimal Credito, decimal AportePropio);

/// <summary>Totales del Anexo 1.</summary>
public record TotalesPresupuesto(decimal CapitalRiesgo, decimal Credito, decimal AportePropio, decimal Timbres, IReadOnlyList<MontoLinea> Lineas)
{
    /// <summary>Lo que financia ENAMI: capital de riesgo más crédito.</summary>
    public decimal Financiamiento => CapitalRiesgo + Credito;
    public decimal InversionTotal => CapitalRiesgo + Credito + AportePropio;
}

/// <summary>
/// Presupuesto y especificaciones técnicas (Anexo 1), con las reglas de la planilla de ENAMI:
/// <list type="bullet">
/// <item>Reconocimiento de reservas: por unidad, ENAMI aporta el costo por la fracción financiada truncado a dólares enteros
///       (INT de Excel) y el beneficiario, el resto redondeado;</item>
/// <item>Crédito para el desarrollo: ENAMI presta el costo por la fracción indicada (por defecto la mitad), redondeado por unidad;
///       como el beneficiario lo devuelve, también se suma a su aporte;</item>
/// <item>Aporte propio: lo paga el beneficiario;</item>
/// <item>Impuesto de timbres y estampillas: sobre el capital de riesgo, = capital / (1 − tasa) − capital, redondeado; lo paga el beneficiario.</item>
/// </list>
/// </summary>
public static class CalculoPresupuesto
{
    /// <summary>Fracción que se presta por defecto en el crédito para el desarrollo (la planilla financia la mitad del costo).</summary>
    public const decimal FraccionCreditoDesarrollo = 0.5m;

    public static decimal FraccionPorDefecto(string programa, LaborTarifada? labor) => programa switch
    {
        ProgramasPresupuesto.Desarrollo => FraccionCreditoDesarrollo,
        ProgramasPresupuesto.Propio => 0,
        _ => labor?.PorcentajeFinanciamiento ?? 1
    };

    public static MontoLinea Calcular(string programa, decimal cantidad, decimal costo, decimal fraccion)
    {
        decimal total = cantidad * costo;
        switch (programa)
        {
            case ProgramasPresupuesto.Reconocimiento:
                decimal enami = Math.Floor(costo * fraccion) * cantidad;
                decimal propio = Math.Round(costo * (1 - fraccion), 0, MidpointRounding.AwayFromZero) * cantidad;
                return new MontoLinea(total, enami, 0, propio);
            case ProgramasPresupuesto.Desarrollo:
                decimal credito = Math.Round(costo * fraccion, 0, MidpointRounding.AwayFromZero) * cantidad;
                return new MontoLinea(total, 0, credito, credito);
            default:
                return new MontoLinea(total, 0, 0, total);
        }
    }

    /// <param name="tasaTimbresPorcentaje">Impuesto de timbres y estampillas en % (0,8 = 0,8 %).</param>
    public static TotalesPresupuesto Calcular(IEnumerable<DatosLineaPresupuesto> lineas, decimal tasaTimbresPorcentaje)
    {
        var montos = lineas.Select(l => Calcular(l.Programa, l.Cantidad ?? 0, l.CostoUnitario ?? 0, l.PorcentajeFinanciamiento ?? 0)).ToList();
        decimal capital = montos.Sum(m => m.CapitalRiesgo), credito = montos.Sum(m => m.Credito), propio = montos.Sum(m => m.AportePropio);
        decimal timbres = capital == 0 || tasaTimbresPorcentaje <= 0 || tasaTimbresPorcentaje >= 100 ? 0
            : Math.Round(capital / ((100 - tasaTimbresPorcentaje) / 100) - capital, 0, MidpointRounding.AwayFromZero);
        return new TotalesPresupuesto(capital, credito, propio + timbres, timbres, montos);
    }

    /// <summary>Errores de una lista de actividades (cantidades, costos y fracciones).</summary>
    public static List<string> Validar(IReadOnlyList<DatosLineaPresupuesto> lineas, IReadOnlyDictionary<short, LaborTarifada> labores)
    {
        var errores = new List<string>();
        for (int i = 0; i < lineas.Count; i++)
        {
            var l = lineas[i];
            string nombre = labores.TryGetValue(l.IdLabor, out var lab) ? lab.Nombre : $"actividad {i + 1}";
            if (lab is null) errores.Add($"Actividad {i + 1}: seleccione la actividad.");
            if (!ProgramasPresupuesto.Todos.Contains(l.Programa)) errores.Add($"{nombre}: seleccione cómo se financia.");
            if (l.Cantidad is null || l.Cantidad <= 0) errores.Add($"{nombre}: la cantidad debe ser mayor que cero.");
            else if (l.Cantidad > 9_999_999_999m) errores.Add($"{nombre}: la cantidad es demasiado grande.");
            if (l.CostoUnitario is null || l.CostoUnitario < 0) errores.Add($"{nombre}: el costo unitario no puede ser negativo ni estar vacío.");
            else if (l.CostoUnitario > 9_999_999_999m) errores.Add($"{nombre}: el costo unitario es demasiado grande.");
            if (l.PorcentajeFinanciamiento is null || l.PorcentajeFinanciamiento < 0 || l.PorcentajeFinanciamiento > 1)
                errores.Add($"{nombre}: el porcentaje financiado debe estar entre 0 y 100 %.");
        }
        return errores;
    }
}
