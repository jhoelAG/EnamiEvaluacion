using Enami.Dominio.Calculo;
using Enami.Dominio.Modelo;
using Enami.Dominio.Servicios;

namespace Enami.Pruebas;

/// <summary>Sprint 5: formulario configurable y cálculo del Anexo 2 por escenarios de precio.</summary>
public class Sprint5Tests
{
    [Fact]
    public void El_Anexo_2_reproduce_la_planilla_de_ENAMI()
    {
        // Valores esperados: hoja «anexos 2 y 4» de Ev.Proyectos_Reconocimiento_Minerales_enero_2026_ejemplo.xlsx
        var r = MotorEvaluacion.Evaluar(PlantillaAnexo2.Crear());
        Assert.True(r.Exitoso, string.Join(" ", r.Errores));
        Assert.Equal(ModoCalculo.Escenarios, r.Modo);
        Assert.Equal(9, r.Escenarios.Count);
        Assert.Equal(3145, r.Escenarios[0].Precio);
        Assert.Equal(3285, r.Escenarios[7].Precio);
        Assert.Equal(3224.5, r.PrecioProyectado);
        Assert.Equal("US$/oz", r.UnidadPrecio);

        var primera = r.Escenarios[0].ValoresFormulas;
        Assert.Equal(32352.054, primera["Reservas"], 3);
        Assert.Equal(107.84, primera["Horizonte"], 2);
        Assert.Equal(60, primera["PeriodoPago"], 9);
        Assert.Equal(8.662857, primera["LeyAuProd"], 5);
        Assert.Equal(579.2447, primera["Ingreso"], 3);          // tarifa!T31
        Assert.Equal(191.351, primera["CostoTotal"], 3);        // C34
        Assert.Equal(358.932, primera["Excedente"], 3);         // E34
        Assert.Equal(5161903.914, primera["CapacidadPago"], 2); // F34
        Assert.Equal(0.2, primera["TasaDevolucion"], 9);        // G34

        var proyectada = r.Escenarios[^1].ValoresFormulas;
        Assert.Equal(593.5758, proyectada["Ingreso"], 3);       // B42
        Assert.Equal(161450, r.Recuperacion, 6);                // H42 = I28
        Assert.Equal(7683901.91, r.Van, 1);                     // I42
        Assert.Equal(161450, r.Inversion, 6);
        Assert.Equal(372.069, r.PrecioCritico!.Value, 2);       // D28
        Assert.Equal(488.178, r.PrecioMinimoRetorno!.Value, 2); // D29
        Assert.True(r.Viable);
    }

    [Fact]
    public void Sin_capacidad_de_pago_suficiente_no_es_viable()
    {
        var p = PlantillaAnexo2.Crear();
        var r = MotorEvaluacion.Evaluar(p, new Dictionary<string, double> { ["RecursosIndicados"] = 300, ["ProduccionMensual"] = 20 });
        Assert.True(r.Exitoso, string.Join(" ", r.Errores));
        Assert.True(r.Recuperacion < r.Inversion);
        Assert.False(r.Viable);
    }

    [Fact]
    public void El_modo_por_escenarios_exige_sus_formulas_y_no_permite_texto_en_calculos()
    {
        var p = PlantillaAnexo2.Crear();
        p.Formulas.RemoveAll(f => f.Codigo == "Utilidad");
        p.Formulas.Add(new FormulaCalculo { Codigo = "Mala", Nombre = "Mala", Expresion = "ResenaProyecto * 2" });
        var c = MotorFormulas.Compilar(p);
        Assert.Contains(c.Errores, e => e.Mensaje.Contains("«Utilidad»"));
        Assert.Contains(c.Errores, e => e.Mensaje.Contains("campo de texto"));

        var q = PlantillaAnexo2.Crear();
        q.Formulas.Single(f => f.Codigo == "PrecioProyectado").Expresion = "Precio * 1";
        Assert.Contains(MotorFormulas.Compilar(q).Errores, e => e.Mensaje.Contains("no puede depender"));

        var s = PlantillaAnexo2.Crear();
        s.Variables.Single(v => v.Codigo == "Producto").Opciones = "";
        Assert.Contains(MotorFormulas.Compilar(s).Errores, e => e.Mensaje.Contains("opciones"));
    }

