using Enami.Dominio.Modelo;
using Enami.Dominio.Persistencia;

namespace Enami.Dominio.Calculo;

/// <summary>Acceso a las plantillas de cálculo y a su historial de versiones.</summary>
public interface IRepositorioPlantillas
{
    IReadOnlyList<PlantillaCalculo> Listar();
    PlantillaCalculo? Obtener(int id);
    /// <summary>Versión vigente del instrumento, buscado por su nombre.</summary>
    PlantillaCalculo? ObtenerVigente(string instrumento);
    PlantillaCalculo? ObtenerVigente(byte idInstrumento);
    /// <summary>Crea una versión en borrador a partir de otra (vigente o no). Si ya hay un borrador de ese instrumento, lo devuelve.</summary>
    PlantillaCalculo CrearBorrador(int idOrigen, int idAutor);
    /// <summary>
    /// Crea en borrador la primera versión de un instrumento que aún no tiene fórmulas, copiando los campos y las fórmulas
    /// de otra versión (de cualquier instrumento). La Jefatura la revisa y la activa como cualquier otra versión.
    /// </summary>
    PlantillaCalculo CrearPrimeraVersion(byte idInstrumento, int idOrigen, int idAutor);
    /// <summary>Guarda los cambios de un borrador. Las versiones vigentes o retiradas no se pueden modificar.</summary>
    void GuardarBorrador(PlantillaCalculo borrador);
    /// <summary>Activa un borrador válido (aprobación de la Jefatura) y retira la versión vigente anterior.</summary>
    void Activar(int id, int idAprobador);
    /// <summary>Indica si la versión se puede eliminar y, si no, por qué.</summary>
    string? MotivoNoEliminable(int id);
    /// <summary>Elimina un borrador o una versión retirada que no se usó en ninguna evaluación.</summary>
    void Eliminar(int id);
}

/// <summary>
/// Guarda las plantillas en las tablas PLANTILLA_CALCULO, VARIABLE_CALCULO y FORMULA_CALCULO.
/// Cada versión es inmutable una vez activada: un cambio siempre crea una versión nueva (RF-06 y RF-08).
/// La creación y la aprobación de versiones quedan en la auditoría (RF-27).
/// </summary>
public class RepositorioPlantillas(IAlmacen db, Servicios.ServicioAuditoria? auditoria = null) : IRepositorioPlantillas
{
    private const string Borrador = "BORRADOR", Vigente = "VIGENTE", Retirada = "RETIRADA";

    public IReadOnlyList<PlantillaCalculo> Listar()
    {
        var instrumentos = db.Tabla<InstrumentoFomento>().ToDictionary(t => t.Id, t => t.Nombre);
        var versiones = db.Tabla<VersionPlantilla>().ToList();
        return versiones.Select(v => Cargar(v, instrumentos)).OrderBy(p => p.IdInstrumento).ThenByDescending(p => p.Version).ToList();
    }

    public PlantillaCalculo? Obtener(int id)
    {
        var v = db.Tabla<VersionPlantilla>().FirstOrDefault(x => x.Id == id);
        return v is null ? null : Cargar(v);
    }

    public PlantillaCalculo? ObtenerVigente(string instrumento)
    {
        var i = db.Tabla<InstrumentoFomento>().ToList().FirstOrDefault(t => t.Nombre.Equals(instrumento.Trim(), StringComparison.OrdinalIgnoreCase));
        return i is null ? null : ObtenerVigente(i.Id);
    }

    public PlantillaCalculo? ObtenerVigente(byte idInstrumento)
    {
        var v = db.Tabla<VersionPlantilla>().FirstOrDefault(x => x.IdInstrumento == idInstrumento && x.Estado == Vigente);
        return v is null ? null : Cargar(v);
    }

