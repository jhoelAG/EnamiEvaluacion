using Enami.Dominio.Modelo;
using Enami.Dominio.Servicios;

namespace Enami.Pruebas;

/// <summary>Trazabilidad adicional: causal en la auditoría del rechazo (E6), valor anterior y nuevo al corregir (E7) y búsqueda en la auditoría.</summary>
public class HistorialCambiosTests
{
    [Fact]
    public void Al_corregir_una_evaluacion_observada_queda_el_valor_anterior_y_el_nuevo()
    {
        var e = new Entorno();
        var a = new ServicioAprobaciones(e.Db, e.Solicitudes, e.Auditoria);
        var datos = e.DatosCompletos(e.IdFaenaElegible, 65_000);
        long id = e.Solicitudes.GuardarBorrador(null, datos, e.Sesion(e.IdIngeniero)).IdSolicitud;
        a.Enviar(id, e.Sesion(e.IdIngeniero));
        a.Decidir(id, e.Sesion(e.IdJefeCopiapo), ServicioAprobaciones.Observar, "Revise el monto y el primer dato del proyecto.");

        var corregidos = e.DatosCompletos(e.IdFaenaElegible, 70_000);
        var codigo = corregidos.DatosProyecto.Keys.First();
        corregidos.DatosProyecto[codigo] = (corregidos.DatosProyecto[codigo] ?? 0) + 1;
        e.Solicitudes.GuardarBorrador(id, corregidos, e.Sesion(e.IdIngeniero));

        var registro = e.Db.Tabla<Auditoria>().Where(x => x.Accion == "SOLICITUD_ACTUALIZADA" && x.IdSolicitud == id).OrderBy(x => x.Id).Last();
        Assert.Contains("Monto solicitado (US$): 65.000 → 70.000", registro.Detalle);
        Assert.Equal(2, registro.Detalle!.Split(';').Length);   // el monto y el dato del proyecto; nada más cambió

        // guardar otra vez sin cambios lo deja dicho
        e.Solicitudes.GuardarBorrador(id, corregidos, e.Sesion(e.IdIngeniero));
        var sinCambios = e.Db.Tabla<Auditoria>().Where(x => x.Accion == "SOLICITUD_ACTUALIZADA" && x.IdSolicitud == id).OrderBy(x => x.Id).Last();
        Assert.Contains("sin cambios", sinCambios.Detalle);
    }

    [Fact]
    public void La_auditoria_del_rechazo_incluye_la_causal_y_la_busqueda_filtra_por_accion_y_evaluacion()
    {
        var e = new Entorno();
        var a = new ServicioAprobaciones(e.Db, e.Solicitudes, e.Auditoria);
        var causal = e.Db.Tabla<CausalRechazo>().OrderBy(c => c.Id).First();
        long id = e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible), e.Sesion(e.IdIngeniero)).IdSolicitud;
        long otra = e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible), e.Sesion(e.IdIngeniero)).IdSolicitud;
        a.Enviar(id, e.Sesion(e.IdIngeniero));
        a.Decidir(id, e.Sesion(e.IdJefeCopiapo), ServicioAprobaciones.Rechazar, "Leyes bajo el mínimo.", causal.Id);

        var rechazo = Assert.Single(e.Auditoria.Buscar(null, null, "DECISION_RECHAZADA", null, null));
        Assert.Contains(causal.Codigo, rechazo.Detalle);

        var nro = e.Db.Tabla<SolicitudCredito>().Single(s => s.Id == id).NroSolicitud;
        Assert.All(e.Auditoria.Buscar(null, null, null, null, nro), x => Assert.Equal(id, x.IdSolicitud));
        Assert.All(e.Auditoria.Buscar(null, null, null, e.IdJefeCopiapo.ToString(), null), x => Assert.Equal(e.IdJefeCopiapo, x.IdUsuario));
        Assert.Empty(e.Auditoria.Buscar(DateTime.Today.AddDays(1), null, null, null, null));
        Assert.Contains("SOLICITUD_CREADA", e.Auditoria.Catalogo().Acciones);
        Assert.Equal(2, e.Auditoria.Buscar(null, null, "SOLICITUD_CREADA", null, null).Count(x => x.IdSolicitud == id || x.IdSolicitud == otra));
    }
}
