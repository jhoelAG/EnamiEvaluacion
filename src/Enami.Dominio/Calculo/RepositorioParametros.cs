using Enami.Dominio.Modelo;
using Enami.Dominio.Persistencia;

namespace Enami.Dominio.Calculo;

/// <summary>
/// Valor registrado de una variable de mercado o de mantenedor (tabla PARAMETRO_ECONOMICO). <c>Fecha</c> es el día en que se registró;
/// <c>FechaValor</c>, el día al que corresponde según la API; <c>IdUsuario</c>, quién lo fijó (nulo si fue automático).
/// </summary>
public record ValorParametro(string Codigo, double Valor, string Fuente, DateTime Fecha, DateTime? FechaValor = null, int? IdUsuario = null)
{
    /// <summary>Lo fijó una persona (a mano o eligiendo una fecha): la consulta automática no lo reemplaza.</summary>
    public bool Fijado => IdUsuario is not null;
}

/// <summary>
/// Valores vigentes de las variables que no ingresa el ingeniero: las que se consultan por API o actualiza BOT-01
/// (dólar, precios del cobre, oro y plata), que también se pueden ingresar a mano, y las del mantenedor
/// (tarifa de flete, tasa de descuento). Al evaluar se usa el último valor registrado, venga de la API o sea manual.
/// </summary>
public interface IRepositorioParametros
{
    IReadOnlyDictionary<string, ValorParametro> Ultimos();
    /// <summary>
    /// Registra el valor del día. Si ya existe uno de la misma fecha y fuente, lo reemplaza (clave única fecha + variable + fuente)
    /// y pasa a ser el último registrado. <paramref name="fechaValor"/> es el día al que corresponde el valor según la API;
    /// <paramref name="idUsuario"/>, la persona que lo fija (nulo para los robots y la consulta automática).
    /// </summary>
    void Registrar(string codigo, double valor, string fuente, DateTime? fechaValor = null, int? idUsuario = null);
    /// <summary>Valores registrados de una variable, del más reciente al más antiguo.</summary>
    IReadOnlyList<ValorParametro> Historial(string codigo, int cantidad = 10);
}

public class RepositorioParametros(IAlmacen db) : IRepositorioParametros
{
    public static readonly string[] Fuentes = ["API_BCCH", "RPA_COCHILCO", "API_EXTERNA", "MANUAL"];

    public IReadOnlyDictionary<string, ValorParametro> Ultimos()
    {
        // Para cada variable, el registro más reciente (si hay dos el mismo día, el último ingresado).
        return db.Tabla<ParametroEconomico>().ToList()
            .GroupBy(p => p.CodigoVariable, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(p => p.Fecha).ThenByDescending(p => p.Id).First())
            .ToDictionary(p => p.CodigoVariable, p => new ValorParametro(p.CodigoVariable, (double)p.Valor, p.Fuente, p.Fecha, p.FechaValor, p.IdUsuario),
                          StringComparer.OrdinalIgnoreCase);
    }

    public void Registrar(string codigo, double valor, string fuente, DateTime? fechaValor = null, int? idUsuario = null)
    {
        if (string.IsNullOrWhiteSpace(codigo) || codigo.Length > 30)
            throw new ArgumentException("El código de la variable es obligatorio y tiene como máximo 30 caracteres.");
        if (double.IsNaN(valor) || double.IsInfinity(valor) || valor <= 0)
            throw new ArgumentException("El valor debe ser un número mayor que cero.");
        fuente = NormalizarFuente(fuente);
        var hoy = DateTime.Today;
        codigo = codigo.Trim();
        var existente = db.Tabla<ParametroEconomico>()
            .FirstOrDefault(p => p.Fecha == hoy && p.CodigoVariable == codigo && p.Fuente == fuente);
        // Si ya había uno de la misma fecha y fuente se reemplaza por una fila nueva, para que quede como el último registrado
        // (por ejemplo: API a las 9:00, corrección manual a las 10:00 y nueva consulta a la API a las 11:00 → vale la de las 11:00).
        db.EnTransaccion(() =>
        {
            if (existente is not null)
            {
                db.Eliminar(existente);
                db.Guardar();
            }
            db.Agregar(new ParametroEconomico
            {
                Fecha = hoy, CodigoVariable = codigo, Valor = (decimal)valor, Fuente = fuente,
                FechaValor = fechaValor?.Date, IdUsuario = idUsuario
            });
            db.Guardar();
        });
    }

    public IReadOnlyList<ValorParametro> Historial(string codigo, int cantidad = 10) =>
        db.Tabla<ParametroEconomico>().Where(p => p.CodigoVariable == codigo)
          .OrderByDescending(p => p.Fecha).ThenByDescending(p => p.Id).Take(cantidad).ToList()
          .Select(p => new ValorParametro(p.CodigoVariable, (double)p.Valor, p.Fuente, p.Fecha, p.FechaValor, p.IdUsuario)).ToList();

    /// <summary>Convierte los nombres que envían los robots (por ejemplo, «BOT-01» o «Banco Central») a los valores permitidos.</summary>
    public static string NormalizarFuente(string? fuente)
    {
        var f = (fuente ?? "").Trim().ToUpperInvariant();
        if (Fuentes.Contains(f)) return f;
        if (f.Contains("BCCH") || f.Contains("BANCO") || f == "BOT-01") return "API_BCCH";
        if (f.Contains("COCHILCO") || f.Contains("RPA")) return "RPA_COCHILCO";
        return "MANUAL";
    }
}
