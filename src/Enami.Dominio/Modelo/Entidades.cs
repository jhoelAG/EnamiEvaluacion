namespace Enami.Dominio.Modelo;

// Entidades del modelo de datos normalizado en 3FN (sección 3.3 y Anexo A del informe).
// Cada clase corresponde a una tabla. Las relaciones se guardan como claves foráneas (Id...),
// para que la misma lógica funcione con SQL Server (EF Core) y con el almacén en memoria.

/// <summary>OFICINA: oficina de fomento de ENAMI.</summary>
public class Oficina
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Region { get; set; } = "";
}

/// <summary>COMUNA: comuna y su región.</summary>
public class Comuna
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Region { get; set; } = "";
}

/// <summary>ROL: rol del usuario y su nivel en la cadena de aprobación.</summary>
public class Rol
{
    public byte Id { get; set; }
    public string Nombre { get; set; } = "";
    /// <summary>1 = Ingeniero, 2 = Jefe de Oficina, 3 = Subgerente; nulo si el rol no aprueba.</summary>
    public byte? NivelAprobacion { get; set; }
}

/// <summary>Nombres de los cinco roles (columna ROL.nombre_rol).</summary>
public static class Roles
{
    public const string Ingeniero = "INGENIERO";
    public const string JefeOficina = "JEFE_OFICINA";
    public const string Subgerente = "SUBGERENTE";
    public const string Jefatura = "JEFATURA";
    public const string Administrador = "ADMINISTRADOR";

    public static readonly string[] Todos = [Ingeniero, JefeOficina, Subgerente, Jefatura, Administrador];

    /// <summary>Roles que trabajan asociados a una oficina (los demás tienen alcance nacional).</summary>
    public static bool RequiereOficina(string rol) => rol is Ingeniero or JefeOficina;

    public static string Nombre(string rol) => rol switch
    {
        Ingeniero => "Ingeniero de Fomento",
        JefeOficina => "Jefe de Oficina",
        Subgerente => "Subgerente",
        Jefatura => "Jefatura de Fomento",
        Administrador => "Administrador",
        _ => rol
    };
}

/// <summary>USUARIO: persona interna que registra, revisa o aprueba solicitudes.</summary>
public class Usuario
{
    public int Id { get; set; }
    public string Correo { get; set; } = "";
    public string NombreCompleto { get; set; } = "";
    public byte IdRol { get; set; }
    public int? IdOficina { get; set; }
    public bool Activo { get; set; } = true;
    /// <summary>Hash de la contraseña (formato del PasswordHasher de ASP.NET Core Identity). Nunca se guarda la clave en texto.</summary>
    public string ClaveHash { get; set; } = "";
}

/// <summary>PRODUCTOR: cliente de ENAMI que solicita el crédito.</summary>
public class Productor
{
    public int Id { get; set; }
    public string Rut { get; set; } = "";
    /// <summary>NATURAL o EMPRESA.</summary>
    public string TipoProductor { get; set; } = TiposProductor.Natural;
    public string? Nombres { get; set; }
    public string? Apellidos { get; set; }
    public string? RazonSocial { get; set; }
    public string? RepresentanteLegal { get; set; }
    public int? IdComuna { get; set; }
    /// <summary>Domicilio, teléfono y correo de la ficha del productor (columnas «Domicilio Particular», «Fono…» y «Correo…»).</summary>
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public string? Correo { get; set; }
    /// <summary>AL_DIA, MOROSO o JUDICIAL. Un productor MOROSO o JUDICIAL no puede solicitar.</summary>
    public string NivelCumplimiento { get; set; } = NivelesCumplimiento.AlDia;

