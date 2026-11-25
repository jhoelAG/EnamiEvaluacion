using System.Text;
using Enami.Dominio.Servicios;

namespace Enami.Pruebas;

/// <summary>Lectura de la API del Banco Central (BOT-01), incluida su respuesta con tildes en Latin-1.</summary>
public class BancoCentralTests
{
    private const string Respuesta =
        "{\"Codigo\":0,\"Descripcion\":\"Success\",\"Series\":{\"descripEsp\":\"Tipo de cambio nominal (dólar observado $CLP/USD)\"," +
        "\"seriesId\":\"F073.TCO.PRE.Z.D\",\"Obs\":[" +
        "{\"indexDateString\":\"20-11-2026\",\"value\":\"948.10\",\"statusCode\":\"OK\"}," +
        "{\"indexDateString\":\"21-11-2026\",\"value\":\"951.35\",\"statusCode\":\"OK\"}," +
        "{\"indexDateString\":\"22-11-2026\",\"value\":\"NaN\",\"statusCode\":\"ND\"}]}}";

    [Fact]
    public void Lee_la_respuesta_en_latin1_y_toma_la_ultima_observacion_publicada()
    {
        var texto = BancoCentral.Decodificar(Encoding.Latin1.GetBytes(Respuesta));   // como la envía el Banco Central
        var r = BancoCentral.LeerUltimo(texto, out var error);
        Assert.Null(error);
        Assert.Equal(951.35, r!.Value.Valor);
        Assert.Equal(new DateTime(2026, 11, 21), r.Value.Fecha);
        Assert.Contains("dólar", texto);
    }

    [Fact]
    public void Tambien_acepta_utf8() =>
        Assert.Equal(951.35, BancoCentral.LeerUltimo(BancoCentral.Decodificar(Encoding.UTF8.GetBytes(Respuesta)), out _)!.Value.Valor);

    [Fact]
    public void Informa_el_error_del_banco_central()
    {
        var r = BancoCentral.LeerUltimo("{\"Codigo\":-5,\"Descripcion\":\"Invalid username or password\",\"Series\":{}}", out var error);
        Assert.Null(r);
        Assert.Contains("-5", error);
    }

    [Fact]
    public void Sin_valores_publicados_no_entrega_dato()
    {
        var r = BancoCentral.LeerUltimo("{\"Codigo\":0,\"Series\":{\"Obs\":[{\"indexDateString\":\"22-11-2026\",\"value\":\"NaN\",\"statusCode\":\"ND\"}]}}", out var error);
        Assert.Null(r);
        Assert.NotNull(error);
    }

    [Fact]
    public void La_consulta_incluye_la_serie_y_el_rango_de_fechas()
    {
        var url = BancoCentral.Consulta("u@x.cl", "c&1", BancoCentral.Series["PrecioAu"].Serie, new DateTime(2026, 11, 25));
        Assert.Contains("timeseries=F019.PPB.PRE.44.D", url);
        Assert.Contains("firstdate=2026-11-10", url);
        Assert.Contains("pass=c%261", url);
    }
}
