using System.Globalization;
using Enami.Dominio.Calculo;

namespace Enami.Web.Servicios;

/// <summary>Formatos chilenos para mostrar montos, números y porcentajes.</summary>
public static class Formato
{
    public static readonly CultureInfo Chile = CultureInfo.GetCultureInfo("es-CL");
    public static string Pesos(double x) => "$ " + x.ToString("#,##0", Chile);
    /// <summary>Montos de la evaluación: desde el Sprint 4 se calculan en dólares, como la planilla de ENAMI.</summary>
    public static string Dolares(double x) => "US$ " + x.ToString("#,##0", Chile);
    /// <summary>Número con una cantidad fija de decimales (por ejemplo, leyes: 1,30 %).</summary>
    public static string Fijo(double x, int dec) => x.ToString(dec == 0 ? "#,##0" : "#,##0." + new string('0', dec), Chile);
    public static string Numero(double x, int dec = 2) => x.ToString("#,##0." + new string('#', dec), Chile);
    public static string Porcentaje(double x, int dec = 3) => (x * 100).ToString("0." + new string('0', dec), Chile) + " %";

    public static string Origen(OrigenVariable o) => o switch
    {
        OrigenVariable.Formulario => "Formulario",
        OrigenVariable.Mantenedor => "Mantenedor",
        OrigenVariable.Robot => "API / robot (BOT-01)",
        _ => o.ToString()
    };

    /// <summary>De dónde vino un valor de PARAMETRO_ECONOMICO.</summary>
    public static string Fuente(string fuente) => fuente switch
    {
        "API_BCCH" => "API (Banco Central)",
        "RPA_COCHILCO" => "API / robot COCHILCO",
        "API_EXTERNA" => "API (tipo de cambio de mercado)",
        "MANUAL" => "Ingreso manual",
        _ => fuente
    };

    public static string ClaseEstado(EstadoPlantilla e) => e switch
    {
        EstadoPlantilla.Vigente => "bg-success",
        EstadoPlantilla.Borrador => "bg-warning text-dark",
        _ => "bg-secondary"
    };
}
