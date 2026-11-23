using Enami.Dominio.Importacion;

namespace Enami.Pruebas;

public class ImportacionTests
{
    [Theory]
    [InlineData("12.345.678-5", "12345678-5")]
    [InlineData(" 123456785 ", "12345678-5")]
    [InlineData("7654321-k", "7654321-K")]
    public void Normaliza_el_RUT(string entrada, string esperado) =>
        Assert.Equal(esperado, ReglasCalidad.NormalizarRut(entrada));

    [Theory]
    [InlineData("12345678-5", true)]
    [InlineData("12345678-4", false)]
    [InlineData("11111111-1", true)]
    public void Valida_el_digito_verificador(string rut, bool esperado) =>
        Assert.Equal(esperado, ReglasCalidad.DigitoVerificadorValido(rut));

    [Theory]
    [InlineData(0, false)]
    [InlineData(80000, false)]
    [InlineData(60, true)]
    public void Valida_la_distancia_al_poder_de_compra(double km, bool valida) =>
        Assert.Equal(valida, ReglasCalidad.ValidarDistancia(km) is null);

    [Theory]
    [InlineData(100, false)]
    [InlineData(0, false)]
    [InlineData(2.5, true)]
    public void Valida_la_ley_de_cobre(double ley, bool valida) =>
        Assert.Equal(valida, ReglasCalidad.ValidarLeyCobre(ley) is null);

    [Fact]
    public void Trata_01_01_1900_como_fecha_vacia()
    {
        Assert.Null(ReglasCalidad.LeerFecha("01-01-1900"));
        Assert.Null(ReglasCalidad.LeerFecha("1"));                       // número de serie de Excel = 1900
        Assert.Equal(new DateTime(2024, 3, 15), ReglasCalidad.LeerFecha("45366"));
    }

    [Theory]
    [InlineData("  CONC CU   ", "CONC CU")]
    [InlineData("CONC   CU, CONC AU", "CONC CU, CONC AU")]
    [InlineData("   ", null)]
    public void Quita_espacios_de_relleno(string entrada, string? esperado) =>
        Assert.Equal(esperado, ReglasCalidad.LimpiarTexto(entrada));

    [Theory]
    [InlineData("921-2", "09212")]
    [InlineData("12396", "12396")]
    [InlineData("315k", "0315K")]
    public void Normaliza_el_codigo_de_faena(string entrada, string esperado) =>
        Assert.Equal(esperado, ReglasCalidad.NormalizarCodigoFaena(entrada));

    [Fact]
    public void Importa_faenas_aislando_las_filas_con_problemas()
    {
        var filas = new List<(int, Dictionary<string, string>)>
        {
            (2, new() { ["Código"] = "12396", ["RUT"] = "12345678-5", ["Faena Nombre"] = "San José", ["Estado"] = "Activa", ["Oficina"] = "Oficina Minera Copiapó", ["Distancia Poder de Compra"] = "60" }),
            (3, new() { ["Código"] = "20781", ["RUT"] = "12345678-5", ["Faena Nombre"] = "Anamaría", ["Estado"] = "Activa", ["Oficina"] = "", ["Distancia Poder de Compra"] = "80000" }),
            (4, new() { ["Código"] = "30000", ["RUT"] = "99999999-9", ["Faena Nombre"] = "Sin productor", ["Estado"] = "Activa" }),
        };
        var r = ImportadorPlanillas.Faenas(filas, rut => rut == "12345678-5");
        Assert.Equal(2, r.Validas.Count);
        Assert.Single(r.Rechazadas);
        Assert.Null(r.Validas[1].DistanciaKm);          // 80.000 km se deja sin dato
        Assert.Contains(r.Observaciones, o => o.Campo == "Oficina");
    }

    [Fact]
    public void Importa_productores_separando_nombres_y_mapeando_el_cumplimiento()
    {
        var filas = new List<(int, Dictionary<string, string>)>
        {
            (2, new() { ["RUT"] = "12.345.678-5", ["Nombre"] = "Juan Andrés Pérez Soto", ["Tipo"] = "Natural", ["Comuna"] = "Copiapó", ["Región"] = "Atacama", ["Nivel de Cumplimiento con deudas en ENAMI"] = "Alto", ["Última Actualización Fecha"] = "45366" }),
            (3, new() { ["RUT"] = "76.543.210-3", ["Nombre"] = "Minera Los Pinos SpA", ["Tipo"] = "Jurídica", ["Nivel de Cumplimiento con deudas en ENAMI"] = "Bajo" }),
            (4, new() { ["RUT"] = "sin rut", ["Nombre"] = "RUT inválido" }),
            (5, new() { ["RUT"] = "12345678-5", ["Nombre"] = "Juan Pérez", ["Tipo"] = "Natural", ["Última Actualización Fecha"] = "45000" }),
        };
        var r = ImportadorPlanillas.Productores(filas);
        Assert.Equal(2, r.Validas.Count);
        Assert.Single(r.Rechazadas);
        var juan = r.Validas.Single(p => p.Rut == "12345678-5");
        Assert.Equal("Juan Andrés", juan.Nombres);          // se conserva la ficha más reciente (fila 2)
        Assert.Equal("Pérez Soto", juan.Apellidos);
        Assert.Equal("AL_DIA", juan.NivelCumplimiento);
        var empresa = r.Validas.Single(p => p.Rut == "76543210-3");
        Assert.Equal("EMPRESA", empresa.Tipo);
        Assert.Equal("Minera Los Pinos SpA", empresa.RazonSocial);
        Assert.Equal("MOROSO", empresa.NivelCumplimiento);
    }

    [Fact]
    public void De_los_padrones_conserva_el_activo_con_vencimiento_mas_lejano()
    {
        var filas = new List<(int, Dictionary<string, string>)>
        {
            (2, new() { ["Nro Padrón"] = "100", ["Estado Padrón"] = "Vencido", ["Código Faena"] = "12396", ["Fecha Vencimiento"] = "31-12-2030" }),
            (3, new() { ["Nro Padrón"] = "200", ["Estado Padrón"] = "Activo", ["Código Faena"] = "12396", ["Fecha Vencimiento"] = "30-06-2027" }),
            (4, new() { ["Nro Padrón"] = "300", ["Estado Padrón"] = "Activo", ["Código Faena"] = "12396", ["Fecha Vencimiento"] = "31-03-2027" }),
            (5, new() { ["Nro Padrón"] = "400", ["Estado Padrón"] = "Vencido", ["Código Faena"] = "921-2", ["Fecha Vencimiento"] = "31-12-1998" }),
        };
        var r = ImportadorPlanillas.Padrones(filas);
        Assert.Equal(2, r.Validas.Count);
        var p = r.Validas.Single(x => x.CodigoFaena == "12396");
        Assert.Equal("200", p.NroPadron);
        Assert.Equal(new DateTime(2027, 6, 30), p.Vencimiento);
        Assert.Equal(new DateTime(1998, 12, 31), r.Validas.Single(x => x.CodigoFaena == "09212").Vencimiento);
    }
}
