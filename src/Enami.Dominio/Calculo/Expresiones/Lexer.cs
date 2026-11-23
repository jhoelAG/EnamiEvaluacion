using System.Globalization;

namespace Enami.Dominio.Calculo.Expresiones;

/// <summary>Tipos de símbolo que reconoce el analizador de fórmulas.</summary>
public enum TipoToken
{
    Numero,
    Identificador,
    Mas,
    Menos,
    Por,
    Division,
    Potencia,
    Menor,
    MenorIgual,
    Mayor,
    MayorIgual,
    Igual,
    Distinto,
    ParentesisIzq,
    ParentesisDer,
    Separador,
    Fin
}

public readonly record struct Token(TipoToken Tipo, string Texto, double Valor, int Posicion);

/// <summary>
/// Separa el texto de una fórmula en símbolos.
/// Convenciones (iguales a Excel en español): los decimales pueden escribirse con coma o punto
/// y los argumentos de una función se separan con punto y coma. Ejemplo: SI(LeyCu &gt; 1,5; 1; 0).
/// </summary>
public static class Lexer
{
    public static List<Token> Tokenizar(string texto)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < texto.Length)
        {
            char c = texto[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (char.IsDigit(c) || ((c == '.' || c == ',') && i + 1 < texto.Length && char.IsDigit(texto[i + 1])))
            {
                int inicio = i;
                bool decimalVisto = false;
                while (i < texto.Length && (char.IsDigit(texto[i]) || ((texto[i] == '.' || texto[i] == ',') && !decimalVisto)))
                {
                    if (texto[i] == '.' || texto[i] == ',')
                    {
                        // una coma o punto solo es decimal si le sigue un dígito
                        if (i + 1 >= texto.Length || !char.IsDigit(texto[i + 1])) break;
                        decimalVisto = true;
                    }
                    i++;
                }
                string literal = texto[inicio..i];
                double valor = double.Parse(literal.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
                tokens.Add(new Token(TipoToken.Numero, literal, valor, inicio));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int inicio = i;
                while (i < texto.Length && (char.IsLetterOrDigit(texto[i]) || texto[i] == '_')) i++;
                tokens.Add(new Token(TipoToken.Identificador, texto[inicio..i], 0, inicio));
                continue;
            }

            int pos = i;
            switch (c)
            {
                case '+': tokens.Add(new Token(TipoToken.Mas, "+", 0, pos)); i++; break;
                case '-': tokens.Add(new Token(TipoToken.Menos, "-", 0, pos)); i++; break;
                case '*': tokens.Add(new Token(TipoToken.Por, "*", 0, pos)); i++; break;
                case '/': tokens.Add(new Token(TipoToken.Division, "/", 0, pos)); i++; break;
                case '^': tokens.Add(new Token(TipoToken.Potencia, "^", 0, pos)); i++; break;
                case '(': tokens.Add(new Token(TipoToken.ParentesisIzq, "(", 0, pos)); i++; break;
                case ')': tokens.Add(new Token(TipoToken.ParentesisDer, ")", 0, pos)); i++; break;
                case ';': tokens.Add(new Token(TipoToken.Separador, ";", 0, pos)); i++; break;
                case '=': tokens.Add(new Token(TipoToken.Igual, "=", 0, pos)); i++; break;
                case '<':
                    if (i + 1 < texto.Length && texto[i + 1] == '=') { tokens.Add(new Token(TipoToken.MenorIgual, "<=", 0, pos)); i += 2; }
                    else if (i + 1 < texto.Length && texto[i + 1] == '>') { tokens.Add(new Token(TipoToken.Distinto, "<>", 0, pos)); i += 2; }
                    else { tokens.Add(new Token(TipoToken.Menor, "<", 0, pos)); i++; }
                    break;
                case '>':
                    if (i + 1 < texto.Length && texto[i + 1] == '=') { tokens.Add(new Token(TipoToken.MayorIgual, ">=", 0, pos)); i += 2; }
                    else { tokens.Add(new Token(TipoToken.Mayor, ">", 0, pos)); i++; }
                    break;
                default:
                    throw new FormulaException($"Carácter no permitido «{c}».", pos);
            }
        }
        tokens.Add(new Token(TipoToken.Fin, "", 0, texto.Length));
        return tokens;
    }
}
