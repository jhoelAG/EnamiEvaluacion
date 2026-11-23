using Enami.Dominio.Calculo;
using Enami.Dominio.Modelo;
using Enami.Dominio.Servicios;

namespace Enami.Pruebas;

/// <summary>
/// Observaciones del profesor guía (12-11-2026): las fórmulas se definen por instrumento de fomento (no por tipo de crédito),
/// se pueden agregar instrumentos, y el dólar llega por API pero también se puede ingresar a mano.
/// </summary>
public class InstrumentosTests
{
    private static ServicioInstrumentos Servicio(Entorno e) => new(e.Db, e.Plantillas, e.Auditoria);

    [Fact]
    public void Cada_uno_de_los_nueve_instrumentos_tiene_sus_propias_formulas()
    {
        var e = new Entorno();
        for (byte i = 1; i <= 9; i++)
            Assert.NotNull(e.Plantillas.ObtenerVigente(i));
        // Reconocimiento de Recursos y Estudios Distritales se evalúan con el Anexo 2
        Assert.Equal(ModoCalculo.Escenarios, e.Plantillas.ObtenerVigente(Semilla.InstrumentoAnexo2)!.Modo);
        Assert.Equal(ModoCalculo.Escenarios, e.Plantillas.ObtenerVigente(9)!.Modo);
        Assert.Equal(ModoCalculo.Flujo, e.Plantillas.ObtenerVigente(1)!.Modo);

        // cambiar las fórmulas de un instrumento no cambia las de otro
        var v2 = e.Plantillas.ObtenerVigente(2)!;
        var borrador = e.Plantillas.CrearBorrador(v2.Id, e.IdJefatura);
        borrador.Formulas.Single(f => f.Codigo == "CostoFlete").Expresion = "TonelajeMensual * DistanciaKm * TarifaFleteTonKm * 2";
        e.Plantillas.GuardarBorrador(borrador);
        e.Plantillas.Activar(borrador.Id, e.IdJefatura);
        Assert.Equal(2, e.Plantillas.ObtenerVigente(2)!.Version);
        Assert.Equal(1, e.Plantillas.ObtenerVigente(1)!.Version);
        Assert.DoesNotContain("* 2", e.Plantillas.ObtenerVigente(1)!.Formulas.Single(f => f.Codigo == "CostoFlete").Expresion);
        Assert.Equal("Apoyo a la Producción", e.Plantillas.ObtenerVigente(2)!.Instrumento);
    }

    [Fact]
    public void Un_instrumento_nuevo_parte_con_una_copia_en_borrador_y_se_usa_cuando_la_Jefatura_la_activa()
    {
        var e = new Entorno();
        var origen = e.Plantillas.ObtenerVigente(Semilla.InstrumentoAnexo2)!;
        var (id, idBorrador) = Servicio(e).Crear("Programa de Capacitación Minera", "Instrumento de prueba", origen.Id, e.IdJefatura);
        Assert.Equal((byte)10, id);
        Assert.NotNull(idBorrador);
        var borrador = e.Plantillas.Obtener(idBorrador!.Value)!;
        Assert.Equal(EstadoPlantilla.Borrador, borrador.Estado);
        Assert.Equal(1, borrador.Version);
        Assert.Equal(origen.Formulas.Count, borrador.Formulas.Count);

        // aparece en el selector, pero sin fórmulas vigentes no se puede calcular
        Assert.Contains(e.Solicitudes.Instrumentos(), i => i.Id == id);
        Assert.Null(e.Solicitudes.PlantillaPara(id));

        e.Plantillas.Activar(borrador.Id, e.IdJefatura);
        Assert.Equal(ModoCalculo.Escenarios, e.Solicitudes.PlantillaPara(id)!.Modo);
        Assert.Contains(e.Db.Tabla<Auditoria>(), a => a.Accion == "INSTRUMENTO_CREADO" && a.IdUsuario == e.IdJefatura);
        // la primera versión solo se crea una vez
        Assert.Throws<InvalidOperationException>(() => e.Plantillas.CrearPrimeraVersion(id, origen.Id, e.IdJefatura));
    }