    public PlantillaCalculo CrearBorrador(int idOrigen, int idAutor)
    {
        var origen = db.Tabla<VersionPlantilla>().FirstOrDefault(x => x.Id == idOrigen)
                     ?? throw new InvalidOperationException("La plantilla no existe.");
        var existente = db.Tabla<VersionPlantilla>().FirstOrDefault(x => x.IdInstrumento == origen.IdInstrumento && x.Estado == Borrador);
        if (existente is not null) return Cargar(existente);

        short version = (short)(db.Tabla<VersionPlantilla>().Where(x => x.IdInstrumento == origen.IdInstrumento).Max(x => (int)x.Version) + 1);
        var nueva = new VersionPlantilla { IdInstrumento = origen.IdInstrumento, Version = version, Estado = Borrador, IdAutor = idAutor, ModoCalculo = origen.ModoCalculo };
        db.EnTransaccion(() =>
        {
            db.Agregar(nueva);
            db.Guardar();
            Copiar(origen.Id, nueva.Id);
            db.Guardar();
        });
        auditoria?.Registrar("VERSION_CREADA", idAutor, detalle: $"{NombreInstrumento(nueva.IdInstrumento)}: borrador v{nueva.Version} creado a partir de la v{origen.Version}");
        return Cargar(nueva);
    }

    public PlantillaCalculo CrearPrimeraVersion(byte idInstrumento, int idOrigen, int idAutor)
    {
        if (!db.Tabla<InstrumentoFomento>().Any(i => i.Id == idInstrumento)) throw new InvalidOperationException("El instrumento no existe.");
        if (db.Tabla<VersionPlantilla>().Any(x => x.IdInstrumento == idInstrumento))
            throw new InvalidOperationException("El instrumento ya tiene fórmulas: cree una nueva versión desde el editor de fórmulas.");
        var origen = db.Tabla<VersionPlantilla>().FirstOrDefault(x => x.Id == idOrigen)
                     ?? throw new InvalidOperationException("La versión de origen no existe.");
        var nueva = new VersionPlantilla { IdInstrumento = idInstrumento, Version = 1, Estado = Borrador, IdAutor = idAutor, ModoCalculo = origen.ModoCalculo };
        db.EnTransaccion(() =>
        {
            db.Agregar(nueva);
            db.Guardar();
            Copiar(origen.Id, nueva.Id);
            db.Guardar();
        });
        auditoria?.Registrar("VERSION_CREADA", idAutor,
            detalle: $"{NombreInstrumento(idInstrumento)}: borrador v1 creado a partir de {NombreInstrumento(origen.IdInstrumento)} v{origen.Version}");
        return Cargar(nueva);
    }

    public void GuardarBorrador(PlantillaCalculo borrador)
    {
        var v = db.Tabla<VersionPlantilla>().FirstOrDefault(x => x.Id == borrador.Id)
                ?? throw new InvalidOperationException("La plantilla no existe.");
        if (v.Estado != Borrador)
            throw new InvalidOperationException("Solo se pueden modificar plantillas en borrador. Cree una nueva versión.");
        // los textos más largos que la columna no se pueden guardar (SQL Server los rechaza)
        var largos = MotorFormulas.Compilar(borrador).Errores.Where(e => e.Mensaje.Contains(" caracteres")).ToList();
        if (largos.Count > 0)
            throw new InvalidOperationException("No se puede guardar: " + string.Join(" ", largos));
        v.ModoCalculo = Modo(borrador.Modo);
        db.EnTransaccion(() =>
        {
            foreach (var f in db.Tabla<FormulaPlantilla>().Where(x => x.IdPlantilla == v.Id).ToList()) db.Eliminar(f);
            foreach (var x in db.Tabla<VariablePlantilla>().Where(x => x.IdPlantilla == v.Id).ToList()) db.Eliminar(x);
            db.Guardar();
            foreach (var x in borrador.Variables) db.Agregar(AFila(v.Id, x));
            foreach (var f in borrador.Formulas) db.Agregar(AFila(v.Id, f));
            db.Guardar();
        });
    }

    public void Activar(int id, int idAprobador)
    {
        var v = db.Tabla<VersionPlantilla>().FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("La plantilla no existe.");
        if (v.Estado != Borrador) throw new InvalidOperationException("Solo se puede activar un borrador.");
        // regla de aprobación: solo la Jefatura de Fomento activa versiones (también se controla en la interfaz)
        var aprobador = db.Tabla<Usuario>().FirstOrDefault(u => u.Id == idAprobador);
        var rolJefatura = db.Tabla<Rol>().FirstOrDefault(r => r.Nombre == Roles.Jefatura);
        if (aprobador is not null && rolJefatura is not null && aprobador.IdRol != rolJefatura.Id)
            throw new InvalidOperationException("Solo la Jefatura de Fomento puede aprobar una versión de fórmulas.");
        var compilado = MotorFormulas.Compilar(Cargar(v));
        if (!compilado.EsValido)
            throw new InvalidOperationException("La plantilla tiene errores y no se puede activar: " + string.Join(" ", compilado.Errores));
        var retiradas = new List<short>();
        db.EnTransaccion(() =>
        {
            foreach (var vigente in db.Tabla<VersionPlantilla>().Where(x => x.IdInstrumento == v.IdInstrumento && x.Estado == Vigente).ToList())
            {
                vigente.Estado = Retirada;
                retiradas.Add(vigente.Version);
            }
            v.Estado = Vigente;
            v.IdAprobador = idAprobador;
            v.FechaAprobacion = DateTime.Now;
            db.Guardar();
        });
        auditoria?.Registrar("VERSION_APROBADA", idAprobador, detalle: $"{NombreInstrumento(v.IdInstrumento)}: v{v.Version} aprobada y vigente"
            + (retiradas.Count > 0 ? $"; se retira la v{string.Join(", v", retiradas)}" : ""));
    }

