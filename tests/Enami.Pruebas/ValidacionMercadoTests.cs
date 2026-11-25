using Enami.Dominio.Servicios;

namespace Enami.Pruebas;

/// <summary>Validación de rango de los valores de BOT-01 antes de guardarlos.</summary>
public class ValidacionMercadoTests
{
    [Theory]
    [InlineData(1018.0, 1017.66)]     // variación normal del dólar
    [InlineData(5.48, 5.51)]          // cobre del respaldo frente al de COCHILCO
    [InlineData(48.3, 39.4)]          // plata real frente al valor de referencia de la planilla (+23 %)
    public void Acepta_variaciones_razonables(double nuevo, double vigente) =>
        Assert.Null(ValidacionMercado.Revisar("X", nuevo, vigente));

    [Theory]
    [InlineData(12_080.0, 5.51)]      // cobre por tonelada en vez de por libra
    [InlineData(1.0, 1017.66)]        // dólar en otra unidad
    [InlineData(0.0, 5.51)]
    [InlineData(-3.0, null)]
    public void Rechaza_valores_fuera_de_rango_o_no_positivos(double nuevo, double? vigente) =>
        Assert.NotNull(ValidacionMercado.Revisar("X", nuevo, vigente));

    [Fact]
    public void Sin_valor_vigente_acepta_cualquier_valor_positivo() =>
        Assert.Null(ValidacionMercado.Revisar("PrecioAu", 4102.35, null));
}
