namespace Enami.Dominio.Calculo.Expresiones;

/// <summary>Error de sintaxis o de evaluación en una fórmula, con la posición del problema.</summary>
public class FormulaException(string mensaje, int posicion = -1) : Exception(mensaje)
{
    public int Posicion { get; } = posicion;
}

/// <summary>Contexto de evaluación: entrega el valor de cada identificador.</summary>
public interface IContextoEvaluacion
{
    double ObtenerValor(string identificador);
}

/// <summary>Nodo del árbol sintáctico de una fórmula.</summary>
public abstract class Nodo
{
    public abstract double Evaluar(IContextoEvaluacion contexto);

    /// <summary>Agrega a la colección los identificadores (variables o fórmulas) que usa el nodo.</summary>
    public abstract void RecolectarIdentificadores(ISet<string> destino);
}

public sealed class NodoNumero(double valor) : Nodo
{
    public double Valor { get; } = valor;
    public override double Evaluar(IContextoEvaluacion contexto) => Valor;
    public override void RecolectarIdentificadores(ISet<string> destino) { }
}

public sealed class NodoIdentificador(string nombre, int posicion) : Nodo
{
    public string Nombre { get; } = nombre;
    public int Posicion { get; } = posicion;
    public override double Evaluar(IContextoEvaluacion contexto) => contexto.ObtenerValor(Nombre);
    public override void RecolectarIdentificadores(ISet<string> destino) => destino.Add(Nombre);
}

public sealed class NodoUnario(TipoToken operador, Nodo operando) : Nodo
{
    public override double Evaluar(IContextoEvaluacion contexto)
    {
        double v = operando.Evaluar(contexto);
        return operador == TipoToken.Menos ? -v : v;
    }
    public override void RecolectarIdentificadores(ISet<string> destino) => operando.RecolectarIdentificadores(destino);
}

public sealed class NodoBinario(TipoToken operador, Nodo izquierdo, Nodo derecho, int posicion) : Nodo
{
    public override double Evaluar(IContextoEvaluacion contexto)
    {
        double a = izquierdo.Evaluar(contexto);
        double b = derecho.Evaluar(contexto);
        return operador switch
        {
            TipoToken.Mas => a + b,
            TipoToken.Menos => a - b,
            TipoToken.Por => a * b,
            TipoToken.Division => b == 0 ? throw new FormulaException("División por cero.", posicion) : a / b,
            TipoToken.Potencia => Math.Pow(a, b),
            TipoToken.Menor => a < b ? 1 : 0,
            TipoToken.MenorIgual => a <= b ? 1 : 0,
            TipoToken.Mayor => a > b ? 1 : 0,
            TipoToken.MayorIgual => a >= b ? 1 : 0,
            TipoToken.Igual => a == b ? 1 : 0,
            TipoToken.Distinto => a != b ? 1 : 0,
            _ => throw new FormulaException($"Operador no soportado «{operador}».", posicion)
        };
    }

    public override void RecolectarIdentificadores(ISet<string> destino)
    {
        izquierdo.RecolectarIdentificadores(destino);
        derecho.RecolectarIdentificadores(destino);
    }
}

public sealed class NodoFuncion(string nombre, IReadOnlyList<Nodo> argumentos, int posicion) : Nodo
{
    public string Nombre { get; } = nombre;
    public IReadOnlyList<Nodo> Argumentos { get; } = argumentos;

    public override double Evaluar(IContextoEvaluacion contexto)
    {
        // SI evalúa solo la rama que corresponde (evita errores en la rama descartada)
        if (Funciones.EsSi(Nombre))
        {
            double condicion = Argumentos[0].Evaluar(contexto);
            return condicion != 0 ? Argumentos[1].Evaluar(contexto) : Argumentos[2].Evaluar(contexto);
        }
        var valores = Argumentos.Select(a => a.Evaluar(contexto)).ToArray();
        return Funciones.Ejecutar(Nombre, valores, posicion);
    }

    public override void RecolectarIdentificadores(ISet<string> destino)
    {
        foreach (var a in Argumentos) a.RecolectarIdentificadores(destino);
    }
}
