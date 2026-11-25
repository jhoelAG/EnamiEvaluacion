using Enami.Dominio.Calculo;

namespace Enami.Pruebas;

public class MotorEvaluacionTests
{
    /// <summary>Plantilla mínima: flujo constante, tasa fija. Permite comparar con un cálculo hecho a mano.</summary>
    private static PlantillaCalculo PlantillaSimple(string flujo = "Flujo", string tasa = "0,10") => new()
    {
        Nombre = "Prueba",
        Version = 1,
        Variables =
        [
            new() { Codigo = "Inversion", ValorPorDefecto = 250 },
            new() { Codigo = "PlazoMeses", ValorPorDefecto = 3 },
            new() { Codigo = "Flujo", ValorPorDefecto = 100 },
        ],
        Formulas =
        [
            new() { Codigo = "FlujoPeriodo", Expresion = flujo },
            new() { Codigo = "TasaPeriodo", Expresion = tasa },
        ]
    };

    [Fact]
    public void Calcula_el_VAN_igual_que_a_mano()
    {
        // VAN = -250 + 100/1,1 + 100/1,1^2 + 100/1,1^3 = -1,3148...
        var r = MotorEvaluacion.Evaluar(PlantillaSimple());
        Assert.True(r.Exitoso, string.Join(" ", r.Errores));
        Assert.Equal(-250 + 100 / 1.1 + 100 / 1.21 + 100 / 1.331, r.Van, 9);
        Assert.False(r.Viable);
        Assert.Equal(r.Van / 250, r.Ivan, 12);
        Assert.Equal(3, r.Periodos.Count);
    }

    [Fact]
    public void Cambiar_la_formula_desde_la_interfaz_cambia_el_resultado()
    {
        var antes = MotorEvaluacion.Evaluar(PlantillaSimple());
        var despues = MotorEvaluacion.Evaluar(PlantillaSimple(flujo: "Flujo * 1,2"));
        Assert.False(antes.Viable);
        Assert.True(despues.Viable);
        Assert.Equal(-250 + 120 / 1.1 + 120 / 1.21 + 120 / 1.331, despues.Van, 9);
    }

    [Fact]
    public void Las_formulas_pueden_depender_del_periodo()
    {
        var r = MotorEvaluacion.Evaluar(PlantillaSimple(flujo: "Flujo * Periodo", tasa: "0"));
        Assert.Equal(-250 + 100 + 200 + 300, r.Van, 9);
    }

    [Fact]
    public void Calcula_las_formulas_en_orden_aunque_esten_desordenadas()
    {
        var p = PlantillaSimple(flujo: "Ingreso - Costo");
        p.Formulas.Add(new() { Codigo = "Costo", Expresion = "Ingreso * 0,4" });
        p.Formulas.Add(new() { Codigo = "Ingreso", Expresion = "Flujo * 2" });
        var r = MotorEvaluacion.Evaluar(p);
        Assert.True(r.Exitoso, string.Join(" ", r.Errores));
        Assert.Equal(120, r.Periodos[0].Flujo, 9);
    }

    [Fact]
    public void Usa_los_valores_ingresados_en_vez_de_los_por_defecto()
    {
        var r = MotorEvaluacion.Evaluar(PlantillaSimple(), new Dictionary<string, double> { ["Flujo"] = 200, ["PlazoMeses"] = 1 });
        Assert.Equal(-250 + 200 / 1.1, r.Van, 9);
    }

    [Fact]
    public void Rechaza_valores_fuera_de_rango()
    {
        var p = PlantillaSimple();
        p.Variables[2].Minimo = 0;
        var r = MotorEvaluacion.Evaluar(p, new Dictionary<string, double> { ["Flujo"] = -5 });
        Assert.False(r.Exitoso);
        Assert.Contains(r.Errores, e => e.Contains("menor que el mínimo"));
    }

