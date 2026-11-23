using Enami.Dominio.Modelo;
using Enami.Dominio.Servicios;

namespace Enami.Pruebas;

public class DatosPruebaTests
{
    [Fact]
    public void Completa_solo_los_campos_vacios_y_se_puede_repetir()
    {
        var e = new Entorno();
        var p = e.Db.Tabla<Productor>().Single();
        var empresa = new Productor { Rut = "76543210-3", TipoProductor = TiposProductor.Empresa, RazonSocial = "COMP" };
        e.Db.Agregar(empresa);
        var sinDatos = new Faena { Codigo = "40000", Nombre = "LA V", IdProductor = empresa.Id, Estado = EstadosFaena.Activa };
        e.Db.Agregar(sinDatos);
        var distanciaOriginal = e.Db.Tabla<Faena>().Single(f => f.Id == e.IdFaenaElegible).DistanciaPoderCompraKm;

        var servicio = new ServicioDatosPrueba(e.Db, e.Fichas, e.Auditoria);
        var r = servicio.Completar(e.IdAdmin);

        Assert.True(r.Total > 0);
        Assert.NotNull(sinDatos.IdComuna);
        Assert.NotNull(sinDatos.IdOficina);
        Assert.Equal(e.IdCopiapo, sinDatos.IdOficina);            // la oficina más frecuente de su comuna
        Assert.NotNull(sinDatos.DistanciaPoderCompraKm);
        Assert.NotNull(sinDatos.NroPadron);
        Assert.NotNull(sinDatos.VencimientoPadron);
        Assert.NotEqual("LA V", sinDatos.Nombre);
        Assert.Contains("Minera", empresa.RazonSocial!);
        Assert.NotNull(empresa.RepresentanteLegal);
        Assert.Equal("Juan", p.Nombres);                             // los datos que ya existían no cambian
        Assert.Equal(distanciaOriginal, e.Db.Tabla<Faena>().Single(f => f.Id == e.IdFaenaElegible).DistanciaPoderCompraKm);
        var recursos = e.Db.Tabla<RecursoFaena>().ToList();
        Assert.Equal(3 * e.Db.Tabla<Faena>().Count(f => f.Estado == EstadosFaena.Activa), recursos.Count);
        Assert.All(recursos, x => Assert.Equal(ServicioDatosPrueba.FuenteRecursos, x.Fuente));
        Assert.Contains(e.Db.Tabla<Auditoria>(), a => a.Accion == "DATOS_PRUEBA");

        var segunda = servicio.Completar(e.IdAdmin);
        Assert.Equal(0, segunda.Total);
    }
}
