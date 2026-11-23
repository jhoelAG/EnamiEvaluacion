using Enami.Dominio.Modelo;
using Enami.Dominio.Persistencia;

namespace Enami.Dominio.Servicios;

/// <summary>Lectura de los catálogos para los selectores de la interfaz.</summary>
public class ServicioCatalogos(IAlmacen db)
{
    public IReadOnlyList<Oficina> Oficinas() => db.Tabla<Oficina>().OrderBy(o => o.Nombre).ToList();
    public IReadOnlyList<CausalRechazo> Causales() => db.Tabla<CausalRechazo>().OrderBy(c => c.Codigo).ToList();
    public IReadOnlyList<InstrumentoFomento> Instrumentos(bool soloActivos = false) =>
        db.Tabla<InstrumentoFomento>().Where(i => !soloActivos || i.Activo).OrderBy(i => i.Id).ToList();
}