    [Fact]
    public void No_se_repiten_nombres_y_un_instrumento_desactivado_no_se_ofrece_en_evaluaciones_nuevas()
    {
        var e = new Entorno();
        var s = Servicio(e);
        Assert.Throws<ErrorNegocio>(() => s.Crear("  apoyo a la producción ", null, null, e.IdJefatura));
        Assert.Throws<ErrorNegocio>(() => s.Crear("", null, null, e.IdJefatura));

        s.Actualizar(2, "Apoyo a la Producción", null, activo: false, e.IdJefatura);
        Assert.DoesNotContain(e.Solicitudes.Instrumentos(), i => i.Id == 2);
        var datos = e.DatosCompletos(e.IdFaenaElegible);
        datos.IdInstrumento = 2;
        var ex = Assert.Throws<ErrorNegocio>(() => e.Solicitudes.GuardarBorrador(null, datos, e.Sesion(e.IdIngeniero)));
        Assert.Contains("desactivado", ex.Message);

        // una evaluación que ya lo usaba lo conserva y se puede seguir editando
        s.Actualizar(2, "Apoyo a la Producción", null, activo: true, e.IdJefatura);
        var r = e.Solicitudes.GuardarBorrador(null, datos, e.Sesion(e.IdIngeniero));
        s.Actualizar(2, "Apoyo a la Producción", null, activo: false, e.IdJefatura);
        e.Solicitudes.GuardarBorrador(r.IdSolicitud, datos, e.Sesion(e.IdIngeniero));
        Assert.Contains(e.Solicitudes.Instrumentos(2), i => i.Id == 2);
        Assert.Equal(1, s.Listar().Single(v => v.Instrumento.Id == 2).Evaluaciones);
    }

    [Fact]
    public void El_dolar_ingresado_a_mano_reemplaza_al_de_la_API_y_una_nueva_consulta_lo_vuelve_a_reemplazar()
    {
        var e = new Entorno();
        e.Parametros.Registrar("Dolar", 950.5, "API_BCCH");
        Assert.Equal(950.5, e.Parametros.Ultimos()["Dolar"].Valor);
        e.Parametros.Registrar("Dolar", 940, "MANUAL");
        Assert.Equal(940, e.Parametros.Ultimos()["Dolar"].Valor);
        Assert.Equal("MANUAL", e.Parametros.Ultimos()["Dolar"].Fuente);
        // otra consulta a la API el mismo día queda como el último valor registrado
        e.Parametros.Registrar("Dolar", 951, "API_BCCH");
        Assert.Equal(951, e.Parametros.Ultimos()["Dolar"].Valor);
        Assert.Equal("API_BCCH", e.Parametros.Ultimos()["Dolar"].Fuente);
        // un registro por fecha, variable y fuente: el historial tiene el manual y el último de la API
        var historial = e.Parametros.Historial("Dolar");
        Assert.Equal(2, historial.Count);
        Assert.Equal(951, historial[0].Valor);
        Assert.Equal(940, historial[1].Valor);
        // la evaluación usa el último valor
        var r = e.Solicitudes.Simular(e.DatosCompletos(e.IdFaenaElegible));
        Assert.Equal(951, r.Entradas["Dolar"]);
        // respaldo cuando el Banco Central no responde: tipo de cambio de mercado
        e.Parametros.Registrar("Dolar", 948.3, "API_EXTERNA");
        Assert.Equal("API_EXTERNA", e.Parametros.Ultimos()["Dolar"].Fuente);

        // el dólar de una fecha elegida queda con su fecha y fijado por la persona que lo eligió
        e.Parametros.Registrar("Dolar", 932.1, "API_BCCH", new DateTime(2026, 10, 15), e.IdJefatura);
        var fijado = e.Parametros.Ultimos()["Dolar"];
        Assert.Equal(932.1, fijado.Valor);
        Assert.Equal(new DateTime(2026, 10, 15), fijado.FechaValor);
        Assert.True(fijado.Fijado);
        Assert.Equal(932.1, e.Solicitudes.Simular(e.DatosCompletos(e.IdFaenaElegible)).Entradas["Dolar"]);
    }
}
