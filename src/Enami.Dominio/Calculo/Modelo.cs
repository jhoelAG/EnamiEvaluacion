namespace Enami.Dominio.Calculo;

/// <summary>De dónde obtiene su valor una variable del cálculo.</summary>
public enum OrigenVariable
{
    /// <summary>La ingresa el Ingeniero de Fomento en el formulario de evaluación.</summary>
    Formulario,
    /// <summary>La mantiene el administrador en el mantenedor de variables (RF-04).</summary>
    Mantenedor,
    /// <summary>La actualiza un robot (BOT-01: Banco Central y COCHILCO).</summary>
    Robot
}

/// <summary>Cómo se calcula el resultado de una plantilla.</summary>
public enum ModoCalculo
{
    /// <summary>Flujo de caja por período: VAN = −Inversión + Σ FlujoPeriodo / (1 + TasaPeriodo)^t.</summary>
    Flujo,
    /// <summary>
    /// Escenarios de precio, como el Anexo 2 de ENAMI: las fórmulas se calculan para una serie de precios alrededor del
    /// precio proyectado; entrega la recuperación del fomento, el VAN del proyecto, el precio crítico y el precio mínimo de retorno.
    /// </summary>
    Escenarios
}

/// <summary>Tipo de dato de un campo del formulario.</summary>
public static class TiposCampo
{
    public const string Numero = "NUMERO";
    /// <summary>Lista de opciones numéricas, por ejemplo «1=Cobre;3=Oro».</summary>
    public const string Seleccion = "SELECCION";
    public const string Texto = "TEXTO";
    public const string TextoLargo = "TEXTO_LARGO";
    public static readonly string[] Todos = [Numero, Seleccion, Texto, TextoLargo];
    public static bool EsNumerico(string? tipo) => tipo is null or "" or Numero or Seleccion;
    public static string Nombre(string? tipo) => tipo switch
    {
        Seleccion => "Lista", Texto => "Texto", TextoLargo => "Texto largo", _ => "Número"
    };
}

/// <summary>
/// De dónde se precarga un campo del formulario. Lo elige la Jefatura en el editor; el ingeniero puede corregir el valor,
/// salvo los del presupuesto, que se calculan siempre.
/// </summary>
public static class FuentesCampo
{
    public static readonly (string Codigo, string Nombre)[] Todas =
    [
        ("USUARIO.NOMBRE", "Usuario: nombre de quien registra la evaluación"),
        ("PRODUCTOR.DIRECCION", "Productor: domicilio"),
        ("PRODUCTOR.COMUNA", "Productor: comuna y región"),
        ("PRODUCTOR.TELEFONO", "Productor: teléfono"),
        ("PRODUCTOR.CORREO", "Productor: correo electrónico"),
        ("PRODUCTOR.REPRESENTANTE", "Productor: representante legal"),
        ("FAENA.UBICACION", "Faena: ubicación (sierra y localidad)"),
        ("FAENA.COMUNA", "Faena: comuna y región"),
        ("FAENA.NORTE", "Faena: coordenada norte (UTM)"),
        ("FAENA.ESTE", "Faena: coordenada este (UTM)"),
        ("FAENA.MANO_OBRA", "Faena: mano de obra contratada (N°)"),
        ("PRODUCTOR.TMS_MES", "Recepción del productor, todas sus faenas: TMS/mes"),
        ("PRODUCTOR.LEY_CU", "Recepción del productor, todas sus faenas: ley de cobre (%)"),
        ("PRODUCTOR.LEY_AU", "Recepción del productor, todas sus faenas: ley de oro (g/t)"),
        ("PRODUCTOR.LEY_AG", "Recepción del productor, todas sus faenas: ley de plata (g/t)"),
        ("RECEPCION.TMS_MES", "Recepción: producción promedio de 6 meses (TMS/mes)"),
        ("RECEPCION.LEY_CU", "Recepción: ley de cobre promedio (%)"),
        ("RECEPCION.LEY_AU", "Recepción: ley de oro promedio (g/t)"),
        ("RECEPCION.LEY_AG", "Recepción: ley de plata promedio (g/t)"),
        ("RECEPCION.MESES", "Recepción: meses con entregas"),
        ("RECURSO.INFERIDO", "Recursos: toneladas inferidas"),
        ("RECURSO.INDICADO", "Recursos: toneladas indicadas"),
        ("RECURSO.MEDIDO", "Recursos: toneladas medidas"),
        ("RECURSO.LEY_CU", "Recursos: ley de cobre (%)"),
        ("RECURSO.LEY_AU", "Recursos: ley de oro (g/t)"),
        ("RECURSO.LEY_AG", "Recursos: ley de plata (g/t)"),
        ("FAENA.DISTANCIA", "Faena: distancia al poder de compra (km)"),
        ("SOLICITUD.MONTO", "Evaluación: monto solicitado (US$)"),
        ("SOLICITUD.PLAZO", "Evaluación: plazo (meses)"),
        ("PRESUPUESTO.CAPITAL_RIESGO", "Presupuesto: aporte ENAMI en capital de riesgo (US$)"),
        ("PRESUPUESTO.CREDITO", "Presupuesto: crédito para el desarrollo (US$)"),
        ("PRESUPUESTO.APORTE_PROPIO", "Presupuesto: aporte del beneficiario (US$)"),
    ];
    /// <summary>Los campos del presupuesto se calculan con las actividades del Anexo 1 y no se editan a mano.</summary>
    public static bool EsCalculada(string? fuente) => fuente?.StartsWith("PRESUPUESTO.", StringComparison.OrdinalIgnoreCase) == true;
    public static string Nombre(string? fuente) => Todas.FirstOrDefault(f => f.Codigo == fuente).Nombre ?? "";
    /// <summary>Fuentes que entregan texto (para campos de texto); el resto entrega números.</summary>
    public static bool EsTexto(string? fuente) => fuente is "USUARIO.NOMBRE" or "PRODUCTOR.DIRECCION" or "PRODUCTOR.COMUNA" or "PRODUCTOR.TELEFONO"
        or "PRODUCTOR.CORREO" or "PRODUCTOR.REPRESENTANTE" or "FAENA.UBICACION" or "FAENA.COMUNA";
}

