using Enami.Dominio.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Enami.Datos;

/// <summary>Implementación de <see cref="IAlmacen"/> sobre SQL Server con Entity Framework Core.</summary>
public class AlmacenEf(EnamiDbContext db) : IAlmacen
{
    public IQueryable<T> Tabla<T>() where T : class => db.Set<T>();

    public void Agregar<T>(T entidad) where T : class => db.Set<T>().Add(entidad);

    public void AgregarVarios<T>(IReadOnlyCollection<T> entidades) where T : class
    {
        // Se guarda por bloques y sin detección automática de cambios: con miles de filas es varias veces más rápido.
        db.SaveChanges();
        var detectar = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            foreach (var bloque in entidades.Chunk(5000))
            {
                db.Set<T>().AddRange(bloque);
                db.SaveChanges();
                foreach (var e in bloque) db.Entry(e).State = EntityState.Detached;
            }
        }
        finally { db.ChangeTracker.AutoDetectChangesEnabled = detectar; }
    }

    public void Eliminar<T>(T entidad) where T : class => db.Set<T>().Remove(entidad);

    public void Guardar() => db.SaveChanges();

    public void EnTransaccion(Action accion)
    {
        // Si ya hay una transacción abierta (llamadas anidadas), la acción participa de ella.
        if (db.Database.CurrentTransaction is not null)
        {
            accion();
            return;
        }
        using var tx = db.Database.BeginTransaction();
        try
        {
            accion();
            db.SaveChanges();
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