    [Fact]
    public void Las_plantillas_caben_en_las_columnas_de_la_base_de_datos()
    {
        // SQL Server rechaza textos más largos que la columna (por ejemplo, unidad de 15 caracteres)
        foreach (var p in new[] { PlantillaAnexo2.Crear(), PlantillaEjemplo.Crear() })
            Assert.True(MotorFormulas.Compilar(p).EsValido, string.Join(" ", MotorFormulas.Compilar(p).Errores));
        var q = PlantillaAnexo2.Crear();
        q.Variables[0].Unidad = "una unidad demasiado larga";
        Assert.Contains(MotorFormulas.Compilar(q).Errores, e => e.Mensaje.Contains("hasta 15"));
    }

    [Fact]
    public void Interseccion_igual_que_Excel()
    {
        Assert.Equal(10, MotorEvaluacion.Interseccion([10, 20, 30], [0, 1, 2])!.Value, 9);
        Assert.Equal(5, MotorEvaluacion.Interseccion([7, 9, 11], [1, 2, 3])!.Value, 9);
        Assert.Null(MotorEvaluacion.Interseccion([1, 2], [5, 5]));
        Assert.Equal(0, MotorEvaluacion.Interseccion([-10, 0], [0, 1])!.Value);  // negativo => 0
    }

    [Theory]
    [InlineData(1.239, 2, 1.23)]
    [InlineData(-1.239, 2, -1.23)]
    [InlineData(7.9, 0, 7)]
    public void TRUNCAR_corta_sin_redondear(double x, int d, double esperado) =>
        Assert.Equal(esperado, Enami.Dominio.Calculo.Expresiones.Funciones.Ejecutar("TRUNCAR", [x, d], 0), 9);

    private static List<DatosLineaPresupuesto> PresupuestoEjemplo(IReadOnlyList<LaborTarifada> labores)
    {
        short Id(string nombre) => labores.Single(l => l.Nombre == nombre).Id;
        return
        [
            new() { IdLabor = Id("Sondajes"), Programa = ProgramasPresupuesto.Reconocimiento, Cantidad = 1000, CostoUnitario = 131, PorcentajeFinanciamiento = 1 },
            new() { IdLabor = Id("Análisis químico"), Programa = ProgramasPresupuesto.Reconocimiento, Cantidad = 25, CostoUnitario = 36, PorcentajeFinanciamiento = 1 },
            new() { IdLabor = Id("Gastos notariales y legales"), Programa = ProgramasPresupuesto.Propio, Cantidad = 1000, CostoUnitario = 1, PorcentajeFinanciamiento = 0 },
            new() { IdLabor = Id("Galerías 2,5 x 2,5"), Programa = ProgramasPresupuesto.Desarrollo, Cantidad = 20, CostoUnitario = 590, PorcentajeFinanciamiento = 0.5m },
            new() { IdLabor = Id("Rampas 4,0 x 4,0"), Programa = ProgramasPresupuesto.Desarrollo, Cantidad = 50, CostoUnitario = 945, PorcentajeFinanciamiento = 0.5m },
        ];
    }