    public string NombreMostrado => TipoProductor == TiposProductor.Empresa
        ? RazonSocial ?? "(sin razón social)"
        : string.Join(" ", new[] { Nombres, Apellidos }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public static class TiposProductor
{
    public const string Natural = "NATURAL";
    public const string Empresa = "EMPRESA";
}

public static class NivelesCumplimiento
{
    public const string AlDia = "AL_DIA";
    public const string Moroso = "MOROSO";
    public const string Judicial = "JUDICIAL";
}

/// <summary>FAENA: faena minera para la que se pide el crédito, con su padrón vigente.</summary>
public class Faena
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public int IdProductor { get; set; }
    public int? IdComuna { get; set; }
    public int? IdOficina { get; set; }
    /// <summary>ACTIVA o INACTIVA.</summary>
    public string Estado { get; set; } = EstadosFaena.Activa;
    public decimal? DistanciaPoderCompraKm { get; set; }
    public string? NroPadron { get; set; }
    public DateTime? VencimientoPadron { get; set; }
    /// <summary>Ubicación de la ficha de faena (sierra y localidad).</summary>
    public string? Ubicacion { get; set; }
    /// <summary>Coordenadas UTM de la ficha de faena (columnas «Norte» y «Este»).</summary>
    public decimal? CoordenadaNorte { get; set; }
    public decimal? CoordenadaEste { get; set; }
    /// <summary>Mano de obra contratada (formal más informal) según la ficha de faena.</summary>
    public short? ManoObra { get; set; }
}

public static class EstadosFaena
{
    public const string Activa = "ACTIVA";
    public const string Inactiva = "INACTIVA";
}

/// <summary>
/// RECEPCION_MINERAL: lote de mineral que la faena entregó en un poder de compra de ENAMI (4_Recepción_Minerales.xlsx).
/// Con los lotes de los últimos 6 meses se obtiene la producción mensual y las leyes medias de la faena.
/// </summary>
public class RecepcionMineral
{
    public long Id { get; set; }
    /// <summary>Número de lote; único en la fuente.</summary>
    public string NroLote { get; set; } = "";
    public int IdFaena { get; set; }
    public DateTime FechaCierre { get; set; }
    /// <summary>Código de la agencia o poder de compra que recibió el lote.</summary>
    public string Agencia { get; set; } = "";
    /// <summary>Código de producto de ENAMI (por ejemplo, 4 = mineral de cobre para flotación).</summary>
    public string Producto { get; set; } = "";
    /// <summary>Peso seco en kilogramos; mayor que 0.</summary>
    public decimal PesoSecoKg { get; set; }
    /// <summary>Ley de cobre en %; entre 0 y menos de 100.</summary>
    public decimal LeyCuPct { get; set; }
    /// <summary>Ley de oro en g/t.</summary>
    public decimal LeyAuGt { get; set; }
    /// <summary>Ley de plata en g/t.</summary>
    public decimal LeyAgGt { get; set; }
}

/// <summary>RECURSO_FAENA: recursos de la faena por categoría (medido, indicado o inferido), con sus leyes.</summary>
public class RecursoFaena
{
    public int Id { get; set; }
    public int IdFaena { get; set; }
    /// <summary>MEDIDO, INDICADO o INFERIDO.</summary>
    public string Categoria { get; set; } = CategoriasRecurso.Medido;
    public decimal Toneladas { get; set; }
    public decimal LeyCuPct { get; set; }
    public decimal LeyAuGt { get; set; }
    public decimal LeyAgGt { get; set; }
    /// <summary>De dónde sale el dato (por ejemplo, «Informe geológico 2025» o «Visita técnica»).</summary>
    public string? Fuente { get; set; }
    public int IdUsuario { get; set; }
    public DateTime FechaRegistro { get; set; }
}

public static class CategoriasRecurso
{
    public const string Medido = "MEDIDO";
    public const string Indicado = "INDICADO";
    public const string Inferido = "INFERIDO";
    public static readonly string[] Todas = [Medido, Indicado, Inferido];
    public static string Nombre(string c) => c switch { Medido => "Medido", Indicado => "Indicado", Inferido => "Inferido", _ => c };
}

/// <summary>
/// INSTRUMENTO_FOMENTO: instrumento con que ENAMI financia proyectos (nueve al iniciar; la Jefatura puede agregar más).
/// Cada instrumento tiene sus propias versiones de fórmulas (PLANTILLA_CALCULO).
/// </summary>
public class InstrumentoFomento
{
    public byte Id { get; set; }
    public string Nombre { get; set; } = "";
    public string? Descripcion { get; set; }
    /// <summary>Un instrumento inactivo no se ofrece en las evaluaciones nuevas; las anteriores lo conservan.</summary>
    public bool Activo { get; set; } = true;
}

/// <summary>CAUSAL_RECHAZO: catálogo de causales de no viabilidad o rechazo.</summary>
public class CausalRechazo
{
    public short Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    /// <summary>TECNICA, FINANCIERA o LEGAL.</summary>
    public string Tipo { get; set; } = "";
}

/// <summary>SOLICITUD_CREDITO: solicitud de financiamiento que se revisa y aprueba.</summary>
public class SolicitudCredito
{
    public long Id { get; set; }
    public string NroSolicitud { get; set; } = "";
    public int IdFaena { get; set; }
    public byte IdInstrumento { get; set; }
    public int IdIngeniero { get; set; }
    public decimal MontoSolicitado { get; set; }
    public short PlazoMeses { get; set; }
    public DateTime FechaIngreso { get; set; }
    /// <summary>BORRADOR, EN_REVISION, OBSERVADA, APROBADA o RECHAZADA.</summary>
    public string Estado { get; set; } = EstadosSolicitud.Borrador;
    public short? IdCausal { get; set; }
    public DateTime? FechaDictamen { get; set; }
}

public static class EstadosSolicitud
{
    public const string Borrador = "BORRADOR";
    public const string EnRevision = "EN_REVISION";
    public const string Observada = "OBSERVADA";
    public const string Aprobada = "APROBADA";
    public const string Rechazada = "RECHAZADA";
}

/// <summary>PLANTILLA_CALCULO: versión de las fórmulas de un instrumento de fomento.</summary>
public class VersionPlantilla
{
    public int Id { get; set; }
    public byte IdInstrumento { get; set; }
    public short Version { get; set; }
    /// <summary>BORRADOR, VIGENTE o RETIRADA.</summary>
    public string Estado { get; set; } = "BORRADOR";
    public int IdAutor { get; set; }
    public int? IdAprobador { get; set; }
    public DateTime? FechaAprobacion { get; set; }
    /// <summary>FLUJO (VAN por período) o ESCENARIOS (Anexo 2: serie de precios).</summary>
    public string ModoCalculo { get; set; } = "FLUJO";
}

/// <summary>VARIABLE_CALCULO: dato de entrada que usan las fórmulas de una versión.</summary>
public class VariablePlantilla
{
    public int Id { get; set; }
    public int IdPlantilla { get; set; }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Unidad { get; set; } = "";
    /// <summary>FORMULARIO, MANTENEDOR o ROBOT.</summary>
    public string Origen { get; set; } = "FORMULARIO";
    public decimal? Minimo { get; set; }
    public decimal? Maximo { get; set; }
    /// <summary>Valor de referencia que se propone en el formulario y en el simulador.</summary>
    public decimal? ValorReferencia { get; set; }
    /// <summary>Sección del formulario (Sprint 5: formulario configurable).</summary>
    public string? Seccion { get; set; }
    public short Orden { get; set; }
    /// <summary>NUMERO, SELECCION, TEXTO o TEXTO_LARGO.</summary>
    public string Tipo { get; set; } = "NUMERO";
    /// <summary>Precarga: RECEPCION.TMS_MES, RECURSO.INDICADO, PRESUPUESTO.CREDITO, etc.</summary>
    public string? Fuente { get; set; }
    /// <summary>Opciones de un campo de lista: «1=Cobre;3=Oro».</summary>
    public string? Opciones { get; set; }
}

/// <summary>
/// DATO_SOLICITUD: valor de cada campo del formulario de una solicitud, aunque el cálculo aún no esté completo.
/// Los campos numéricos usan valor_numero y los de texto, valor_texto.
/// </summary>
public class DatoSolicitud
{
    public long IdSolicitud { get; set; }
    public string Codigo { get; set; } = "";
    public decimal? ValorNumero { get; set; }
    public string? ValorTexto { get; set; }
}

/// <summary>LABOR_TARIFADA: actividad que se puede financiar, con su costo medio (planilla «Tarificación» de ENAMI).</summary>
public class LaborTarifada
{
    public short Id { get; set; }
    public string Nombre { get; set; } = "";
    /// <summary>m, m3, hr, c/u, US$.</summary>
    public string Unidad { get; set; } = "";
    /// <summary>Costo medio en US$ por unidad.</summary>
    public decimal CostoUnitario { get; set; }
    /// <summary>Programa por defecto: RECONOCIMIENTO, DESARROLLO o PROPIO.</summary>
    public string Programa { get; set; } = ProgramasPresupuesto.Reconocimiento;
    /// <summary>Fracción que financia ENAMI por defecto (0,8 = 80 %).</summary>
    public decimal PorcentajeFinanciamiento { get; set; } = 1;
    public bool Activa { get; set; } = true;
}

/// <summary>PRESUPUESTO_SOLICITUD: actividad del Anexo 1 de una solicitud.</summary>
public class LineaPresupuesto
{
    public long Id { get; set; }
    public long IdSolicitud { get; set; }
    public short IdLabor { get; set; }
    public string Programa { get; set; } = ProgramasPresupuesto.Reconocimiento;
    public decimal Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
    public decimal PorcentajeFinanciamiento { get; set; } = 1;
}

/// <summary>Cómo se financia una actividad del presupuesto (Anexo 1).</summary>
public static class ProgramasPresupuesto
{
    /// <summary>Reconocimiento de reservas: ENAMI aporta capital de riesgo (truncado a dólares enteros por unidad, como la planilla).</summary>
    public const string Reconocimiento = "RECONOCIMIENTO";
    /// <summary>Crédito para el desarrollo: ENAMI presta la fracción indicada del costo; el beneficiario lo devuelve.</summary>
    public const string Desarrollo = "DESARROLLO";
    /// <summary>Lo paga el beneficiario (por ejemplo, gastos notariales).</summary>
    public const string Propio = "PROPIO";
    public static readonly string[] Todos = [Reconocimiento, Desarrollo, Propio];
    public static string Nombre(string p) => p switch
    {
        Reconocimiento => "Reconocimiento (capital de riesgo)", Desarrollo => "Crédito para el desarrollo", Propio => "Aporte propio", _ => p
    };
    public static string NombreCorto(string p) => p switch
    {
        Reconocimiento => "Reconocimiento", Desarrollo => "Crédito desarrollo", Propio => "Aporte propio", _ => p
    };
}

/// <summary>FORMULA_CALCULO: fórmula editable de una versión.</summary>
public class FormulaPlantilla
{
    public int Id { get; set; }
    public int IdPlantilla { get; set; }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Expresion { get; set; } = "";
}

/// <summary>RESULTADO_EVALUACION: cada cálculo financiero guardado de una solicitud.</summary>
public class ResultadoCalculo
{
    public long Id { get; set; }
    public long IdSolicitud { get; set; }
    public int IdPlantilla { get; set; }
    public DateTime FechaCalculo { get; set; }
    public decimal Van { get; set; }
}

/// <summary>VALOR_ENTRADA: valor de cada variable usado en un cálculo (clave compuesta).</summary>
public class ValorEntrada
{
    public long IdResultado { get; set; }
    public int IdVariable { get; set; }
    public decimal Valor { get; set; }
}

/// <summary>APROBACION: decisión de un nivel de la cadena de aprobación.</summary>
public class Aprobacion
{
    public long Id { get; set; }
    public long IdSolicitud { get; set; }
    public int IdUsuario { get; set; }
    public DateTime FechaAsignacion { get; set; }
    public DateTime? FechaDecision { get; set; }
    /// <summary>APROBADA, RECHAZADA u OBSERVADA; nulo mientras está pendiente.</summary>
    public string? Decision { get; set; }
    public string? Comentario { get; set; }
}

/// <summary>PARAMETRO_ECONOMICO: valor diario de una variable de mercado o de mantenedor.</summary>
public class ParametroEconomico
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public string CodigoVariable { get; set; } = "";
    public decimal Valor { get; set; }
    /// <summary>API_BCCH, RPA_COCHILCO, API_EXTERNA (tipo de cambio de mercado, respaldo del Banco Central) o MANUAL.</summary>
    public string Fuente { get; set; } = "MANUAL";
    /// <summary>Día al que corresponde el valor según la API (por ejemplo, el dólar observado de una fecha elegida). Nulo si se ingresó a mano.</summary>
    public DateTime? FechaValor { get; set; }
    /// <summary>
    /// Usuario que fijó el valor (a mano o eligiendo una fecha). Nulo si lo registró un robot o la consulta automática.
    /// Mientras el último valor de una variable esté fijado por una persona, la consulta automática no lo reemplaza.
    /// </summary>
    public int? IdUsuario { get; set; }
}

/// <summary>AUDITORIA: registro inmodificable de las acciones de usuarios y robots.</summary>
public class Auditoria
{
    public long Id { get; set; }
    public DateTime Fecha { get; set; }
    public int? IdUsuario { get; set; }
    public string? Robot { get; set; }
    public string Accion { get; set; } = "";
    public long? IdSolicitud { get; set; }
    public string? Detalle { get; set; }
}
