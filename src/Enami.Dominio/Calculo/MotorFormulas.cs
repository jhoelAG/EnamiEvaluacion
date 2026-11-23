using System.Text.RegularExpressions;
using Enami.Dominio.Calculo.Expresiones;

namespace Enami.Dominio.Calculo;

/// <summary>Problema detectado al validar una plantilla. Formula/Variable indican dónde está.</summary>
public record ErrorValidacion(string Elemento, string Mensaje, int Posicion = -1)
{
    public override string ToString() => string.IsNullOrEmpty(Elemento) ? Mensaje : $"{Elemento}: {Mensaje}";
}

/// <summary>
/// Compila y valida las fórmulas de una plantilla: sintaxis, identificadores desconocidos,
/// códigos duplicados, referencias circulares y fórmulas obligatorias. Entrega además el orden de cálculo.
/// </summary>
public static partial class MotorFormulas
{
    /// <summary>Fórmula obligatoria: flujo de caja de cada período.</summary>
    public const string CodigoFlujo = "FlujoPeriodo";
    /// <summary>Fórmula obligatoria: tasa de descuento por período (por ejemplo, mensual).</summary>
    public const string CodigoTasa = "TasaPeriodo";
    /// <summary>Variable obligatoria: inversión inicial (período 0).</summary>
    public const string CodigoInversion = "Inversion";
    /// <summary>Variable obligatoria: cantidad de períodos del flujo.</summary>
    public const string CodigoPlazo = "PlazoMeses";
    /// <summary>Identificador reservado: número del período que se está calculando (1, 2, 3...).</summary>
    public const string CodigoPeriodo = "Periodo";

    // --- modo por escenarios de precio (Anexo 2) ---
    /// <summary>Identificador reservado: precio de la fila de la tabla de escenarios que se está calculando.</summary>
    public const string CodigoPrecio = "Precio";
    /// <summary>Variable o fórmula obligatoria: precio proyectado; la tabla de precios se arma a su alrededor.</summary>
    public const string CodigoPrecioProyectado = "PrecioProyectado";
    /// <summary>Variable opcional: distancia entre los precios de la tabla (por defecto 20, como la planilla).</summary>
    public const string CodigoPasoPrecio = "PasoPrecio";
    /// <summary>Obligatoria: utilidad por tonelada; el precio crítico es el precio con el que vale cero.</summary>
    public const string CodigoUtilidad = "Utilidad";
    /// <summary>Obligatoria: recuperación esperada del fomento (US$).</summary>
    public const string CodigoRecuperacion = "Recuperacion";
    /// <summary>Obligatoria: financiamiento menos la capacidad de pago; el precio mínimo de retorno es el precio con el que vale cero.</summary>
    public const string CodigoSaldo = "SaldoFinanciamiento";
    /// <summary>Obligatoria: VAN del proyecto (US$).</summary>
    public const string CodigoVanProyecto = "VanProyecto";
    /// <summary>Variable o fórmula obligatoria: monto que financia ENAMI (US$).</summary>
    public const string CodigoFinanciamiento = "Financiamiento";

    private static bool EsReservado(string id, ModoCalculo modo) =>
        id.Equals(CodigoPeriodo, StringComparison.OrdinalIgnoreCase)
        || (modo == ModoCalculo.Escenarios && id.Equals(CodigoPrecio, StringComparison.OrdinalIgnoreCase));

    // largos máximos de las columnas de VARIABLE_CALCULO y FORMULA_CALCULO
    public const int LargoCodigo = 30, LargoNombre = 80, LargoUnidad = 15, LargoSeccion = 60, LargoOpciones = 200, LargoExpresion = 500;

    [GeneratedRegex(@"^[A-Za-zÁÉÍÓÚáéíóúÑñ_][A-Za-z0-9ÁÉÍÓÚáéíóúÑñ_]*$")]
    private static partial Regex RegexCodigo();

    public sealed class Compilado
    {
        public required IReadOnlyList<(FormulaCalculo Formula, Nodo Arbol)> OrdenCalculo { get; init; }
        public required IReadOnlyList<ErrorValidacion> Errores { get; init; }
        public bool EsValido => Errores.Count == 0;
    }