    private string NombreInstrumento(byte id) => db.Tabla<InstrumentoFomento>().FirstOrDefault(t => t.Id == id)?.Nombre ?? $"Instrumento {id}";

    public string? MotivoNoEliminable(int id)
    {
        var v = db.Tabla<VersionPlantilla>().FirstOrDefault(x => x.Id == id);
        if (v is null) return "La versión no existe.";
        if (v.Estado == Vigente) return "La versión vigente no se puede eliminar: es la que usan las evaluaciones de proyecto. Active otra versión primero.";
        int usos = db.Tabla<ResultadoCalculo>().Count(r => r.IdPlantilla == id);
        if (usos > 0)
            return $"Se usó en {usos} evaluación(es) guardada(s); se conserva para poder revisar cómo se calcularon.";
        return null;
    }

    public void Eliminar(int id)
    {
        if (MotivoNoEliminable(id) is string motivo) throw new InvalidOperationException(motivo);
        db.EnTransaccion(() =>
        {
            foreach (var f in db.Tabla<FormulaPlantilla>().Where(x => x.IdPlantilla == id).ToList()) db.Eliminar(f);
            foreach (var x in db.Tabla<VariablePlantilla>().Where(x => x.IdPlantilla == id).ToList()) db.Eliminar(x);
            db.Guardar();
            db.Eliminar(db.Tabla<VersionPlantilla>().First(x => x.Id == id));
            db.Guardar();
        });
    }

    /// <summary>Registra una plantilla completa como versión vigente (la usa la carga inicial).</summary>
    public int RegistrarVigente(PlantillaCalculo p, byte idInstrumento, int idAutor)
    {
        // si ya hay versiones de ese instrumento, la nueva queda como la siguiente y la vigente anterior se retira
        var anteriores = db.Tabla<VersionPlantilla>().Where(x => x.IdInstrumento == idInstrumento).ToList();
        foreach (var a in anteriores.Where(a => a.Estado == Vigente)) a.Estado = Retirada;
        var v = new VersionPlantilla
        {
            IdInstrumento = idInstrumento, Version = (short)(anteriores.Count == 0 ? 1 : anteriores.Max(a => a.Version) + 1), Estado = Vigente,
            IdAutor = idAutor, IdAprobador = idAutor, FechaAprobacion = DateTime.Now, ModoCalculo = Modo(p.Modo)
        };
        db.Agregar(v);
        db.Guardar();
        foreach (var x in p.Variables) db.Agregar(AFila(v.Id, x));
        foreach (var f in p.Formulas) db.Agregar(AFila(v.Id, f));
        db.Guardar();
        return v.Id;
    }

    private void Copiar(int idOrigen, int idDestino)
    {
        foreach (var x in db.Tabla<VariablePlantilla>().Where(x => x.IdPlantilla == idOrigen).ToList())
            db.Agregar(new VariablePlantilla
            {
                IdPlantilla = idDestino, Codigo = x.Codigo, Nombre = x.Nombre, Unidad = x.Unidad, Origen = x.Origen,
                Minimo = x.Minimo, Maximo = x.Maximo, ValorReferencia = x.ValorReferencia,
                Seccion = x.Seccion, Orden = x.Orden, Tipo = x.Tipo, Fuente = x.Fuente, Opciones = x.Opciones
            });
        foreach (var f in db.Tabla<FormulaPlantilla>().Where(x => x.IdPlantilla == idOrigen).ToList())
            db.Agregar(new FormulaPlantilla { IdPlantilla = idDestino, Codigo = f.Codigo, Nombre = f.Nombre, Expresion = f.Expresion });
    }

