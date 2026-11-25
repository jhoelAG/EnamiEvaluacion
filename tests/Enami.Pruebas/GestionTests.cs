using Enami.Dominio.Modelo;
using Enami.Dominio.Servicios;

namespace Enami.Pruebas;

/// <summary>Datos de gestión del tablero: preguntas de negocio 1 a 4 de la sección 4.3 del informe.</summary>
public class GestionTests
{
    [Fact]
    public void Cuenta_aprobadas_y_rechazadas_por_mes_y_oficina_con_su_monto_demora_por_nivel_y_causal()
    {
        var e = new Entorno();
        var a = new ServicioAprobaciones(e.Db, e.Solicitudes, e.Auditoria);
        int idSubgerente = e.Db.Tabla<Usuario>().Single(u => u.Correo == "subgerente@enami.cl").Id;
        var causal = e.Db.Tabla<CausalRechazo>().OrderBy(c => c.Id).First();

        // una aprobada por US$ 65.000, una rechazada por el jefe con causal y un borrador
        long aprobada = e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible, 65_000), e.Sesion(e.IdIngeniero)).IdSolicitud;
        a.Enviar(aprobada, e.Sesion(e.IdIngeniero));
        a.Decidir(aprobada, e.Sesion(e.IdJefeCopiapo), ServicioAprobaciones.Aprobar, null);
        a.Decidir(aprobada, e.Sesion(idSubgerente), ServicioAprobaciones.Aprobar, null);
        long rechazada = e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible, 40_000), e.Sesion(e.IdIngeniero)).IdSolicitud;
        a.Enviar(rechazada, e.Sesion(e.IdIngeniero));
        a.Decidir(rechazada, e.Sesion(e.IdJefeCopiapo), ServicioAprobaciones.Rechazar, "Leyes bajo el mínimo.", causal.Id);
        e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible), e.Sesion(e.IdIngeniero));

        var g = new ServicioGestion(e.Db).Calcular(DateTime.Today, DateTime.Today);

        // 1. el mes de hoy en Copiapó: 3 ingresadas, 1 aprobada y 1 rechazada (50 %)
        var mes = Assert.Single(g.PorMesYOficina);
        Assert.Equal("Oficina Minera Copiapó", mes.Oficina);
        Assert.Equal((3, 1, 1), (mes.Ingresadas, mes.Aprobadas, mes.Rechazadas));
        Assert.Equal(50, mes.TasaAprobacion);

        // 2. el jefe decidió dos veces y el subgerente una
        Assert.Equal(2, g.PorNivel.Single(f => f.Nivel == "Jefe de Oficina").Decisiones);
        Assert.Equal(1, g.PorNivel.Single(f => f.Nivel == "Subgerente").Decisiones);

        // 3. solo cuenta el monto aprobado: 100 % en el instrumento y en persona natural
        var instrumento = Assert.Single(g.MontoPorInstrumento);
        Assert.Equal((65_000m, 100.0), (instrumento.Monto, instrumento.Porcentaje));
        Assert.Equal("Persona natural", Assert.Single(g.MontoPorTipoProductor).Grupo);

        // 4. la causal del rechazo
        var c = Assert.Single(g.Causales);
        Assert.Equal((causal.Codigo, 1, 100.0), (c.Codigo, c.Veces, c.Porcentaje));
    }

    [Fact]
    public void El_filtro_de_oficina_deja_fuera_las_evaluaciones_de_otras_oficinas()
    {
        var e = new Entorno();
        e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible), e.Sesion(e.IdIngeniero));
        var otra = new Oficina { Nombre = "Oficina de prueba" };
        e.Db.Agregar(otra);

        Assert.Single(new ServicioGestion(e.Db).Calcular(DateTime.Today, DateTime.Today, e.IdCopiapo).PorMesYOficina);
        Assert.Empty(new ServicioGestion(e.Db).Calcular(DateTime.Today, DateTime.Today, otra.Id).PorMesYOficina);
    }
}
