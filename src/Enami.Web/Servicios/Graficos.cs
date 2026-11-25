using System.Globalization;

namespace Enami.Web.Servicios;

/// <summary>Una serie de un gráfico: nombre (para la leyenda), color de la marca y un valor por categoría.</summary>
public record SerieGrafico(string Nombre, string Color, IReadOnlyList<double> Valores);

/// <summary>Utilidades de los gráficos SVG del tablero.</summary>
public static class Graficos
{
    // Colores de marca validados para gráficos (paleta categórica de dos series, apta para daltonismo):
    public const string Turquesa = "#00A396", TurquesaSuave = "#E3F7F5", Naranjo = "#F88D2B", NaranjoSuave = "#FEF0E3";
    public const string Grilla = "#E2DEE4", TextoSecundario = "#7D8285";

    /// <summary>Número para atributos SVG (siempre con punto decimal, sin importar la cultura).</summary>
    public static string N(double x) => Math.Round(x, 2).ToString(CultureInfo.InvariantCulture);

    /// <summary>Máximo "redondo" del eje y su paso (por ejemplo 0, 5, 10, 15, 20) para un valor máximo dado.</summary>
    public static (double Tope, double Paso) Escala(double maximo, int marcas = 4)
    {
        if (maximo <= 0) return (marcas, 1);
        double bruto = maximo / marcas, potencia = Math.Pow(10, Math.Floor(Math.Log10(bruto)));
        double paso = new[] { 1, 2, 5, 10 }.Select(m => m * potencia).First(p => p >= bruto);
        if (paso < 1) paso = 1;   // los conteos son enteros
        return (Math.Ceiling(maximo / paso) * paso, paso);
    }
}
