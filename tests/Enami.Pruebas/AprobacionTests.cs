using Enami.Dominio.Modelo;
using Enami.Dominio.Servicios;

namespace Enami.Pruebas;

/// <summary>Aprobación en tres niveles (RF-20 a RF-22, BOT-03): Ingeniero → Jefe de Oficina → Subgerente.</summary>
public class AprobacionTests
{
    private static ServicioAprobaciones Servicio(Entorno e) => new(e.Db, e.Solicitudes, e.Auditoria);

    private static long Evaluacion(Entorno e) =>
        e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible), e.Sesion(e.IdIngeniero)).IdSolicitud;

    private static SolicitudCredito Solicitud(Entorno e, long id) => e.Db.Tabla<SolicitudCredito>().Single(s => s.Id == id);

    [Fact]
    public void Pasa_por_el_Jefe_de_Oficina_y_el_Subgerente_y_queda_aprobada()
    {
        var e = new Entorno();
        var a = Servicio(e);
        long id = Evaluacion(e);
        int idSubgerente = e.Db.Tabla<Usuario>().Single(u => u.Correo == "subgerente@enami.cl").Id;

        // nivel 1: el ingeniero envía; queda asignada al Jefe de Oficina de la faena y se avisa al flujo
        var r1 = a.Enviar(id, e.Sesion(e.IdIngeniero));
        Assert.Equal(EstadosSolicitud.EnRevision, Solicitud(e, id).Estado);
        var aviso1 = Assert.Single(r1.Eventos);
        Assert.Equal("APROBACION_PENDIENTE", aviso1.Tipo);
        Assert.Equal("jefe.copiapo@enami.cl", aviso1.Aprobador!.Correo);
        Assert.Equal(2, aviso1.Nivel);
        Assert.NotNull(aviso1.Van);
        Assert.Contains(id, a.PendientesDe(e.IdJefeCopiapo));

        // nivel 2: el Jefe de Oficina aprueba desde el correo (lo registra el flujo por la API)
        var r2 = a.DecidirPorCorreo(id, "JEFE.COPIAPO@enami.cl", "Aprobar", null);
        Assert.Equal(EstadosSolicitud.EnRevision, r2.Estado);
        Assert.Equal("subgerente@enami.cl", Assert.Single(r2.Eventos).Aprobador!.Correo);
        Assert.Contains(id, a.PendientesDe(idSubgerente));

        // nivel 3: el Subgerente aprueba en la aplicación y se emite el dictamen
        var r3 = a.Decidir(id, e.Sesion(idSubgerente), ServicioAprobaciones.Aprobar, "Proyecto viable.");
        Assert.Equal(EstadosSolicitud.Aprobada, Solicitud(e, id).Estado);
        Assert.NotNull(Solicitud(e, id).FechaDictamen);
        Assert.Equal("DICTAMEN", Assert.Single(r3.Eventos).Tipo);

        var historial = a.Historial(id);
        Assert.Equal(new[] { "Jefe de Oficina", "Subgerente" }, historial.Select(h => h.NivelNombre));
        Assert.All(historial, h => Assert.Equal(ServicioAprobaciones.Aprobar, h.Decision));
        Assert.Contains(e.Db.Tabla<Auditoria>(), x => x.Accion == "DECISION_APROBADA" && x.Robot == "BOT-03" && x.IdSolicitud == id);
        Assert.Contains(e.Db.Tabla<Auditoria>(), x => x.Accion == "SOLICITUD_ENVIADA" && x.IdUsuario == e.IdIngeniero);
    }

    [Fact]
    public void Una_evaluacion_de_una_faena_que_dejo_de_ser_elegible_no_se_envia_y_queda_en_borrador()
    {
        var e = new Entorno();
        var a = Servicio(e);
        long id = Evaluacion(e);

        // el padrón vence después de guardar el borrador (error E1 de las pruebas del 24-11-2026)
        var faena = e.Db.Tabla<Faena>().Single(f => f.Id == e.IdFaenaElegible);
        faena.VencimientoPadron = DateTime.Today.AddDays(-1);
        e.Db.Guardar();

        var error = Assert.Throws<ErrorNegocio>(() => a.Enviar(id, e.Sesion(e.IdIngeniero)));
        Assert.Contains("venció", error.Message);
        Assert.Equal(EstadosSolicitud.Borrador, Solicitud(e, id).Estado);
        Assert.Empty(a.Historial(id));
        Assert.DoesNotContain(e.Db.Tabla<Auditoria>(), x => x.Accion == "SOLICITUD_ENVIADA" && x.IdSolicitud == id);
    }

    [Fact]
    public void Una_observacion_vuelve_al_ingeniero_que_corrige_y_reenvia()
    {
        var e = new Entorno();
        var a = Servicio(e);
        long id = Evaluacion(e);
        a.Enviar(id, e.Sesion(e.IdIngeniero));

        // en la aplicación, observar exige indicar qué corregir
        Assert.Throws<ErrorNegocio>(() => a.Decidir(id, e.Sesion(e.IdJefeCopiapo), ServicioAprobaciones.Observar, " "));
        var r = a.Decidir(id, e.Sesion(e.IdJefeCopiapo), ServicioAprobaciones.Observar, "Falta el respaldo de las leyes.");
        Assert.Equal("OBSERVADA", Assert.Single(r.Eventos).Tipo);
        Assert.Equal(EstadosSolicitud.Observada, Solicitud(e, id).Estado);

        // el ingeniero puede editarla y volver a enviarla: queda otra vez con el Jefe de Oficina
        Assert.True(e.Solicitudes.PuedeEditar(Solicitud(e, id), e.Sesion(e.IdIngeniero)));
        e.Solicitudes.GuardarBorrador(id, e.DatosCompletos(e.IdFaenaElegible), e.Sesion(e.IdIngeniero));
        a.Enviar(id, e.Sesion(e.IdIngeniero));
        Assert.Equal(EstadosSolicitud.EnRevision, Solicitud(e, id).Estado);
        Assert.Equal(2, a.Historial(id).Count(h => h.NivelNombre == "Jefe de Oficina"));
    }

    [Fact]
    public void Solo_decide_el_aprobador_asignado_y_un_rechazo_cierra_la_evaluacion()
    {
        var e = new Entorno();
        var a = Servicio(e);
        long id = Evaluacion(e);

        // no se puede decidir antes de enviarla, ni la puede enviar otro usuario
        Assert.Throws<ErrorNegocio>(() => a.Decidir(id, e.Sesion(e.IdJefeCopiapo), ServicioAprobaciones.Aprobar, null));
        Assert.Throws<ErrorNegocio>(() => a.Enviar(id, e.Sesion(e.IdJefatura)));
        a.Enviar(id, e.Sesion(e.IdIngeniero));

        // la Jefatura no está en la cadena; una decisión desconocida se rechaza
        Assert.Throws<ErrorNegocio>(() => a.Decidir(id, e.Sesion(e.IdJefatura), ServicioAprobaciones.Aprobar, null));
        Assert.Throws<ErrorNegocio>(() => a.DecidirPorCorreo(id, "jefe.copiapo@enami.cl", "Quizás", null));

        short causal = e.Db.Tabla<CausalRechazo>().First().Id;
        var r = a.Decidir(id, e.Sesion(e.IdJefeCopiapo), ServicioAprobaciones.Rechazar, "Leyes bajo el mínimo.", causal);
        var s = Solicitud(e, id);
        Assert.Equal(EstadosSolicitud.Rechazada, s.Estado);
        Assert.Equal(causal, s.IdCausal);
        Assert.Equal("DICTAMEN", Assert.Single(r.Eventos).Tipo);
        // ya decidida: un segundo clic en el correo no la cambia
        Assert.Throws<ErrorNegocio>(() => a.DecidirPorCorreo(id, "jefe.copiapo@enami.cl", "Aprobar", null));
    }

    [Fact]
    public void Un_rechazo_sin_causal_se_detiene_con_un_mensaje_y_no_cambia_la_evaluacion()
    {
        // la base exige causal en toda evaluación rechazada (CK_SOLICITUD_causal); sin ella, SQL Server fallaría al guardar
        var e = new Entorno();
        var a = Servicio(e);
        long id = Evaluacion(e);
        a.Enviar(id, e.Sesion(e.IdIngeniero));

        var enApp = Assert.Throws<ErrorNegocio>(() => a.Decidir(id, e.Sesion(e.IdJefeCopiapo), ServicioAprobaciones.Rechazar, "Leyes bajo el mínimo.", null));
        Assert.Contains("causal", enApp.Message);
        var porCorreo = Assert.Throws<ErrorNegocio>(() => a.DecidirPorCorreo(id, "jefe.copiapo@enami.cl", "Rechazar", null));
        Assert.Contains("causal", porCorreo.Message);

        // la evaluación sigue en revisión y la decisión sigue pendiente del Jefe de Oficina
        var s = Solicitud(e, id);
        Assert.Equal(EstadosSolicitud.EnRevision, s.Estado);
        Assert.Null(s.IdCausal);
        Assert.Contains(id, a.PendientesDe(e.IdJefeCopiapo));
    }
}
