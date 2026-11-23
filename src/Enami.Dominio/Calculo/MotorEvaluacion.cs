using System.Globalization;
using Enami.Dominio.Calculo.Expresiones;

namespace Enami.Dominio.Calculo;

/// <summary>
/// Calcula una evaluación financiera con las fórmulas de la plantilla.
/// Las fórmulas (editables) definen el flujo de cada período y la tasa;
/// el VAN, el IVAN y la viabilidad se calculan aquí y no se pueden editar:
///   VAN = −Inversión + Σ FlujoPeriodo(t) / (1 + TasaPeriodo)^t,  t = 1..Plazo
///   IVAN = VAN / Inversión;  viable si VAN ≥ 0 (RF-02, RF-03 y RF-17).
/// </summary>
public static class MotorEvaluacion
{
    public static ResultadoEvaluacion Evaluar(PlantillaCalculo plantilla, IDictionary<string, double>? entradas = null)
    {
        var r = new ResultadoEvaluacion { Plantilla = plantilla.Nombre, VersionPlantilla = plantilla.Version };
        var compilado = MotorFormulas.Compilar(plantilla);
        if (!compilado.EsValido)
        {
            r.Errores.AddRange(compilado.Errores.Select(e => e.ToString()));
            return r;
        }

        // valores de entrada: lo ingresado o, si falta, el valor por defecto de la variable
        var valores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        r.Modo = plantilla.Modo;
        foreach (var v in plantilla.Variables.Where(v => v.EsNumerica))
        {
            double valor = entradas is not null && entradas.TryGetValue(v.Codigo, out var x) ? x : v.ValorPorDefecto;
            if (double.IsNaN(valor) || double.IsInfinity(valor))
                r.Errores.Add($"{v.Nombre}: el valor no es un número válido.");
            else if (v.Minimo is not null && valor < v.Minimo)
                r.Errores.Add($"{v.Nombre}: el valor {Formato(valor)} es menor que el mínimo permitido ({Formato(v.Minimo.Value)} {v.Unidad}).");
            else if (v.Maximo is not null && valor > v.Maximo)
                r.Errores.Add($"{v.Nombre}: el valor {Formato(valor)} es mayor que el máximo permitido ({Formato(v.Maximo.Value)} {v.Unidad}).");
            valores[v.Codigo] = valor;
            r.Entradas[v.Codigo] = valor;
        }
        if (!r.Exitoso) return r;
        if (plantilla.Modo == ModoCalculo.Escenarios) return EvaluarEscenarios(r, compilado, valores);

        double inversion = valores[MotorFormulas.CodigoInversion];
        double plazoReal = valores[MotorFormulas.CodigoPlazo];
        int plazo = (int)Math.Round(plazoReal);
        if (plazo < 1 || Math.Abs(plazo - plazoReal) > 1e-9)
        {
            r.Errores.Add("El plazo debe ser un número entero de períodos mayor o igual a 1.");
            return r;
        }
        r.Inversion = inversion;
        r.Plazo = plazo;

        double van = -inversion;
        try
        {
            for (int t = 1; t <= plazo; t++)
            {
                var ctx = new Contexto(valores, t);
                foreach (var (f, arbol) in compilado.OrdenCalculo)
                {
                    double y = arbol.Evaluar(ctx);
                    if (double.IsNaN(y) || double.IsInfinity(y))
                        throw new FormulaException($"La fórmula «{f.Codigo}» no entrega un número válido en el período {t}.");
                    ctx.Formulas[f.Codigo] = y;
                }
                double flujo = ctx.Formulas[MotorFormulas.CodigoFlujo];
                double tasa = ctx.Formulas[MotorFormulas.CodigoTasa];
                if (tasa <= -1)
                    throw new FormulaException("La tasa por período debe ser mayor que −100 %.");
                double factor = 1 / Math.Pow(1 + tasa, t);
                double actualizado = flujo * factor;
                van += actualizado;
                r.Periodos.Add(new PeriodoFlujo(t, flujo, factor, actualizado, new Dictionary<string, double>(ctx.Formulas, StringComparer.OrdinalIgnoreCase)));
            }
        }
        catch (FormulaException ex)
        {
            r.Errores.Add(ex.Message);
            return r;
        }

        r.Van = van;
        r.Ivan = inversion != 0 ? van / inversion : 0;
        return r;
    }

