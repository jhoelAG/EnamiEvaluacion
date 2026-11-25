using Enami.Dominio.Modelo;
using Enami.Dominio.Servicios;

namespace Enami.Pruebas;

/// <summary>
/// Evaluaciones no viables (proceso 2, pasos 2.6 y 2.7; proceso 3, compuerta «¿Viable?»; CU-02 flujo 6a; HU-11): no pasan a la
/// visación, el ingeniero las cierra con la causal y ningún aprobador puede aprobarlas, ni en la aplicación ni por el correo.
/// </summary>
public class NoViableTests
{
    private static ServicioAprobaciones Servicio(Entorno e) => new(e.Db, e.Solicitudes, e.Auditoria);

    /// <summary>Evaluación con un costo de extracción que deja el VAN negativo.</summary>
    private static long NoViable(Entorno e)
    {
        var d = e.DatosCompletos(e.IdFaenaElegible);
        d.DatosProyecto["CostoMinaTon"] = 400;
        return e.Solicitudes.GuardarBorrador(null, d, e.Sesion(e.IdIngeniero)).IdSolicitud;
    }

    [Fact]
    public void Un_proyecto_no_viable_no_se_envia_a_revision()
    {
        var e = new Entorno();
        var a = Servicio(e);
        long id = NoViable(e);

        Assert.False(e.Solicitudes.Viable(id));
        var error = Assert.Throws<ErrorNegocio>(() => a.Enviar(id, e.Sesion(e.IdIngeniero)));
        Assert.Contains("no es viable", error.Message);
        Assert.Equal(EstadosSolicitud.Borrador, e.Db.Tabla<SolicitudCredito>().Single(s => s.Id == id).Estado);
        Assert.Empty(a.Historial(id));
        Assert.True(a.PuedeCerrarNoViable(e.Db.Tabla<SolicitudCredito>().Single(s => s.Id == id), e.Sesion(e.IdIngeniero)));
    }

    [Fact]
    public void El_ingeniero_lo_cierra_con_la_causal_sin_pasar_por_los_aprobadores()
    {
        var e = new Entorno();
        var a = Servicio(e);
        long id = NoViable(e);
        var causal = e.Db.Tabla<CausalRechazo>().OrderBy(c => c.Id).First();

        Assert.Throws<ErrorNegocio>(() => a.CerrarNoViable(id, e.Sesion(e.IdIngeniero), null, null));   // la causal es obligatoria
        Assert.Throws<ErrorNegocio>(() => a.CerrarNoViable(id, e.Sesion(e.IdJefeCopiapo), causal.Id, null));   // solo su ingeniero
        var r = a.CerrarNoViable(id, e.Sesion(e.IdIngeniero), causal.Id, "Costos de extracción muy altos.");

        var s = e.Db.Tabla<SolicitudCredito>().Single(x => x.Id == id);
        Assert.Equal(EstadosSolicitud.Rechazada, s.Estado);
        Assert.Equal(causal.Id, s.IdCausal);
        Assert.NotNull(s.FechaDictamen);
        var decision = Assert.Single(a.Historial(id));
        Assert.Equal("Ingeniero de Fomento", decision.NivelNombre);
        Assert.Equal(ServicioAprobaciones.Rechazar, decision.Decision);
        Assert.Empty(a.PendientesDe(e.IdJefeCopiapo));
        Assert.Equal("DICTAMEN", Assert.Single(r.Eventos).Tipo);   // se notifica el dictamen con su causal
        Assert.Contains(e.Db.Tabla<Auditoria>(), x => x.Accion == "DECISION_RECHAZADA" && x.IdSolicitud == id
                                                       && x.Detalle!.Contains("cerrada como no viable") && x.Detalle.Contains(causal.Codigo));

        var pdf = new ServicioDictamen(e.Db, e.Solicitudes, a, e.Plantillas, e.Auditoria).Generar(id, e.Sesion(e.IdIngeniero))!.Value.Contenido;
        var texto = DictamenTests.Contenido(pdf);
        Assert.Contains(Enami.Dominio.Exportacion.DocumentoPdf.Cadena("Evaluación rechazada · proyecto no viable"), texto);
        Assert.Contains("Cerrada como no viable", texto);
    }

    [Fact]
    public void Un_proyecto_viable_no_se_cierra_como_no_viable()
    {
        var e = new Entorno();
        long id = e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible), e.Sesion(e.IdIngeniero)).IdSolicitud;
        var causal = e.Db.Tabla<CausalRechazo>().OrderBy(c => c.Id).First();

        Assert.True(e.Solicitudes.Viable(id));
        Assert.Throws<ErrorNegocio>(() => Servicio(e).CerrarNoViable(id, e.Sesion(e.IdIngeniero), causal.Id, null));
    }

    [Fact]
    public void Una_evaluacion_no_viable_que_ya_estaba_en_revision_no_se_puede_aprobar_ni_desde_el_correo()
    {
        var e = new Entorno();
        var a = Servicio(e);
        long id = e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible), e.Sesion(e.IdIngeniero)).IdSolicitud;
        a.Enviar(id, e.Sesion(e.IdIngeniero));
        // caso de las evaluaciones enviadas antes de esta regla: el cálculo guardado resulta no viable
        var resultado = e.Db.Tabla<ResultadoCalculo>().Single(r => r.IdSolicitud == id);
        int idCosto = e.Db.Tabla<VariablePlantilla>().Single(v => v.IdPlantilla == resultado.IdPlantilla && v.Codigo == "CostoMinaTon").Id;
        e.Db.Tabla<ValorEntrada>().Single(v => v.IdResultado == resultado.Id && v.IdVariable == idCosto).Valor = 400;
        e.Db.Guardar();
        Assert.False(e.Solicitudes.Viable(id));

        Assert.Contains("no es viable", Assert.Throws<ErrorNegocio>(() => a.DecidirPorCorreo(id, "jefe.copiapo@enami.cl", "Aprobar", null)).Message);
        Assert.Throws<ErrorNegocio>(() => a.Decidir(id, e.Sesion(e.IdJefeCopiapo), ServicioAprobaciones.Aprobar, "Conforme."));
        // sí se puede observar (para que el ingeniero corrija) o rechazar con su causal
        a.Decidir(id, e.Sesion(e.IdJefeCopiapo), ServicioAprobaciones.Observar, "Revise el costo de extracción.");
        Assert.Equal(EstadosSolicitud.Observada, e.Db.Tabla<SolicitudCredito>().Single(s => s.Id == id).Estado);
    }
}
