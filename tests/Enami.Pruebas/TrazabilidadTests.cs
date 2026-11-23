using Enami.Dominio.Calculo;
using Enami.Dominio.Modelo;

namespace Enami.Pruebas;

/// <summary>Trazabilidad de decisiones: aprobación de versiones de fórmulas y evaluaciones ligadas a su versión (RF-06, RF-08, RF-27).</summary>
public class TrazabilidadTests
{
    [Fact]
    public void Solo_la_Jefatura_aprueba_versiones_y_queda_en_la_auditoria()
    {
        var e = new Entorno();
        var vigente = e.Plantillas.ObtenerVigente(1)!;
        var borrador = e.Plantillas.CrearBorrador(vigente.Id, e.IdJefatura);

        // un ingeniero no puede aprobar, aunque llame directamente al servicio
        var error = Assert.Throws<InvalidOperationException>(() => e.Plantillas.Activar(borrador.Id, e.IdIngeniero));
        Assert.Contains("Jefatura", error.Message);
        Assert.Equal(EstadoPlantilla.Borrador, e.Plantillas.Obtener(borrador.Id)!.Estado);

        e.Plantillas.Activar(borrador.Id, e.IdJefatura);
        var version = e.Db.Tabla<VersionPlantilla>().Single(v => v.Id == borrador.Id);
        Assert.Equal(e.IdJefatura, version.IdAprobador);
        Assert.NotNull(version.FechaAprobacion);

        var creada = Assert.Single(e.Db.Tabla<Auditoria>().Where(a => a.Accion == "VERSION_CREADA"));
        Assert.Equal(e.IdJefatura, creada.IdUsuario);
        var aprobada = Assert.Single(e.Db.Tabla<Auditoria>().Where(a => a.Accion == "VERSION_APROBADA"));
        Assert.Equal(e.IdJefatura, aprobada.IdUsuario);
        Assert.Contains("v2 aprobada", aprobada.Detalle!);
        Assert.Contains("se retira la v1", aprobada.Detalle!);
    }

    [Fact]
    public void La_evaluacion_guardada_queda_ligada_a_su_version_a_sus_valores_y_a_su_autor()
    {
        var e = new Entorno();
        var ing = e.Sesion(e.IdIngeniero);
        var vigente = e.Plantillas.ObtenerVigente(1)!;
        var r = e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible), ing);
        Assert.NotNull(r.Calculo);

        var resultado = Assert.Single(e.Db.Tabla<ResultadoCalculo>().Where(x => x.IdSolicitud == r.IdSolicitud));
        Assert.Equal(vigente.Id, resultado.IdPlantilla);                         // versión de fórmulas usada
        Assert.Equal((decimal)Math.Round(r.Calculo!.Van, 2), Math.Round(resultado.Van, 2));
        Assert.NotEmpty(e.Db.Tabla<ValorEntrada>().Where(v => v.IdResultado == resultado.Id));   // valores de entrada usados

        Assert.Contains(e.Db.Tabla<Auditoria>(), a => a.Accion == "SOLICITUD_CREADA" && a.IdSolicitud == r.IdSolicitud && a.IdUsuario == e.IdIngeniero);
        Assert.Contains(e.Db.Tabla<Auditoria>(), a => a.Accion == "CALCULO" && a.IdSolicitud == r.IdSolicitud && a.IdUsuario == e.IdIngeniero);

        // si después se aprueba otra versión, la evaluación sigue apuntando a la que se usó y esa versión no se puede borrar
        var nueva = e.Plantillas.CrearBorrador(vigente.Id, e.IdJefatura);
        e.Plantillas.Activar(nueva.Id, e.IdJefatura);
        Assert.Equal(vigente.Id, e.Db.Tabla<ResultadoCalculo>().Single(x => x.Id == resultado.Id).IdPlantilla);
        Assert.NotNull(e.Plantillas.MotivoNoEliminable(vigente.Id));
    }
}