    [Fact]
    public void El_presupuesto_reproduce_el_Anexo_1_de_la_planilla()
    {
        var e = new Entorno();
        var t = CalculoPresupuesto.Calcular(PresupuestoEjemplo(e.Solicitudes.Labores()), 0.8m);
        Assert.Equal(131_900m, t.CapitalRiesgo);   // anexo 1 y 3!E45
        Assert.Equal(29_550m, t.Credito);          // F45 (rampas: 945/2 = 472,5 se redondea a 473, como Excel)
        Assert.Equal(1_064m, t.Timbres);           // G31
        Assert.Equal(31_614m, t.AportePropio);     // G45
        Assert.Equal(193_064m, t.InversionTotal);  // B46

        // labores mineras al 80 %: ENAMI aporta INT(0,8 × costo) y el beneficiario el resto redondeado
        var pique = CalculoPresupuesto.Calcular(ProgramasPresupuesto.Reconocimiento, 10, 746, 0.8m);
        Assert.Equal(5_960m, pique.CapitalRiesgo);
        Assert.Equal(1_490m, pique.AportePropio);
    }

    [Fact]
    public void Una_solicitud_del_Anexo_2_guarda_textos_presupuesto_y_resultado()
    {
        var e = new Entorno();
        var ing = e.Sesion(e.IdIngeniero);
        byte instrumento = Semilla.InstrumentoAnexo2;
        var plantilla = e.Solicitudes.PlantillaPara(instrumento)!;
        Assert.Equal(ModoCalculo.Escenarios, plantilla.Modo);
        Assert.True(ServicioSolicitudes.UsaPresupuesto(plantilla));
        Assert.Contains(ServicioSolicitudes.CamposFormulario(plantilla), v => v.Tipo == TiposCampo.TextoLargo);
        Assert.DoesNotContain(ServicioSolicitudes.VariablesProyecto(plantilla), v => v.Codigo == "CapitalRiesgo");

        var d = new DatosSolicitud { IdFaena = e.IdFaenaElegible, IdInstrumento = instrumento, MontoSolicitado = 1, PlazoMeses = 12 };
        foreach (var v in ServicioSolicitudes.VariablesProyecto(plantilla)) d.DatosProyecto[v.Codigo] = v.ValorPorDefecto;
        d.Textos["ResenaProyecto"] = "Reconocer estructura en corrida y altura.";
        d.Presupuesto = PresupuestoEjemplo(e.Solicitudes.Labores());

        var r = e.Solicitudes.GuardarBorrador(null, d, ing);
        Assert.NotNull(r.Calculo);
        Assert.True(r.Calculo!.Exitoso, string.Join(" ", r.Calculo.Errores));
        Assert.Equal(161_450, r.Calculo.Recuperacion, 0);
        Assert.Equal(161_450m, e.Db.Tabla<SolicitudCredito>().Single(s => s.Id == r.IdSolicitud).MontoSolicitado);   // monto = financiamiento del presupuesto

        var detalle = e.Solicitudes.Obtener(r.IdSolicitud, ing)!;
        Assert.Equal("Reconocer estructura en corrida y altura.", detalle.Datos.Textos["ResenaProyecto"]);
        Assert.Equal(5, detalle.Datos.Presupuesto.Count);
        Assert.Equal(85, detalle.Datos.DatosProyecto["RecuperacionMinera"]);
        Assert.Equal(131_900, detalle.Datos.DatosProyecto["CapitalRiesgo"]);

        // un borrador incompleto también conserva lo escrito
        var incompleto = new DatosSolicitud { IdFaena = e.IdFaenaElegible, IdInstrumento = instrumento, MontoSolicitado = 1000, PlazoMeses = 12 };
        incompleto.Textos["ProfesionalApoyo"] = "Marcela Collio";
        incompleto.DatosProyecto["Dilucion"] = 7;
        var r2 = e.Solicitudes.GuardarBorrador(null, incompleto, ing);
        Assert.Null(r2.Calculo);
        var d2 = e.Solicitudes.Obtener(r2.IdSolicitud, ing)!.Datos;
        Assert.Equal("Marcela Collio", d2.Textos["ProfesionalApoyo"]);
        Assert.Equal(7, d2.DatosProyecto["Dilucion"]);

        // presupuesto inválido
        var malo = new DatosSolicitud { IdFaena = e.IdFaenaElegible, IdInstrumento = instrumento, MontoSolicitado = 1, PlazoMeses = 12 };
        malo.Presupuesto.Add(new DatosLineaPresupuesto { IdLabor = d.Presupuesto[0].IdLabor, Cantidad = 0, CostoUnitario = 131, PorcentajeFinanciamiento = 1 });
        Assert.Contains("cantidad", Assert.Throws<ErrorNegocio>(() => e.Solicitudes.GuardarBorrador(null, malo, ing)).Message);
    }

