using Enami.Dominio.Modelo;
using Enami.Dominio.Servicios;

namespace Enami.Pruebas;

/// <summary>Indicadores del tablero (RF-30, HU-19): TPE, TEP, ITR e IAM.</summary>
public class IndicadoresTests
{
    private static readonly DateTime Lunes = new(2026, 11, 16);

    [Fact]
    public void Las_horas_habiles_cuentan_solo_la_jornada_de_lunes_a_viernes()
    {
        Assert.Equal(2, ServicioIndicadores.HorasHabiles(Lunes.AddHours(10), Lunes.AddHours(12)));
        // lunes 16:30 a martes 09:30: una hora el lunes y una el martes
        Assert.Equal(2, ServicioIndicadores.HorasHabiles(Lunes.AddHours(16.5), Lunes.AddDays(1).AddHours(9.5)));
        // viernes 17:00 a lunes 09:00: media hora el viernes y media el lunes; el fin de semana no cuenta
        Assert.Equal(1, ServicioIndicadores.HorasHabiles(Lunes.AddDays(4).AddHours(17), Lunes.AddDays(7).AddHours(9)));
        Assert.Equal(1, ServicioIndicadores.DiasHabilesEntre(Lunes.AddDays(4), Lunes.AddDays(7)));
    }

    [Fact]
    public void Una_evaluacion_con_la_cadena_completa_es_trazable_y_una_rechazada_sin_causal_no()
    {
        var e = new Entorno();
        var a = new ServicioAprobaciones(e.Db, e.Solicitudes, e.Auditoria);
        int idSubgerente = e.Db.Tabla<Usuario>().Single(u => u.Correo == "subgerente@enami.cl").Id;
        long id = e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible), e.Sesion(e.IdIngeniero)).IdSolicitud;
        a.Enviar(id, e.Sesion(e.IdIngeniero));
        a.Decidir(id, e.Sesion(e.IdJefeCopiapo), ServicioAprobaciones.Aprobar, null);
        a.Decidir(id, e.Sesion(idSubgerente), ServicioAprobaciones.Aprobar, null);

        // un registro incompleto: rechazada sin causal, sin decisiones ni auditoría (como las que hoy quedan en la planilla)
        e.Db.Agregar(new SolicitudCredito { NroSolicitud = "COP-2026-9999", IdFaena = e.IdFaenaElegible, IdInstrumento = 1, IdIngeniero = e.IdIngeniero,
            MontoSolicitado = 1000, PlazoMeses = 12, FechaIngreso = DateTime.Now, Estado = EstadosSolicitud.Rechazada, FechaDictamen = DateTime.Now });

        var r = new ServicioIndicadores(e.Db).Calcular(DateTime.Today, DateTime.Today);
        Assert.Equal(50, r.Itr);
        Assert.True(r.DetalleItr.Single(f => f.Id == id).Trazable);
        var incompleta = r.DetalleItr.Single(f => f.Nro == "COP-2026-9999");
        Assert.Contains("rechazada sin causal", incompleta.Faltas);
        Assert.Contains("sin el envío en la auditoría", incompleta.Faltas);

        // TPE: las dos tienen dictamen; sin dictamen (borradores) no se cuentan
        Assert.Equal(2, r.DetalleTpe.Count);
        Assert.NotNull(r.Tpe);
    }

    [Fact]
    public void Un_calculo_con_un_dolar_antiguo_cuenta_como_error_y_uno_fijado_por_una_persona_no()
    {
        var e = new Entorno();
        // el último dólar registrado corresponde a 10 días atrás (por ejemplo, una API que no se actualizó)
        e.Db.Agregar(new ParametroEconomico { Fecha = DateTime.Now, CodigoVariable = "Dolar", Valor = 950, Fuente = "API_BCCH", FechaValor = DateTime.Today.AddDays(-10) });
        long id = e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible), e.Sesion(e.IdIngeniero)).IdSolicitud;

        var r = new ServicioIndicadores(e.Db).Calcular(DateTime.Today, DateTime.Today);
        Assert.Equal(100, r.Tep);
        Assert.Contains("Dólar observado", r.DetalleTep.Single(f => f.Id == id).Problema);

        // la Jefatura fija ese valor a propósito: ya no es un error
        e.Db.Agregar(new ParametroEconomico { Fecha = DateTime.Now, CodigoVariable = "Dolar", Valor = 950, Fuente = "MANUAL",
            FechaValor = DateTime.Today.AddDays(-10), IdUsuario = e.IdJefatura });
        e.Solicitudes.GuardarBorrador(id, e.DatosCompletos(e.IdFaenaElegible), e.Sesion(e.IdIngeniero));
        Assert.Equal(0, new ServicioIndicadores(e.Db).Calcular(DateTime.Today, DateTime.Today).Tep);
    }

    [Fact]
    public void Un_dia_cumple_solo_si_el_dolar_y_el_cobre_se_actualizan_solos_antes_de_las_9_30()
    {
        var e = new Entorno();
        void Registro(string codigo, DateTime fecha, string fuente = "API_BCCH", int? idUsuario = null) =>
            e.Db.Agregar(new ParametroEconomico { Fecha = fecha, CodigoVariable = codigo, Valor = 1, Fuente = fuente, IdUsuario = idUsuario });

        Registro("Dolar", Lunes.AddHours(9.05));                 // lunes: los dos a las 09:03 → cumple
        Registro("PrecioCuLb", Lunes.AddHours(9.05), "RPA_COCHILCO");
        Registro("Dolar", Lunes.AddDays(1).AddHours(10));         // martes: después de las 09:30 → no cumple
        Registro("PrecioCuLb", Lunes.AddDays(1).AddHours(10));
        Registro("Dolar", Lunes.AddDays(2).AddHours(9), "MANUAL", e.IdJefatura);   // miércoles: a mano → no cumple
        Registro("PrecioCuLb", Lunes.AddDays(2).AddHours(9));

        // del lunes al domingo, consultado el jueves a las 08:00: el jueves aún no cuenta y el fin de semana no es hábil
        var r = new ServicioIndicadores(e.Db).Calcular(Lunes, Lunes.AddDays(6), ahora: Lunes.AddDays(3).AddHours(8));
        Assert.Equal(new[] { true, false, false }, r.DetalleIam.Select(f => f.Cumple));
        Assert.Equal(33.3, r.Iam);
    }

    [Fact]
    public void Sin_datos_en_el_periodo_los_indicadores_quedan_sin_valor()
    {
        var r = new ServicioIndicadores(new Entorno().Db).Calcular(Lunes.AddYears(-5), Lunes.AddYears(-5).AddDays(1));
        Assert.Null(r.Tpe);
        Assert.Null(r.Tep);
        Assert.Null(r.Itr);
        Assert.Equal(0, r.Iam);
    }
}
