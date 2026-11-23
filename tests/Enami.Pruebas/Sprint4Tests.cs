using Enami.Dominio.Calculo;
using Enami.Dominio.Calculo.Expresiones;
using Enami.Dominio.Importacion;
using Enami.Dominio.Modelo;
using Enami.Dominio.Servicios;

namespace Enami.Pruebas;

/// <summary>Sprint 4: recepción de mineral, recursos por faena, búsqueda por nombre, funciones financieras y US$.</summary>
public class Sprint4Tests
{
    private static (int, Dictionary<string, string>) Lote(int fila, string lote, string mina, string fecha, string peso, string cu = "2,5", string au = "0", string ag = "0") =>
        (fila, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["N° LOTE"] = lote, ["COD MINA"] = mina, ["FECHA CIERRE"] = fecha, ["P SECO (Kg)"] = peso,
            ["LEY CU%"] = cu, ["LEY AU g/t"] = au, ["LEY AG g/t"] = ag, ["C_AGENCIA"] = "1", ["C_PRODUCTO"] = "4"
        });

    [Fact]
    public void El_importador_de_recepcion_aisla_los_lotes_con_errores()
    {
        var filas = new[]
        {
            Lote(2, "0124407006", "12396", "45474", "41018", "18,01", "8,5", "9"),
            Lote(3, "0124407006", "12396", "45474", "1000"),          // lote repetido
            Lote(4, "0124407007", "99999", "45474", "1000"),          // faena no cargada
            Lote(5, "0124407008", "12396", "", "1000"),               // sin fecha
            Lote(6, "0124407009", "12396", "45474", "0"),             // peso 0
            Lote(7, "0124407010", "12396", "45474", "1000", "100"),   // ley de cobre 100 %
            Lote(8, "1411136", "5301", "45474", "11412", "0"),        // código sin ceros a la izquierda
        };
        var r = ImportadorPlanillas.Recepciones(filas, c => c is "12396" or "05301");
        Assert.Equal(7, r.FilasLeidas);
        Assert.Equal(2, r.Validas.Count);
        Assert.Equal(4, r.Rechazadas.Count);
        Assert.Single(r.Observaciones);
        var primero = r.Validas[0];
        Assert.Equal(new DateTime(2024, 7, 1), primero.FechaCierre);
        Assert.Equal(41018m, primero.PesoSecoKg);
        Assert.Equal(18.01m, primero.LeyCuPct);
        Assert.Equal("05301", r.Validas[1].CodigoFaena);
    }

    [Fact]
    public void El_historial_usa_los_ultimos_6_meses_del_reporte_y_pondera_las_leyes()
    {
        var e = new Entorno();
        void Agregar(string lote, int idFaena, DateTime fecha, decimal kg, decimal cu) =>
            e.Db.Agregar(new RecepcionMineral { NroLote = lote, IdFaena = idFaena, FechaCierre = fecha, PesoSecoKg = kg, LeyCuPct = cu, Agencia = "1", Producto = "4" });
        Agregar("1", e.IdFaenaElegible, new(2024, 12, 20), 30_000, 2);    // diciembre: corte del reporte
        Agregar("2", e.IdFaenaElegible, new(2024, 12, 5), 10_000, 4);
        Agregar("3", e.IdFaenaElegible, new(2024, 7, 1), 20_000, 3);      // julio: dentro de la ventana
        Agregar("4", e.IdFaenaElegible, new(2024, 6, 30), 50_000, 9);     // junio: fuera de la ventana
        Agregar("5", e.IdFaenaElegible, new(2024, 8, 1), 5_000, 0);       // sin leyes: suma toneladas, no pondera
        Agregar("6", e.IdFaenaInactiva, new(2024, 12, 31), 99_000, 1);    // otra faena

        var h = e.Fichas.Produccion(e.IdFaenaElegible)!;
        Assert.Equal(new DateTime(2024, 7, 1), h.Desde);
        Assert.Equal(new DateTime(2024, 12, 31), h.Hasta);
        Assert.Equal(6, h.Meses.Count);
        Assert.Equal(4, h.Lotes);
        Assert.Equal(65, h.TmsTotal, 6);
        Assert.Equal(65 / 6.0, h.TmsPromedioMensual, 6);
        Assert.Equal(3, h.MesesConEntregas);
        // (30·2 + 10·4 + 20·3) / 60 = 2,6667 %
        Assert.Equal(160 / 60.0, h.LeyCuPct, 6);
    }

    [Fact]
    public void Sin_recepcion_cargada_no_hay_historial()
    {
        var e = new Entorno();
        Assert.Null(e.Fichas.Produccion(e.IdFaenaElegible));
    }

    [Fact]
    public void Registra_y_reemplaza_recursos_por_categoria()
    {
        var e = new Entorno();
        var ing = e.Sesion(e.IdIngeniero);
        e.Fichas.GuardarRecurso(e.IdFaenaElegible, new DatosRecurso { Categoria = CategoriasRecurso.Medido, Toneladas = 12_000, LeyCuPct = 2.1m, Fuente = "Visita técnica" }, ing);
        e.Fichas.GuardarRecurso(e.IdFaenaElegible, new DatosRecurso { Categoria = CategoriasRecurso.Inferido, Toneladas = 40_000, LeyAuGt = 5 }, ing);
        e.Fichas.GuardarRecurso(e.IdFaenaElegible, new DatosRecurso { Categoria = CategoriasRecurso.Medido, Toneladas = 15_000, LeyCuPct = 2.3m }, ing);
        var lista = e.Fichas.Recursos(e.IdFaenaElegible);
        Assert.Equal(2, lista.Count);
        Assert.Equal(CategoriasRecurso.Medido, lista[0].Categoria);
        Assert.Equal(15_000m, lista[0].Toneladas);
        Assert.Contains(e.Db.Tabla<Auditoria>(), a => a.Accion == "RECURSO_FAENA");

        var error = Assert.Throws<ErrorNegocio>(() => e.Fichas.GuardarRecurso(e.IdFaenaElegible, new DatosRecurso { Categoria = CategoriasRecurso.Indicado, Toneladas = 0 }, ing));
        Assert.Contains("mayores que cero", error.Message);
        Assert.Contains("al menos una ley", error.Message);
        Assert.Throws<ErrorNegocio>(() => e.Fichas.GuardarRecurso(e.IdFaenaElegible,
            new DatosRecurso { Categoria = CategoriasRecurso.Indicado, Toneladas = 10, LeyCuPct = 1 }, e.Sesion(e.IdJefatura)));

        e.Fichas.EliminarRecurso(lista[1].Id, ing);
        Assert.Single(e.Fichas.Recursos(e.IdFaenaElegible));
    }

    [Theory]
    [InlineData("juan", 1)]
    [InlineData("PEREZ", 1)]       // sin tilde encuentra «Pérez»
    [InlineData("san jos", 1)]     // por el nombre de la faena
    [InlineData("zz", 0)]          // menos de 3 letras
    [InlineData("Gonzalez", 0)]
    public void Busca_productores_por_nombre_o_por_faena(string texto, int esperados)
    {
        var e = new Entorno();
        var r = e.Fichas.BuscarPorNombre(texto);
        Assert.Equal(esperados, r.Count);
        if (esperados > 0)
        {
            Assert.Equal("12345678-5", r[0].Rut);
            Assert.Equal(3, r[0].Faenas);
        }
    }

    [Fact]
    public void VA_y_PAGO_dan_lo_mismo_que_Excel()
    {
        // Excel: =PAGO(1%;12;-1000) = 88,8488 ; =VA(5%/12;60;-500) = 26.495,35 ; =VA(0;10;-100) = 1.000
        Assert.Equal(88.8488, Funciones.Pago(0.01, 12, -1000), 4);
        Assert.Equal(26495.35, Funciones.Va(0.05 / 12, 60, -500), 2);
        Assert.Equal(1000, Funciones.Va(0, 10, -100), 9);
        Assert.Equal(-1000, Funciones.Va(0.01, 12, Funciones.Pago(0.01, 12, -1000)), 6);
        // pago al inicio del período (tipo = 1)
        Assert.Equal(87.9691, Funciones.Pago(0.01, 12, -1000, 0, 1), 4);
    }

    [Fact]
    public void Las_formulas_aceptan_VA_y_PAGO()
    {
        var p = PlantillaEjemplo.Crear();
        p.Formulas.Add(new FormulaCalculo { Codigo = "Recuperable", Nombre = "Recuperable", Expresion = "VA(TasaPeriodo; PlazoMeses; -FlujoPeriodo)" });
        var r = MotorEvaluacion.Evaluar(p);
        Assert.True(r.Exitoso, string.Join(" ", r.Errores));
        var periodo = r.Periodos[0].ValoresFormulas;
        Assert.Equal(-periodo["CuotaCredito"], Funciones.Pago(periodo["TasaPeriodo"], 24, 65_000), 6);
        Assert.True(periodo["Recuperable"] > 0);

        p.Formulas[^1].Expresion = "VA(TasaPeriodo; -1; 1)";
        Assert.False(MotorEvaluacion.Evaluar(p).Exitoso);
        p.Formulas[^1].Expresion = "PAGO(TasaPeriodo)";
        Assert.False(MotorFormulas.Compilar(p).EsValido);
    }

    [Fact]
    public void La_plantilla_de_ejemplo_calcula_en_dolares()
    {
        var p = PlantillaEjemplo.Crear();
        Assert.Equal("US$", p.Variables.Single(v => v.Codigo == "Inversion").Unidad);
        Assert.DoesNotContain(p.Formulas, f => f.Expresion.Contains("Dolar"));
        var e = new Entorno();
        var antes = e.Solicitudes.Simular(e.DatosCompletos(e.IdFaenaElegible));
        e.Parametros.Registrar("Dolar", 1_100, "BOT-01");
        var despues = e.Solicitudes.Simular(e.DatosCompletos(e.IdFaenaElegible));
        Assert.Equal(antes.Van, despues.Van, 6);   // el dólar solo sirve para mostrar pesos de referencia
    }
}