    [Fact]
    public void Los_campos_con_fuente_se_precargan_con_recursos_y_recepcion()
    {
        var e = new Entorno();
        var ing = e.Sesion(e.IdIngeniero);
        e.Fichas.GuardarRecurso(e.IdFaenaElegible, new DatosRecurso { Categoria = CategoriasRecurso.Indicado, Toneladas = 30_000, LeyCuPct = 1, LeyAuGt = 6 }, ing);
        e.Fichas.GuardarRecurso(e.IdFaenaElegible, new DatosRecurso { Categoria = CategoriasRecurso.Medido, Toneladas = 10_000, LeyCuPct = 2, LeyAuGt = 10 }, ing);
        e.Fichas.GuardarRecurso(e.IdFaenaElegible, new DatosRecurso { Categoria = CategoriasRecurso.Inferido, Toneladas = 50_000, LeyCuPct = 9, LeyAuGt = 1 }, ing);
        e.Db.Agregar(new RecepcionMineral { NroLote = "1", IdFaena = e.IdFaenaElegible, FechaCierre = new(2024, 12, 1), PesoSecoKg = 600_000, LeyCuPct = 1, LeyAuGt = 5, Agencia = "1", Producto = "4" });
        byte instrumento = Semilla.InstrumentoAnexo2;
        var p = e.Solicitudes.Precarga(e.Solicitudes.PlantillaPara(instrumento)!, e.IdFaenaElegible);
        Assert.Equal(30_000, p["RecursosIndicados"].Valor!.Value);
        Assert.Equal(50_000, p["RecursosInferidos"].Valor!.Value);
        Assert.Equal(1.25, p["LeyCu"].Valor!.Value, 6);     // ponderada por indicados y medidos: (30·1 + 10·2) / 40
        Assert.Equal(7, p["LeyAu"].Valor!.Value, 6);
        Assert.Equal(100, p["ProduccionMensual"].Valor!.Value, 6);   // 600 t / 6 meses
        Assert.Equal(1, p["MesesConEntregas"].Valor!.Value);
        Assert.Contains("recepción", p["ProduccionMensual"].Origen);
    }

