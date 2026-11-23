using System.Collections.Concurrent;
using System.Reflection;

namespace Enami.Dominio.Persistencia;

/// <summary>
/// Acceso a las tablas del modelo. Los servicios escriben sus consultas con LINQ una sola vez
/// y funcionan igual sobre SQL Server (Enami.Datos, con EF Core) y sobre el almacén en memoria
/// (pruebas y modo de demostración sin base de datos).
/// </summary>
public interface IAlmacen
{
    /// <summary>Consulta una tabla. Las entidades devueltas quedan asociadas: si se modifican, <see cref="Guardar"/> persiste el cambio.</summary>
    IQueryable<T> Tabla<T>() where T : class;
    void Agregar<T>(T entidad) where T : class;
    /// <summary>Agrega y guarda muchas filas de una vez (por ejemplo, los 66.550 lotes de recepción). Más rápido que llamar a Agregar en un ciclo.</summary>
    void AgregarVarios<T>(IReadOnlyCollection<T> entidades) where T : class;
    void Eliminar<T>(T entidad) where T : class;
    /// <summary>Confirma los cambios. En SQL Server asigna las claves IDENTITY a las entidades nuevas.</summary>
    void Guardar();
    /// <summary>Ejecuta varias operaciones como una unidad: si la acción falla, no se confirma nada (RNF-08).</summary>
    void EnTransaccion(Action accion);
}

/// <summary>
/// Almacén en memoria. Se usa en las pruebas y en el modo «Memoria» de la aplicación.
/// Asigna las claves numéricas al agregar (equivalente a IDENTITY) y es seguro para varios usuarios a la vez.
/// </summary>
public class AlmacenMemoria : IAlmacen
{
    private readonly ConcurrentDictionary<Type, List<object>> _tablas = new();
    private readonly object _candado = new();

    public IQueryable<T> Tabla<T>() where T : class
    {
        lock (_candado) return Lista<T>().Cast<T>().ToList().AsQueryable();
    }

    public void Agregar<T>(T entidad) where T : class
    {
        ArgumentNullException.ThrowIfNull(entidad);
        lock (_candado)
        {
            var lista = Lista<T>();
            var id = typeof(T).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
            if (id is not null && id.CanWrite)
            {
                var actual = Convert.ToInt64(id.GetValue(entidad));
                if (actual == 0)
                {
                    long siguiente = lista.Count == 0 ? 1 : lista.Max(e => Convert.ToInt64(id.GetValue(e))) + 1;
                    id.SetValue(entidad, Convert.ChangeType(siguiente, id.PropertyType));
                }
                else if (lista.Any(e => Convert.ToInt64(id.GetValue(e)) == actual))
                    throw new InvalidOperationException($"Ya existe un registro de {typeof(T).Name} con Id {actual}.");
            }
            lista.Add(entidad);
        }
    }

    public void AgregarVarios<T>(IReadOnlyCollection<T> entidades) where T : class
    {
        lock (_candado)
        {
            var lista = Lista<T>();
            var id = typeof(T).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
            if (id is not null && id.CanWrite)
            {
                long siguiente = lista.Count == 0 ? 1 : lista.Max(e => Convert.ToInt64(id.GetValue(e))) + 1;
                foreach (var e in entidades)
                    if (Convert.ToInt64(id.GetValue(e)) == 0) id.SetValue(e, Convert.ChangeType(siguiente++, id.PropertyType));
            }
            lista.AddRange(entidades);
        }
    }

    public void Eliminar<T>(T entidad) where T : class
    {
        lock (_candado) Lista<T>().Remove(entidad);
    }

    public void Guardar() { }

    public void EnTransaccion(Action accion)
    {
        // En memoria se toma una copia de las listas y se restaura si la acción falla.
        lock (_candado)
        {
            var copia = _tablas.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
            try { accion(); }
            catch
            {
                foreach (var (tipo, filas) in copia) { _tablas[tipo].Clear(); _tablas[tipo].AddRange(filas); }
                foreach (var tipo in _tablas.Keys.Except(copia.Keys)) _tablas[tipo].Clear();
                throw;
            }
        }
    }

    private List<object> Lista<T>() => _tablas.GetOrAdd(typeof(T), _ => []);
}