    /// <summary>
    /// Modo por escenarios (Anexo 2 de ENAMI). Igual que la planilla:
    /// <list type="number">
    /// <item>la tabla tiene 8 precios: el precio proyectado redondeado, 4 pasos hacia abajo y 3 hacia arriba (paso = PasoPrecio, por defecto 20),
    ///       más una última fila con el precio proyectado exacto;</item>
    /// <item>en cada fila se calculan todas las fórmulas con el identificador «Precio» igual al precio de la fila;</item>
    /// <item>la recuperación y el VAN son los de la fila del precio proyectado;</item>
    /// <item>el precio crítico es la intersección (regresión lineal, como INTERSECCION.EJE de Excel) del precio con la utilidad
    ///       de las 8 filas, y el precio mínimo de retorno, la del precio con el saldo del financiamiento; si sale negativo, se informa 0.</item>
    /// </list>
    /// </summary>
    private static ResultadoEvaluacion EvaluarEscenarios(ResultadoEvaluacion r, MotorFormulas.Compilado compilado, Dictionary<string, double> valores)
    {
        var arboles = compilado.OrdenCalculo.ToDictionary(x => x.Formula.Codigo, x => x.Arbol, StringComparer.OrdinalIgnoreCase);
        try
        {
            var base_ = new ContextoEscenario(valores, arboles, null);
            double proyectado = base_.ObtenerValor(MotorFormulas.CodigoPrecioProyectado);
            double paso = valores.TryGetValue(MotorFormulas.CodigoPasoPrecio, out var p) && p > 0 ? p : 20;
            if (proyectado <= 0) throw new FormulaException("El precio proyectado debe ser mayor que cero.");
            double centro = Math.Round(proyectado, 0, MidpointRounding.AwayFromZero);
            var precios = Enumerable.Range(-4, 8).Select(k => centro + k * paso).Append(proyectado).ToList();
            for (int i = 0; i < precios.Count; i++)
            {
                var ctx = new ContextoEscenario(valores, arboles, precios[i]);
                foreach (var (f, _) in compilado.OrdenCalculo)
                {
                    double y = ctx.ObtenerValor(f.Codigo);
                    if (double.IsNaN(y) || double.IsInfinity(y))
                        throw new FormulaException($"La fórmula «{f.Codigo}» no entrega un número válido con el precio {Formato(precios[i])}.");
                }
                r.Escenarios.Add(new EscenarioPrecio(precios[i], i == precios.Count - 1, ctx.Memoria));
            }
            var fila = r.Escenarios[^1].ValoresFormulas;
            r.PrecioProyectado = proyectado;
            // la plantilla del Anexo 2 indica el producto: 3 = oro (precio en US$/oz); el resto se valoriza por el cobre (¢US$/lb)
            r.UnidadPrecio = valores.TryGetValue("Producto", out var producto) ? (producto == 3 ? "US$/oz" : "¢US$/lb") : "";
            r.Recuperacion = fila[MotorFormulas.CodigoRecuperacion];
            r.Van = fila[MotorFormulas.CodigoVanProyecto];
            r.Inversion = base_.ObtenerValor(MotorFormulas.CodigoFinanciamiento);
            r.Ivan = r.Inversion != 0 ? r.Van / r.Inversion : 0;
            var tabla = r.Escenarios.Take(8).ToList();
            r.PrecioCritico = Interseccion(tabla.Select(e => e.Precio).ToList(), tabla.Select(e => e.ValoresFormulas[MotorFormulas.CodigoUtilidad]).ToList());
            r.PrecioMinimoRetorno = Interseccion(tabla.Select(e => e.Precio).ToList(), tabla.Select(e => e.ValoresFormulas[MotorFormulas.CodigoSaldo]).ToList());
        }
        catch (FormulaException ex)
        {
            r.Errores.Add(ex.Message);
        }
        return r;
    }

    /// <summary>INTERSECCION.EJE de Excel: valor de y cuando x = 0 en la recta de mínimos cuadrados; null si todos los x son iguales; 0 si es negativo.</summary>
    public static double? Interseccion(IReadOnlyList<double> y, IReadOnlyList<double> x)
    {
        double mx = x.Average(), my = y.Average();
        double sxx = x.Sum(v => (v - mx) * (v - mx));
        if (sxx < 1e-12) return null;
        double b = x.Zip(y, (a, c) => (a - mx) * (c - my)).Sum() / sxx;
        return Math.Max(0, my - b * mx);
    }

    /// <summary>Calcula cada fórmula cuando se la necesita y guarda el resultado (las referencias circulares ya se descartaron al compilar).</summary>
    private sealed class ContextoEscenario(IReadOnlyDictionary<string, double> variables, IReadOnlyDictionary<string, Expresiones.Nodo> arboles, double? precio)
        : IContextoEvaluacion
    {
        public Dictionary<string, double> Memoria { get; } = new(StringComparer.OrdinalIgnoreCase);

        public double ObtenerValor(string id)
        {
            if (id.Equals(MotorFormulas.CodigoPrecio, StringComparison.OrdinalIgnoreCase))
                return precio ?? throw new FormulaException("El precio proyectado y el financiamiento no pueden depender de «Precio».");
            if (Memoria.TryGetValue(id, out var m)) return m;
            if (arboles.TryGetValue(id, out var arbol))
            {
                double y = arbol.Evaluar(this);
                Memoria[id] = y;
                return y;
            }
            if (variables.TryGetValue(id, out var v)) return v;
            throw new FormulaException($"«{id}» no tiene valor.");
        }
    }

    private static string Formato(double x) => x.ToString("#,##0.##", CultureInfo.GetCultureInfo("es-CL"));

    private sealed class Contexto(IReadOnlyDictionary<string, double> variables, int periodo) : IContextoEvaluacion
    {
        public Dictionary<string, double> Formulas { get; } = new(StringComparer.OrdinalIgnoreCase);

        public double ObtenerValor(string id)
        {
            if (id.Equals(MotorFormulas.CodigoPeriodo, StringComparison.OrdinalIgnoreCase)) return periodo;
            if (Formulas.TryGetValue(id, out var f)) return f;
            if (variables.TryGetValue(id, out var v)) return v;
            throw new FormulaException($"«{id}» no tiene valor.");
        }
    }
}