    [Fact]
    public void Se_precargan_los_datos_del_productor_de_la_faena_y_del_usuario()
    {
        var e = new Entorno();
        var faena = e.Db.Tabla<Faena>().Single(f => f.Id == e.IdFaenaElegible);
        var productor = e.Db.Tabla<Productor>().Single(p => p.Id == faena.IdProductor);
        productor.Direccion = "Los Carrera 123, Copiapó";
        productor.Telefono = "+56 9 1234 5678";
        faena.Ubicacion = "Sierra Ánimas, Copiapó";
        faena.CoordenadaNorte = 6_980_000;
        faena.ManoObra = 8;
        e.Db.Agregar(new RecepcionMineral { NroLote = "1", IdFaena = e.IdFaenaInactiva, FechaCierre = new(2024, 12, 1), PesoSecoKg = 60_000, LeyCuPct = 2, Agencia = "1", Producto = "4" });
        e.Db.Agregar(new RecepcionMineral { NroLote = "2", IdFaena = e.IdFaenaElegible, FechaCierre = new(2024, 11, 1), PesoSecoKg = 60_000, LeyCuPct = 1, Agencia = "1", Producto = "4" });

        byte instrumento = Semilla.InstrumentoAnexo2;
        var plantilla = e.Solicitudes.PlantillaPara(instrumento)!;
        var p = e.Solicitudes.Precarga(plantilla, e.IdFaenaElegible, "Ingeniero de prueba");
        Assert.Equal("Los Carrera 123, Copiapó", p["Direccion"].Texto);
        Assert.Equal("+56 9 1234 5678", p["Telefono"].Texto);
        Assert.Equal("Sierra Ánimas, Copiapó", p["UbicacionFaena"].Texto);
        Assert.Contains("Copiapó", p["ComunaFaena"].Texto);
        Assert.Equal("Ingeniero de prueba", p["ProfesionalApoyo"].Texto);
        Assert.Equal(6_980_000, p["CoordenadaNorte"].Valor!.Value);
        Assert.Equal(8, p["ManoObra"].Valor!.Value);
        Assert.Equal(10, p["ProduccionMensual"].Valor!.Value, 6);        // solo la faena: 60 t / 6 meses
        Assert.Equal(20, p["ProduccionProductor"].Valor!.Value, 6);      // todas las faenas del productor: 120 t / 6 meses
        Assert.Equal(1.5, p["LeyCuProductor"].Valor!.Value, 6);
        Assert.False(p.ContainsKey("Correo"));                            // sin dato no se precarga

        // los campos informativos (deudas, coordenadas, etc.) no son obligatorios para calcular
        var obligatorias = ServicioSolicitudes.VariablesObligatorias(plantilla).Select(v => v.Codigo).ToList();
        Assert.Contains("ProduccionMensual", obligatorias);
        Assert.DoesNotContain("DeudaTotal", obligatorias);
        Assert.DoesNotContain("CoordenadaNorte", obligatorias);
    }

    [Theory]
    [InlineData("Masculino:5, Femenino:1", "Masculino:2", (short)8)]
    [InlineData("", null, null)]
    [InlineData("Jefe Mina:1, Perforista:2", "", (short)3)]
    public void La_mano_de_obra_suma_la_ficha_de_faena(string? formal, string? informal, short? esperado) =>
        Assert.Equal(esperado, Enami.Dominio.Importacion.ImportadorPlanillas.ManoObra(formal, informal));

    [Fact]
    public void La_ubicacion_junta_sierra_y_localidad()
    {
        Assert.Equal("Sierra Exploradora, Taltal", Enami.Dominio.Importacion.ImportadorPlanillas.Ubicacion("EXPLORADORA", "Taltal"));
        Assert.Equal("Taltal", Enami.Dominio.Importacion.ImportadorPlanillas.Ubicacion(null, "Taltal"));
        Assert.Null(Enami.Dominio.Importacion.ImportadorPlanillas.Ubicacion("", null));
    }

    [Fact]
    public void Se_eliminan_borradores_y_versiones_retiradas_sin_uso()
    {
        var e = new Entorno();
        var repo = e.Plantillas;
        var vigente = repo.ObtenerVigente(1)!;
        Assert.Contains("vigente", repo.MotivoNoEliminable(vigente.Id));
        Assert.Throws<InvalidOperationException>(() => repo.Eliminar(vigente.Id));

        var borrador = repo.CrearBorrador(vigente.Id, e.IdJefatura);
        Assert.Null(repo.MotivoNoEliminable(borrador.Id));
        repo.Eliminar(borrador.Id);
        Assert.Null(repo.Obtener(borrador.Id));
        Assert.DoesNotContain(e.Db.Tabla<VariablePlantilla>(), v => v.IdPlantilla == borrador.Id);

        // una versión usada en una evaluación guardada se conserva aunque esté retirada
        var r = e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible), e.Sesion(e.IdIngeniero));
        Assert.NotNull(r.Calculo);
        var nueva = repo.CrearBorrador(vigente.Id, e.IdJefatura);
        repo.Activar(nueva.Id, e.IdJefatura);
        Assert.Contains("evaluación", repo.MotivoNoEliminable(vigente.Id));
    }
}