    private PlantillaCalculo Cargar(VersionPlantilla v, Dictionary<byte, string>? instrumentos = null)
    {
        string instrumento = instrumentos is not null && instrumentos.TryGetValue(v.IdInstrumento, out var t)
            ? t
            : db.Tabla<InstrumentoFomento>().Where(x => x.Id == v.IdInstrumento).Select(x => x.Nombre).FirstOrDefault() ?? "";
        var nombres = db.Tabla<Usuario>().Where(u => u.Id == v.IdAutor || u.Id == v.IdAprobador)
                        .Select(u => new { u.Id, u.NombreCompleto }).ToList();
        return new PlantillaCalculo
        {
            Id = v.Id,
            Nombre = $"{instrumento} (versión {v.Version})",
            Instrumento = instrumento,
            IdInstrumento = v.IdInstrumento,
            Version = v.Version,
            Estado = v.Estado switch { Vigente => EstadoPlantilla.Vigente, Retirada => EstadoPlantilla.Retirada, _ => EstadoPlantilla.Borrador },
            IdAutor = v.IdAutor,
            Autor = nombres.FirstOrDefault(n => n.Id == v.IdAutor)?.NombreCompleto ?? "",
            IdAprobador = v.IdAprobador,
            AprobadaPor = nombres.FirstOrDefault(n => n.Id == v.IdAprobador)?.NombreCompleto,
            FechaAprobacion = v.FechaAprobacion,
            Modo = v.ModoCalculo == "ESCENARIOS" ? ModoCalculo.Escenarios : ModoCalculo.Flujo,
            Variables = db.Tabla<VariablePlantilla>().Where(x => x.IdPlantilla == v.Id).OrderBy(x => x.Id).ToList().Select(x => new VariableCalculo
            {
                Codigo = x.Codigo, Nombre = x.Nombre, Unidad = x.Unidad,
                Origen = x.Origen switch { "MANTENEDOR" => OrigenVariable.Mantenedor, "ROBOT" => OrigenVariable.Robot, _ => OrigenVariable.Formulario },
                Minimo = (double?)x.Minimo, Maximo = (double?)x.Maximo, ValorPorDefecto = (double)(x.ValorReferencia ?? 0),
                Seccion = x.Seccion ?? "", Orden = x.Orden, Tipo = string.IsNullOrEmpty(x.Tipo) ? TiposCampo.Numero : x.Tipo,
                Fuente = x.Fuente, Opciones = x.Opciones
            }).ToList(),
            Formulas = db.Tabla<FormulaPlantilla>().Where(x => x.IdPlantilla == v.Id).OrderBy(x => x.Id).ToList()
                .Select(f => new FormulaCalculo { Codigo = f.Codigo, Nombre = f.Nombre, Expresion = f.Expresion }).ToList(),
        };
    }

    private static VariablePlantilla AFila(int idPlantilla, VariableCalculo x) => new()
    {
        IdPlantilla = idPlantilla, Codigo = x.Codigo.Trim(), Nombre = x.Nombre.Trim(), Unidad = x.Unidad.Trim(),
        Origen = x.Origen.ToString().ToUpperInvariant(),
        Minimo = (decimal?)x.Minimo, Maximo = (decimal?)x.Maximo, ValorReferencia = (decimal)x.ValorPorDefecto,
        Seccion = string.IsNullOrWhiteSpace(x.Seccion) ? null : x.Seccion.Trim(), Orden = (short)Math.Clamp(x.Orden, short.MinValue, short.MaxValue),
        Tipo = TiposCampo.Todos.Contains(x.Tipo) ? x.Tipo : TiposCampo.Numero,
        Fuente = string.IsNullOrWhiteSpace(x.Fuente) ? null : x.Fuente.Trim(),
        Opciones = string.IsNullOrWhiteSpace(x.Opciones) ? null : x.Opciones.Trim()
    };

    private static string Modo(ModoCalculo m) => m == ModoCalculo.Escenarios ? "ESCENARIOS" : "FLUJO";

    private static FormulaPlantilla AFila(int idPlantilla, FormulaCalculo f) => new()
    {
        IdPlantilla = idPlantilla, Codigo = f.Codigo.Trim(), Nombre = f.Nombre.Trim(), Expresion = f.Expresion.Trim()
    };
}
