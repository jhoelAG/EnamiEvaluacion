using Enami.Dominio.Calculo;
using Enami.Dominio.Modelo;
using Enami.Dominio.Persistencia;

namespace Enami.Dominio.Servicios;

/// <summary>Instrumento de fomento con el estado de sus fórmulas, para el mantenedor.</summary>
public record InstrumentoVista(InstrumentoFomento Instrumento, int? VersionVigente, int? VersionBorrador, int Evaluaciones);

/// <summary>
/// Mantenedor de instrumentos de fomento (observación del profesor guía, Sprint 5): ENAMI financia proyectos con nueve
/// instrumentos y puede crear otros. Cada instrumento tiene sus propias versiones de fórmulas; uno nuevo parte con una copia
/// en borrador de las fórmulas de otro, que la Jefatura revisa y activa en el editor de fórmulas.
/// Los instrumentos no se eliminan, porque las evaluaciones guardadas los referencian: se desactivan.
/// </summary>
public class ServicioInstrumentos(IAlmacen db, IRepositorioPlantillas plantillas, ServicioAuditoria auditoria)
{
    public const int LargoNombre = 80, LargoDescripcion = 200;

    public IReadOnlyList<InstrumentoVista> Listar()
    {
        var versiones = db.Tabla<VersionPlantilla>().ToList();
        var usos = db.Tabla<SolicitudCredito>().ToList().GroupBy(s => s.IdInstrumento).ToDictionary(g => g.Key, g => g.Count());
        return db.Tabla<InstrumentoFomento>().OrderBy(i => i.Id).ToList().Select(i => new InstrumentoVista(i,
            versiones.Where(v => v.IdInstrumento == i.Id && v.Estado == "VIGENTE").Select(v => (int?)v.Version).FirstOrDefault(),
            versiones.Where(v => v.IdInstrumento == i.Id && v.Estado == "BORRADOR").Select(v => (int?)v.Version).FirstOrDefault(),
            usos.GetValueOrDefault(i.Id))).ToList();
    }

    /// <summary>
    /// Crea un instrumento activo. Si se indica una versión de origen, se crea la versión 1 de sus fórmulas en borrador
    /// copiando esa versión. Devuelve la clave del instrumento y, si se copió, la de la versión en borrador.
    /// </summary>
    public (byte IdInstrumento, int? IdBorrador) Crear(string nombre, string? descripcion, int? idVersionOrigen, int idUsuario)
    {
        nombre = Validar(nombre, descripcion, null);
        var lista = db.Tabla<InstrumentoFomento>().ToList();
        int siguiente = lista.Count == 0 ? 1 : lista.Max(i => i.Id) + 1;
        if (siguiente > byte.MaxValue) throw new ErrorNegocio("Se alcanzó el máximo de 255 instrumentos.");
        var nuevo = new InstrumentoFomento { Id = (byte)siguiente, Nombre = nombre, Descripcion = Limpio(descripcion), Activo = true };
        db.Agregar(nuevo);
        db.Guardar();
        auditoria.Registrar("INSTRUMENTO_CREADO", idUsuario, detalle: $"Instrumento «{nombre}» (N° {nuevo.Id})");
        int? idBorrador = null;
        if (idVersionOrigen is int origen)
        {
            try { idBorrador = plantillas.CrearPrimeraVersion(nuevo.Id, origen, idUsuario).Id; }
            catch (InvalidOperationException ex) { throw new ErrorNegocio(ex.Message); }
        }
        return (nuevo.Id, idBorrador);
    }

    /// <summary>Cambia el nombre, la descripción o si está activo.</summary>
    public void Actualizar(byte id, string nombre, string? descripcion, bool activo, int idUsuario)
    {
        var i = db.Tabla<InstrumentoFomento>().FirstOrDefault(x => x.Id == id) ?? throw new ErrorNegocio("El instrumento no existe.");
        nombre = Validar(nombre, descripcion, id);
        var cambios = new List<string>();
        if (i.Nombre != nombre) cambios.Add($"nombre «{i.Nombre}» → «{nombre}»");
        if ((i.Descripcion ?? "") != (Limpio(descripcion) ?? "")) cambios.Add("descripción");
        if (i.Activo != activo) cambios.Add(activo ? "activado" : "desactivado");
        if (cambios.Count == 0) return;
        i.Nombre = nombre;
        i.Descripcion = Limpio(descripcion);
        i.Activo = activo;
        db.Guardar();
        auditoria.Registrar("INSTRUMENTO_MODIFICADO", idUsuario, detalle: $"Instrumento N° {id}: {string.Join(", ", cambios)}");
    }

    private string Validar(string nombre, string? descripcion, byte? id)
    {
        nombre = (nombre ?? "").Trim();
        if (nombre.Length == 0) throw new ErrorNegocio("Ingrese el nombre del instrumento.");
        if (nombre.Length > LargoNombre) throw new ErrorNegocio($"El nombre tiene como máximo {LargoNombre} caracteres.");
        if ((descripcion ?? "").Trim().Length > LargoDescripcion) throw new ErrorNegocio($"La descripción tiene como máximo {LargoDescripcion} caracteres.");
        if (db.Tabla<InstrumentoFomento>().ToList().Any(x => x.Id != id && x.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase)))
            throw new ErrorNegocio($"Ya existe un instrumento llamado «{nombre}».");
        return nombre;
    }

    private static string? Limpio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