    /// <summary>Identificadores que usan las fórmulas de la plantilla (las que no se pueden leer se ignoran).</summary>
    public static HashSet<string> IdentificadoresUsados(PlantillaCalculo plantilla)
    {
        var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in plantilla.Formulas)
        {
            try { Parser.Analizar(f.Expresion).RecolectarIdentificadores(usados); }
            catch (FormulaException) { }
        }
        return usados;
    }

    /// <summary>Valida solo la sintaxis de una expresión (útil para la validación en vivo del editor).</summary>
    public static ErrorValidacion? ValidarSintaxis(string expresion)
    {
        try
        {
            Parser.Analizar(expresion);
            return null;
        }
        catch (FormulaException ex)
        {
            return new ErrorValidacion("", ex.Message, ex.Posicion);
        }
    }

    public static Compilado Compilar(PlantillaCalculo plantilla)
    {
        var errores = new List<ErrorValidacion>();
        var variables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var textos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modo = plantilla.Modo;
        var formulas = new Dictionary<string, (FormulaCalculo F, Nodo? Arbol, HashSet<string> Usa)>(StringComparer.OrdinalIgnoreCase);

        foreach (var v in plantilla.Variables)
        {
            if (!RegexCodigo().IsMatch(v.Codigo))
                errores.Add(new("Variable " + v.Codigo, "El código debe empezar con una letra y contener solo letras, números o guion bajo."));
            else if (variables.Contains(v.Codigo) || textos.Contains(v.Codigo))
                errores.Add(new("Variable " + v.Codigo, "El código está repetido."));
            else if (EsReservado(v.Codigo, modo))
                errores.Add(new("Variable " + v.Codigo, "El código es un identificador reservado."));
            else if (v.EsNumerica) variables.Add(v.Codigo);
            else textos.Add(v.Codigo);
            if (v.Tipo == TiposCampo.Seleccion && v.ListaOpciones().Count == 0)
                errores.Add(new("Variable " + v.Codigo, "Un campo de lista necesita opciones con el formato «1=Texto;2=Otro texto»."));
            foreach (var (texto, max, campo) in new[] { (v.Codigo, LargoCodigo, "El código"), (v.Nombre, LargoNombre, "El nombre"), (v.Unidad, LargoUnidad, "La unidad"),
                                                         (v.Seccion, LargoSeccion, "La sección"), (v.Opciones ?? "", LargoOpciones, "Las opciones") })
                if ((texto ?? "").Trim().Length > max) errores.Add(new("Variable " + v.Codigo, $"{campo} admite hasta {max} caracteres."));
            if (v.Minimo is not null && v.Maximo is not null && v.Minimo > v.Maximo)
                errores.Add(new("Variable " + v.Codigo, "El mínimo es mayor que el máximo."));
        }

        foreach (var f in plantilla.Formulas)
        {
            if (!RegexCodigo().IsMatch(f.Codigo))
            {
                errores.Add(new(f.Codigo, "El código debe empezar con una letra y contener solo letras, números o guion bajo."));
                continue;
            }
            if (variables.Contains(f.Codigo) || textos.Contains(f.Codigo) || EsReservado(f.Codigo, modo) || Funciones.Existe(f.Codigo))
            {
                errores.Add(new(f.Codigo, modo == ModoCalculo.Escenarios
                    ? "El código ya está usado por una variable, una función o un identificador reservado («Periodo» o «Precio»)."
                    : "El código ya está usado por una variable, una función o el identificador reservado «Periodo»."));
                continue;
            }
            if (formulas.ContainsKey(f.Codigo))
            {
                errores.Add(new(f.Codigo, "El código está repetido."));
                continue;
            }
            if (f.Codigo.Length > LargoCodigo) errores.Add(new(f.Codigo, $"El código admite hasta {LargoCodigo} caracteres."));
            if (f.Nombre.Trim().Length > LargoNombre) errores.Add(new(f.Codigo, $"El nombre admite hasta {LargoNombre} caracteres."));
            if (f.Expresion.Trim().Length > LargoExpresion) errores.Add(new(f.Codigo, $"La expresión admite hasta {LargoExpresion} caracteres; divídala en fórmulas más cortas."));
            try
            {
                var arbol = Parser.Analizar(f.Expresion);
                var usa = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                arbol.RecolectarIdentificadores(usa);
                formulas[f.Codigo] = (f, arbol, usa);
            }
            catch (FormulaException ex)
            {
                errores.Add(new(f.Codigo, ex.Message, ex.Posicion));
                formulas[f.Codigo] = (f, null, []);
            }
        }

        // identificadores desconocidos
        foreach (var (codigo, (_, arbol, usa)) in formulas)
        {
            if (arbol is null) continue;
            foreach (var id in usa)
            {
                if (textos.Contains(id))
                    errores.Add(new(codigo, $"«{id}» es un campo de texto y no se puede usar en un cálculo."));
                else if (!variables.Contains(id) && !formulas.ContainsKey(id) && !EsReservado(id, modo))
                    errores.Add(new(codigo, $"«{id}» no es una variable ni una fórmula de la plantilla."));
            }
        }

        // obligatorias
        if (modo == ModoCalculo.Flujo)
        {
            if (!formulas.ContainsKey(CodigoFlujo)) errores.Add(new("", $"Falta la fórmula obligatoria «{CodigoFlujo}» (flujo de caja de cada período)."));
            if (!formulas.ContainsKey(CodigoTasa)) errores.Add(new("", $"Falta la fórmula obligatoria «{CodigoTasa}» (tasa de descuento por período)."));
            if (!variables.Contains(CodigoInversion)) errores.Add(new("", $"Falta la variable obligatoria «{CodigoInversion}» (inversión inicial)."));
            if (!variables.Contains(CodigoPlazo)) errores.Add(new("", $"Falta la variable obligatoria «{CodigoPlazo}» (cantidad de períodos)."));
        }
        else
        {
            foreach (var (c, d) in new[] { (CodigoUtilidad, "utilidad por tonelada"), (CodigoRecuperacion, "recuperación del fomento"),
                                           (CodigoSaldo, "financiamiento menos la capacidad de pago"), (CodigoVanProyecto, "VAN del proyecto") })
                if (!formulas.ContainsKey(c)) errores.Add(new("", $"Falta la fórmula obligatoria «{c}» ({d})."));
            foreach (var (c, d) in new[] { (CodigoPrecioProyectado, "precio proyectado"), (CodigoFinanciamiento, "monto que financia ENAMI") })
                if (!formulas.ContainsKey(c) && !variables.Contains(c)) errores.Add(new("", $"Falta la variable o fórmula obligatoria «{c}» ({d})."));
            if (formulas.TryGetValue(CodigoPrecioProyectado, out var pp) && pp.Usa.Contains(CodigoPrecio))
                errores.Add(new(CodigoPrecioProyectado, "El precio proyectado no puede depender de «Precio»."));
        }

        // orden topológico y referencias circulares
        var orden = new List<(FormulaCalculo, Nodo)>();
        var estado = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // 0 = pendiente, 1 = visitando, 2 = listo
        var ciclosInformados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visitar(string codigo, Stack<string> camino)
        {
            if (estado.TryGetValue(codigo, out int e) && e == 2) return;
            if (e == 1)
            {
                var ciclo = camino.Reverse().SkipWhile(c => !c.Equals(codigo, StringComparison.OrdinalIgnoreCase)).Append(codigo).ToList();
                if (ciclosInformados.Add(codigo))
                    errores.Add(new(codigo, "Referencia circular: " + string.Join(" → ", ciclo) + "."));
                return;
            }
            estado[codigo] = 1;
            camino.Push(codigo);
            var (f, arbol, usa) = formulas[codigo];
            foreach (var dep in usa.Where(formulas.ContainsKey).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                Visitar(dep, camino);
            camino.Pop();
            estado[codigo] = 2;
            if (arbol is not null) orden.Add((f, arbol));
        }

        foreach (var f in plantilla.Formulas.Where(f => formulas.ContainsKey(f.Codigo)))
            Visitar(f.Codigo, new Stack<string>());

        return new Compilado { OrdenCalculo = orden, Errores = errores };
    }
}