    [Fact]
    public void Detecta_variable_desconocida()
    {
        var c = MotorFormulas.Compilar(PlantillaSimple(flujo: "Flujo * Precio"));
        Assert.False(c.EsValido);
        Assert.Contains(c.Errores, e => e.Mensaje.Contains("Precio"));
    }

    [Fact]
    public void Detecta_referencia_circular()
    {
        var p = PlantillaSimple(flujo: "A");
        p.Formulas.Add(new() { Codigo = "A", Expresion = "B + 1" });
        p.Formulas.Add(new() { Codigo = "B", Expresion = "A + 1" });
        var c = MotorFormulas.Compilar(p);
        Assert.Contains(c.Errores, e => e.Mensaje.StartsWith("Referencia circular"));
    }

    [Fact]
    public void Exige_las_formulas_y_variables_obligatorias()
    {
        var p = PlantillaSimple();
        p.Formulas.RemoveAll(f => f.Codigo == "TasaPeriodo");
        p.Variables.RemoveAll(v => v.Codigo == "Inversion");
        var c = MotorFormulas.Compilar(p);
        Assert.Contains(c.Errores, e => e.Mensaje.Contains("TasaPeriodo"));
        Assert.Contains(c.Errores, e => e.Mensaje.Contains("Inversion"));
    }

    [Fact]
    public void La_plantilla_de_ejemplo_es_valida_y_calcula()
    {
        var p = PlantillaEjemplo.Crear();
        var c = MotorFormulas.Compilar(p);
        Assert.True(c.EsValido, string.Join(" ", c.Errores));
        var r = MotorEvaluacion.Evaluar(p);
        Assert.True(r.Exitoso, string.Join(" ", r.Errores));
        Assert.Equal(24, r.Periodos.Count);
    }

    [Fact]
    public void El_repositorio_versiona_y_activa_plantillas()
    {
        var e = new Entorno();
        var repo = e.Plantillas;
        var vigente = repo.ObtenerVigente(1)!;
        var borrador = repo.CrearBorrador(vigente.Id, e.IdJefatura);
        Assert.Equal(EstadoPlantilla.Borrador, borrador.Estado);
        Assert.Equal(2, borrador.Version);
        // si ya hay un borrador de ese instrumento, se devuelve el mismo
        Assert.Equal(borrador.Id, repo.CrearBorrador(vigente.Id, e.IdJefatura).Id);
        borrador.Formulas.Single(f => f.Codigo == "CostoFlete").Expresion = "TonelajeMensual * DistanciaKm * TarifaFleteTonKm * 1,1";
        repo.GuardarBorrador(borrador);
        repo.Activar(borrador.Id, e.IdJefatura);
        Assert.Equal(EstadoPlantilla.Retirada, repo.Obtener(vigente.Id)!.Estado);
        var nueva = repo.ObtenerVigente(vigente.Instrumento)!;
        Assert.Equal(2, nueva.Version);
        Assert.Contains("* 1,1", nueva.Formulas.Single(f => f.Codigo == "CostoFlete").Expresion);
        Assert.Equal("Jefatura de Fomento (demo)", nueva.AprobadaPor);
        // la versión retirada conserva sus fórmulas originales (historial)
        Assert.DoesNotContain("1,1", repo.Obtener(vigente.Id)!.Formulas.Single(f => f.Codigo == "CostoFlete").Expresion);
    }

    [Fact]
    public void No_permite_activar_un_borrador_con_errores_ni_modificar_una_version_vigente()
    {
        var e = new Entorno();
        var vigente = e.Plantillas.ObtenerVigente(1)!;
        var b = e.Plantillas.CrearBorrador(vigente.Id, e.IdJefatura);
        b.Formulas.Single(f => f.Codigo == "FlujoPeriodo").Expresion = "IngresoMensual - VariableQueNoExiste";
        e.Plantillas.GuardarBorrador(b);
        Assert.Throws<InvalidOperationException>(() => e.Plantillas.Activar(b.Id, e.IdJefatura));
        Assert.Throws<InvalidOperationException>(() => e.Plantillas.GuardarBorrador(vigente));
    }
}