public enum EstadoPlantilla
{
    Borrador,
    Vigente,
    Retirada
}

/// <summary>Dato de entrada que pueden usar las fórmulas (por ejemplo, LeyCu o Dolar).</summary>
public class VariableCalculo
{
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Unidad { get; set; } = "";
    public OrigenVariable Origen { get; set; }
    public double ValorPorDefecto { get; set; }
    /// <summary>Límite inferior permitido (RF-07). Nulo = sin límite.</summary>
    public double? Minimo { get; set; }
    /// <summary>Límite superior permitido (RF-07). Nulo = sin límite.</summary>
    public double? Maximo { get; set; }
    /// <summary>Sección del formulario donde aparece el campo (por ejemplo, «Recursos minerales»).</summary>
    public string Seccion { get; set; } = "";
    /// <summary>Orden dentro del formulario.</summary>
    public int Orden { get; set; }
    /// <summary>NUMERO, SELECCION, TEXTO o TEXTO_LARGO (ver <see cref="TiposCampo"/>). Solo los numéricos se usan en las fórmulas.</summary>
    public string Tipo { get; set; } = TiposCampo.Numero;
    /// <summary>Precarga del campo (ver <see cref="FuentesCampo"/>); vacío = se ingresa a mano.</summary>
    public string? Fuente { get; set; }
    /// <summary>Opciones de un campo de lista: «1=Mineral de cobre;3=Mineral de oro».</summary>
    public string? Opciones { get; set; }

    public bool EsNumerica => TiposCampo.EsNumerico(Tipo);

    /// <summary>Opciones de un campo de lista como pares valor–texto.</summary>
    public IReadOnlyList<(double Valor, string Texto)> ListaOpciones() =>
        (Opciones ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(o => o.Split('=', 2))
            .Where(p => p.Length == 2 && double.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
            .Select(p => (double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), p[1].Trim())).ToList();

    public VariableCalculo Clonar() => (VariableCalculo)MemberwiseClone();
}

/// <summary>Fórmula editable desde la interfaz. La expresión es texto, por ejemplo «TonelajeMensual * LeyCu / 100».</summary>
public class FormulaCalculo
{
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Expresion { get; set; } = "";
    public string Descripcion { get; set; } = "";

