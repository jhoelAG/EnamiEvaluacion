namespace Enami.Dominio.Calculo.Expresiones;

/// <summary>
/// Analizador sintáctico descendente recursivo.
/// Precedencia (de menor a mayor): comparación, suma/resta, multiplicación/división, potencia (asociativa a la derecha), signo, primario.
/// </summary>
public sealed class Parser
{
    private readonly List<Token> _tokens;
    private int _pos;

    private Parser(List<Token> tokens) => _tokens = tokens;

    public static Nodo Analizar(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            throw new FormulaException("La fórmula está vacía.", 0);
        var p = new Parser(Lexer.Tokenizar(texto));
        var nodo = p.Comparacion();
        if (p.Actual.Tipo != TipoToken.Fin)
            throw new FormulaException($"Símbolo inesperado «{p.Actual.Texto}».", p.Actual.Posicion);
        return nodo;
    }

    private Token Actual => _tokens[_pos];
    private Token Avanzar() => _tokens[_pos++];

    private Nodo Comparacion()
    {
        var izq = Suma();
        while (Actual.Tipo is TipoToken.Menor or TipoToken.MenorIgual or TipoToken.Mayor or TipoToken.MayorIgual or TipoToken.Igual or TipoToken.Distinto)
        {
            var op = Avanzar();
            izq = new NodoBinario(op.Tipo, izq, Suma(), op.Posicion);
        }
        return izq;
    }

    private Nodo Suma()
    {
        var izq = Producto();
        while (Actual.Tipo is TipoToken.Mas or TipoToken.Menos)
        {
            var op = Avanzar();
            izq = new NodoBinario(op.Tipo, izq, Producto(), op.Posicion);
        }
        return izq;
    }

    private Nodo Producto()
    {
        var izq = Unario();
        while (Actual.Tipo is TipoToken.Por or TipoToken.Division)
        {
            var op = Avanzar();
            izq = new NodoBinario(op.Tipo, izq, Unario(), op.Posicion);
        }
        return izq;
    }

    private Nodo Unario()
    {
        if (Actual.Tipo is TipoToken.Menos or TipoToken.Mas)
        {
            var op = Avanzar();
            return new NodoUnario(op.Tipo, Unario());
        }
        return Potencia();
    }

    private Nodo Potencia()
    {
        var base_ = Primario();
        if (Actual.Tipo == TipoToken.Potencia)
        {
            var op = Avanzar();
            // asociativa a la derecha: 2^3^2 = 2^(3^2); admite exponente con signo: 2^-1
            return new NodoBinario(TipoToken.Potencia, base_, Unario(), op.Posicion);
        }
        return base_;
    }

    private Nodo Primario()
    {
        var t = Actual;
        switch (t.Tipo)
        {
            case TipoToken.Numero:
                Avanzar();
                return new NodoNumero(t.Valor);

            case TipoToken.Identificador:
                Avanzar();
                if (Actual.Tipo == TipoToken.ParentesisIzq)
                {
                    Avanzar();
                    var args = new List<Nodo>();
                    if (Actual.Tipo != TipoToken.ParentesisDer)
                    {
                        args.Add(Comparacion());
                        while (Actual.Tipo == TipoToken.Separador)
                        {
                            Avanzar();
                            args.Add(Comparacion());
                        }
                    }
                    Esperar(TipoToken.ParentesisDer, "Falta cerrar el paréntesis de la función " + t.Texto + ".");
                    Funciones.ValidarAridad(t.Texto, args.Count, t.Posicion);
                    return new NodoFuncion(t.Texto.ToUpperInvariant(), args, t.Posicion);
                }
                return new NodoIdentificador(t.Texto, t.Posicion);

            case TipoToken.ParentesisIzq:
                Avanzar();
                var interior = Comparacion();
                Esperar(TipoToken.ParentesisDer, "Falta cerrar un paréntesis.");
                return interior;

            case TipoToken.Fin:
                throw new FormulaException("La fórmula termina antes de tiempo.", t.Posicion);

            default:
                throw new FormulaException($"Símbolo inesperado «{t.Texto}».", t.Posicion);
        }
    }

    private void Esperar(TipoToken tipo, string mensaje)
    {
        if (Actual.Tipo != tipo) throw new FormulaException(mensaje, Actual.Posicion);
        Avanzar();
    }
}
