using Enami.Dominio.Calculo.Expresiones;

namespace Enami.Pruebas;

public class ExpresionesTests
{
    private sealed class Ctx(Dictionary<string, double> v) : IContextoEvaluacion
    {
        public double ObtenerValor(string id) => v.TryGetValue(id, out var x) ? x : throw new FormulaException("sin valor " + id);
    }

    private static double Eval(string f, Dictionary<string, double>? v = null) =>
        Parser.Analizar(f).Evaluar(new Ctx(v ?? []));

    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("10 / 4", 2.5)]
    [InlineData("2 ^ 3 ^ 2", 512)]          // potencia asociativa a la derecha
    [InlineData("-2 ^ 2", -4)]
    [InlineData("2 ^ -1", 0.5)]
    [InlineData("1,5 + 1.5", 3)]            // coma o punto decimal
    [InlineData("SI(3 > 2; 10; 20)", 10)]
    [InlineData("SI(3 <= 2; 10; 20)", 20)]
    [InlineData("MAX(1; 7; 3) - MIN(4; 2)", 5)]
    [InlineData("REDONDEAR(2,345; 2)", 2.35)]
    [InlineData("Y(1 > 0; 2 <> 3) + O(0; 0)", 1)]
    [InlineData("ABS(-4) + RAIZ(9) + POTENCIA(2; 2)", 11)]
    public void Evalua_operadores_y_funciones(string formula, double esperado) =>
        Assert.Equal(esperado, Eval(formula), 9);

    [Fact]
    public void Usa_variables_del_contexto() =>
        Assert.Equal(25, Eval("Tonelaje * LeyCu / 100", new() { ["Tonelaje"] = 1000, ["LeyCu"] = 2.5 }), 9);

    [Theory]
    [InlineData("1 +")]
    [InlineData("(1 + 2")]
    [InlineData("SI(1; 2)")]
    [InlineData("FUNCIONX(1)")]
    [InlineData("2 $ 3")]
    [InlineData("")]
    public void Rechaza_formulas_mal_escritas(string formula) =>
        Assert.Throws<FormulaException>(() => Parser.Analizar(formula));

    [Fact]
    public void Division_por_cero_entrega_mensaje_claro()
    {
        var ex = Assert.Throws<FormulaException>(() => Eval("1 / (2 - 2)"));
        Assert.Contains("cero", ex.Message);
    }

    [Fact]
    public void SI_no_evalua_la_rama_descartada() =>
        Assert.Equal(0, Eval("SI(0; 1 / 0; 0)"));
}
