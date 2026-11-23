namespace Enami.Dominio.Calculo.Expresiones;

/// <summary>
/// Funciones permitidas dentro de una fórmula. Solo se aceptan estas funciones matemáticas:
/// el usuario nunca puede ejecutar código, lo que mantiene el editor seguro.
/// </summary>
public static class Funciones
{
    /// <summary>Nombre, cantidad mínima y máxima de argumentos y descripción para la ayuda del editor.</summary>
    public static readonly IReadOnlyList<(string Nombre, int Min, int Max, string Ayuda)> Catalogo =
    [
        ("SI", 3, 3, "SI(condición; valor_si_verdadero; valor_si_falso)"),
        ("MIN", 1, 30, "MIN(a; b; ...) — menor valor"),
        ("MAX", 1, 30, "MAX(a; b; ...) — mayor valor"),
        ("ABS", 1, 1, "ABS(x) — valor absoluto"),
        ("REDONDEAR", 2, 2, "REDONDEAR(x; decimales)"),
        ("TRUNCAR", 1, 2, "TRUNCAR(x; [decimales]) — corta los decimales sin redondear, como TRUNCAR de Excel"),
        ("RAIZ", 1, 1, "RAIZ(x) — raíz cuadrada"),
        ("POTENCIA", 2, 2, "POTENCIA(base; exponente)"),
        ("Y", 1, 30, "Y(c1; c2; ...) — 1 si todas las condiciones se cumplen"),
        ("O", 1, 30, "O(c1; c2; ...) — 1 si alguna condición se cumple"),
        ("VA", 3, 5, "VA(tasa; nper; pago; [vf]; [tipo]) — valor actual, igual que VA de Excel"),
        ("PAGO", 3, 5, "PAGO(tasa; nper; va; [vf]; [tipo]) — cuota periódica, igual que PAGO de Excel"),
    ];

    public static bool Existe(string nombre) => Catalogo.Any(f => f.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase));

    public static bool EsSi(string nombre) => nombre.Equals("SI", StringComparison.OrdinalIgnoreCase);

    public static void ValidarAridad(string nombre, int cantidad, int posicion)
    {
        var f = Catalogo.FirstOrDefault(x => x.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase));
        if (f.Nombre is null)
            throw new FormulaException($"La función «{nombre}» no existe. Funciones disponibles: {string.Join(", ", Catalogo.Select(c => c.Nombre))}.", posicion);
        if (cantidad < f.Min || cantidad > f.Max)
            throw new FormulaException(f.Min == f.Max
                ? $"La función {f.Nombre} requiere {f.Min} argumento(s) y recibió {cantidad}."
                : $"La función {f.Nombre} requiere entre {f.Min} y {f.Max} argumentos y recibió {cantidad}.", posicion);
    }

    public static double Ejecutar(string nombre, double[] v, int posicion) => nombre.ToUpperInvariant() switch
    {
        "MIN" => v.Min(),
        "MAX" => v.Max(),
        "ABS" => Math.Abs(v[0]),
        "REDONDEAR" => Math.Round(v[0], (int)v[1], MidpointRounding.AwayFromZero),
        "TRUNCAR" => Truncar(v[0], v.Length > 1 ? (int)v[1] : 0),
        "RAIZ" => v[0] < 0 ? throw new FormulaException("RAIZ de un número negativo.", posicion) : Math.Sqrt(v[0]),
        "POTENCIA" => Math.Pow(v[0], v[1]),
        "Y" => v.All(x => x != 0) ? 1 : 0,
        "O" => v.Any(x => x != 0) ? 1 : 0,
        "VA" => Va(v[0], v[1] < 0 ? throw new FormulaException("El número de períodos (nper) no puede ser negativo.", posicion) : v[1], v[2], v.Length > 3 ? v[3] : 0, v.Length > 4 ? v[4] : 0),
        "PAGO" => Pago(v[0], Periodos(v[1], posicion), v[2], v.Length > 3 ? v[3] : 0, v.Length > 4 ? v[4] : 0),
        _ => throw new FormulaException($"La función «{nombre}» no existe.", posicion)
    };

    /// <summary>Valor actual (VA de Excel): VA = −(pago·(1+tasa·tipo)·((1+tasa)^n − 1)/tasa + vf) / (1+tasa)^n.</summary>
    public static double Va(double tasa, double n, double pago, double vf = 0, double tipo = 0)
    {
        if (tasa == 0) return -(vf + pago * n);
        double f = Math.Pow(1 + tasa, n);
        return -(pago * (1 + tasa * (tipo != 0 ? 1 : 0)) * (f - 1) / tasa + vf) / f;
    }

    /// <summary>Cuota periódica (PAGO de Excel): PAGO = −(va·(1+tasa)^n + vf)·tasa / ((1+tasa·tipo)·((1+tasa)^n − 1)).</summary>
    public static double Pago(double tasa, double n, double va, double vf = 0, double tipo = 0)
    {
        if (tasa == 0) return -(va + vf) / n;
        double f = Math.Pow(1 + tasa, n);
        return -(va * f + vf) * tasa / ((1 + tasa * (tipo != 0 ? 1 : 0)) * (f - 1));
    }

    private static double Truncar(double x, int decimales)
    {
        double f = Math.Pow(10, decimales);
        return Math.Truncate(x * f) / f;
    }

    private static double Periodos(double n, int posicion) =>
        n <= 0 ? throw new FormulaException("El número de períodos (nper) debe ser mayor que cero.", posicion) : n;
}