    public FormulaCalculo Clonar() => (FormulaCalculo)MemberwiseClone();
}

/// <summary>
/// Conjunto versionado de variables y fórmulas para un instrumento de fomento.
/// Cada evaluación guarda la versión que usó, para poder reconstruir el cálculo (RF-08 y RNF-08).
/// </summary>
public class PlantillaCalculo
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    /// <summary>Nombre del instrumento de fomento al que pertenece la versión.</summary>
    public string Instrumento { get; set; } = "";
    /// <summary>Clave del instrumento (INSTRUMENTO_FOMENTO).</summary>
    public byte IdInstrumento { get; set; }
    public int Version { get; set; } = 1;
    public EstadoPlantilla Estado { get; set; } = EstadoPlantilla.Borrador;
    public string Autor { get; set; } = "";
    public int IdAutor { get; set; }
    public int? IdAprobador { get; set; }
    public DateTime FechaCreacion { get; set; } = DateTime.Now;
    public string? AprobadaPor { get; set; }
    public DateTime? FechaAprobacion { get; set; }
    public string Comentario { get; set; } = "";
    public ModoCalculo Modo { get; set; } = ModoCalculo.Flujo;
    public List<VariableCalculo> Variables { get; set; } = [];
    public List<FormulaCalculo> Formulas { get; set; } = [];

    /// <summary>Copia profunda para crear una nueva versión en borrador.</summary>
    public PlantillaCalculo NuevaVersion(int nuevoId, string autor) => new()
    {
        Id = nuevoId,
        Nombre = Nombre,
        Instrumento = Instrumento,
        IdInstrumento = IdInstrumento,
        Version = Version + 1,
        Modo = Modo,
        Estado = EstadoPlantilla.Borrador,
        Autor = autor,
        FechaCreacion = DateTime.Now,
        Variables = Variables.Select(v => v.Clonar()).ToList(),
        Formulas = Formulas.Select(f => f.Clonar()).ToList(),
    };
}

/// <summary>Resultado de un período del flujo de caja.</summary>
public record PeriodoFlujo(int Periodo, double Flujo, double FactorDescuento, double FlujoActualizado, IReadOnlyDictionary<string, double> ValoresFormulas);

/// <summary>Una fila de la tabla de precios del modo por escenarios.</summary>
public record EscenarioPrecio(double Precio, bool EsProyectado, IReadOnlyDictionary<string, double> ValoresFormulas);

/// <summary>Resultado completo de una evaluación financiera.</summary>
public class ResultadoEvaluacion
{
    public bool Exitoso => Errores.Count == 0;
    public List<string> Errores { get; } = [];
    public string Plantilla { get; set; } = "";
    public int VersionPlantilla { get; set; }
    public double Inversion { get; set; }
    public int Plazo { get; set; }
    public double Van { get; set; }
    /// <summary>Índice de VAN: VAN dividido por la inversión.</summary>
    public double Ivan { get; set; }
    public ModoCalculo Modo { get; set; } = ModoCalculo.Flujo;
    /// <summary>Modo por escenarios: el resultado exige que la recuperación cubra el financiamiento y que el VAN no sea negativo.</summary>
    public bool Viable => Exitoso && Van >= 0 && (Modo == ModoCalculo.Flujo || Recuperacion >= Inversion - 0.5);
    // --- solo en el modo por escenarios ---
    /// <summary>Recuperación esperada del fomento al precio proyectado (US$).</summary>
    public double Recuperacion { get; set; }
    public double PrecioProyectado { get; set; }
    /// <summary>Precio con el que la utilidad es cero (null si no se puede estimar).</summary>
    public double? PrecioCritico { get; set; }
    /// <summary>Precio con el que la capacidad de pago iguala el financiamiento.</summary>
    public double? PrecioMinimoRetorno { get; set; }
    public List<EscenarioPrecio> Escenarios { get; } = [];
    /// <summary>Unidad del precio de los escenarios (por ejemplo «US$/oz»).</summary>
    public string UnidadPrecio { get; set; } = "";
    public List<PeriodoFlujo> Periodos { get; } = [];
    public Dictionary<string, double> Entradas { get; } = new(StringComparer.OrdinalIgnoreCase);
}
