using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Enami.Dominio.Exportacion;

/// <summary>Color RGB de 0 a 255.</summary>
public readonly record struct ColorPdf(byte R, byte G, byte B)
{
    /// <summary>Texto principal (#212529).</summary>
    public static readonly ColorPdf Texto = new(33, 37, 41);
    /// <summary>Texto secundario (#5F6B76, contraste 5,4:1 sobre blanco).</summary>
    public static readonly ColorPdf Secundario = new(95, 107, 118);
    public static readonly ColorPdf Linea = new(206, 212, 218);
    public static readonly ColorPdf LineaSuave = new(233, 236, 239);
    public static readonly ColorPdf Fondo = new(245, 247, 249);
    public static readonly ColorPdf Blanco = new(255, 255, 255);
    /// <summary>Turquesa de la aplicación (#00A396): solo para filetes y barras, no para texto (contraste 3,1:1).</summary>
    public static readonly ColorPdf Acento = new(0, 163, 150);
    /// <summary>Turquesa oscuro para títulos (#00796B, contraste 5,3:1).</summary>
    public static readonly ColorPdf AcentoTexto = new(0, 121, 107);

    internal string Relleno => $"{N(R)} {N(G)} {N(B)} rg";
    internal string Trazo => $"{N(R)} {N(G)} {N(B)} RG";
    private static string N(byte v) => (v / 255.0).ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>Tono de un estado (veredicto): color fuerte para el texto y la barra, y un tinte claro de fondo.</summary>
public readonly record struct TonoPdf(ColorPdf Fuerte, ColorPdf Tinte)
{
    public static readonly TonoPdf Favorable = new(new(20, 108, 67), new(233, 245, 238));     // #146C43 / #E9F5EE
    public static readonly TonoPdf Desfavorable = new(new(165, 29, 18), new(251, 234, 232));  // #A51D12 / #FBEAE8
    public static readonly TonoPdf Pendiente = new(new(122, 74, 0), new(253, 243, 225));      // #7A4A00 / #FDF3E1
}

/// <summary>Imagen para el membrete (logo). Acepta JPEG y PNG de 8 bits por canal, con o sin transparencia.</summary>
public sealed class ImagenPdf
{
    public int Ancho { get; }
    public int Alto { get; }
    internal string Diccionario { get; }
    internal byte[] Datos { get; }
    internal byte[]? Mascara { get; }

    private ImagenPdf(int ancho, int alto, string diccionario, byte[] datos, byte[]? mascara)
    {
        (Ancho, Alto, Diccionario, Datos, Mascara) = (ancho, alto, diccionario, datos, mascara);
    }

    /// <summary>Lee un JPEG o un PNG. Lanza <see cref="FormatException"/> si el formato no se puede usar.</summary>
    public static ImagenPdf Cargar(byte[] archivo)
    {
        if (archivo.Length > 4 && archivo[0] == 0xFF && archivo[1] == 0xD8) return Jpeg(archivo);
        if (archivo.Length > 8 && archivo[0] == 0x89 && archivo[1] == (byte)'P' && archivo[2] == (byte)'N' && archivo[3] == (byte)'G') return Png(archivo);
        throw new FormatException("El logo debe ser una imagen PNG o JPG.");
    }

    private static ImagenPdf Jpeg(byte[] a)
    {
        int i = 2;
        while (i + 9 < a.Length)
        {
            if (a[i] != 0xFF) { i++; continue; }
            byte marca = a[i + 1];
            int largo = (a[i + 2] << 8) | a[i + 3];
            if (marca is 0xC0 or 0xC1 or 0xC2)
            {
                int alto = (a[i + 5] << 8) | a[i + 6], ancho = (a[i + 7] << 8) | a[i + 8], comp = a[i + 9];
                string espacio = comp switch { 1 => "/DeviceGray", 4 => "/DeviceCMYK", _ => "/DeviceRGB" };
                return new ImagenPdf(ancho, alto, $"/ColorSpace {espacio} /BitsPerComponent 8 /Filter /DCTDecode", a, null);
            }
            i += 2 + largo;
        }
        throw new FormatException("No se pudo leer el tamaño del JPG.");
    }

    private static ImagenPdf Png(byte[] a)
    {
        int pos = 8, ancho = 0, alto = 0, bits = 0, tipo = 0, entrelazado = 0;
        byte[]? paleta = null, transparencia = null;
        using var idat = new MemoryStream();
        while (pos + 8 <= a.Length)
        {
            int largo = (a[pos] << 24) | (a[pos + 1] << 16) | (a[pos + 2] << 8) | a[pos + 3];
            string nombre = Encoding.ASCII.GetString(a, pos + 4, 4);
            int d = pos + 8;
            switch (nombre)
            {
                case "IHDR":
                    ancho = (a[d] << 24) | (a[d + 1] << 16) | (a[d + 2] << 8) | a[d + 3];
                    alto = (a[d + 4] << 24) | (a[d + 5] << 16) | (a[d + 6] << 8) | a[d + 7];
                    bits = a[d + 8]; tipo = a[d + 9]; entrelazado = a[d + 12];
                    break;
                case "PLTE": paleta = a[d..(d + largo)]; break;
                case "tRNS": transparencia = a[d..(d + largo)]; break;
                case "IDAT": idat.Write(a, d, largo); break;
            }
            if (nombre == "IEND") break;
            pos = d + largo + 4;
        }
        if (bits != 8 || entrelazado != 0 || tipo is not (0 or 2 or 3 or 4 or 6))
            throw new FormatException("El PNG del logo debe tener 8 bits por canal y no estar entrelazado; guárdelo de nuevo o use JPG.");

        int canales = tipo switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        byte[] crudo;
        using (var z = new ZLibStream(new MemoryStream(idat.ToArray()), CompressionMode.Decompress))
        using (var ms = new MemoryStream()) { z.CopyTo(ms); crudo = ms.ToArray(); }
        var pix = QuitarFiltros(crudo, ancho, alto, canales);

        var rgb = new byte[ancho * alto * 3];
        var alfa = new byte[ancho * alto];
        bool hayAlfa = false;
        for (int p = 0; p < ancho * alto; p++)
        {
            byte r, g, b, al = 255;
            switch (tipo)
            {
                case 0: r = g = b = pix[p]; break;
                case 2: r = pix[p * 3]; g = pix[p * 3 + 1]; b = pix[p * 3 + 2]; break;
                case 3:
                    int k = pix[p];
                    if (paleta is null || k * 3 + 2 >= paleta.Length) throw new FormatException("El PNG del logo tiene una paleta inválida.");
                    r = paleta[k * 3]; g = paleta[k * 3 + 1]; b = paleta[k * 3 + 2];
                    if (transparencia is not null && k < transparencia.Length) al = transparencia[k];
                    break;
                case 4: r = g = b = pix[p * 2]; al = pix[p * 2 + 1]; break;
                default: r = pix[p * 4]; g = pix[p * 4 + 1]; b = pix[p * 4 + 2]; al = pix[p * 4 + 3]; break;
            }
            rgb[p * 3] = r; rgb[p * 3 + 1] = g; rgb[p * 3 + 2] = b;
            alfa[p] = al;
            hayAlfa |= al != 255;
        }
        return new ImagenPdf(ancho, alto, "/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode",
            DocumentoPdf.Comprimir(rgb), hayAlfa ? DocumentoPdf.Comprimir(alfa) : null);
    }

    /// <summary>Deshace los filtros por fila del PNG (None, Sub, Up, Average, Paeth).</summary>
    private static byte[] QuitarFiltros(byte[] d, int ancho, int alto, int bpp)
    {
        int fila = ancho * bpp;
        var salida = new byte[fila * alto];
        for (int y = 0; y < alto; y++)
        {
            int f = d[y * (fila + 1)], o = y * (fila + 1) + 1, s = y * fila;
            for (int x = 0; x < fila; x++)
            {
                int izq = x >= bpp ? salida[s + x - bpp] : 0, arr = y > 0 ? salida[s - fila + x] : 0,
                    diag = x >= bpp && y > 0 ? salida[s - fila + x - bpp] : 0;
                int pred = f switch
                {
                    1 => izq,
                    2 => arr,
                    3 => (izq + arr) / 2,
                    4 => Paeth(izq, arr, diag),
                    _ => 0
                };
                salida[s + x] = (byte)(d[o + x] + pred);
            }
        }
        return salida;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}

/// <summary>
/// Generador de PDF propio (sin librerías externas, como <see cref="EscritorXlsx"/>): hoja A4, fuentes estándar Helvetica,
/// membrete repetido en cada página (logo opcional), texto con ajuste de línea, tablas que continúan en la página siguiente
/// repitiendo el encabezado, recuadros de estado y pie con el número de página.
/// </summary>
public sealed class DocumentoPdf
{
    public const float AnchoPagina = 595.28f, AltoPagina = 841.89f, Margen = 50f;
    private const float AltoPie = 44f;
    public float AnchoUtil => AnchoPagina - 2 * Margen;

    private readonly List<StringBuilder> _paginas = [];
    private StringBuilder _actual = null!;
    private readonly string _titulo, _autor, _asunto;
    private readonly ImagenPdf? _logo;
    private readonly Action<DocumentoPdf>? _membrete;
    private readonly Func<int, int, (string Izquierda, string Derecha, string Segunda)>? _pie;

    /// <summary>Posición vertical actual (desde abajo, en puntos).</summary>
    public float Y { get; private set; }
    public int Paginas => _paginas.Count;
    public ImagenPdf? Logo => _logo;

    /// <param name="membrete">Dibuja el membrete al comenzar cada página (y debe dejar <see cref="Y"/> bajo él).</param>
    /// <param name="pie">Textos del pie según la página y el total: izquierda, derecha y una segunda línea.</param>
    public DocumentoPdf(string titulo, string autor, string asunto, ImagenPdf? logo = null, Action<DocumentoPdf>? membrete = null,
                        Func<int, int, (string, string, string)>? pie = null)
    {
        (_titulo, _autor, _asunto, _logo, _membrete, _pie) = (titulo, autor, asunto, logo, membrete, pie);
        NuevaPagina();
    }

    public void NuevaPagina()
    {
        _actual = new StringBuilder();
        _paginas.Add(_actual);
        Y = AltoPagina - Margen;
        _membrete?.Invoke(this);
    }

    /// <summary>Pasa a otra página si no quedan <paramref name="alto"/> puntos libres.</summary>
    public void Reservar(float alto)
    {
        if (Y - alto < Margen + AltoPie) NuevaPagina();
    }

    public void Espacio(float puntos) => Y -= puntos;

    /// <summary>Párrafo con ajuste de línea.</summary>
    public void Texto(string texto, float tamano = 10, bool negrita = false, ColorPdf? color = null, float sangria = 0, float interlineado = 1.4f)
    {
        foreach (var linea in Partir(texto, AnchoUtil - sangria, tamano, negrita))
        {
            Reservar(tamano * interlineado);
            Y -= tamano * interlineado;
            EscribirTexto(Margen + sangria, Y + tamano * 0.3f, linea, tamano, negrita, color ?? ColorPdf.Texto);
        }
    }

    /// <summary>Título de sección numerado, con un filete fino debajo.</summary>
    public void Seccion(string titulo)
    {
        Reservar(130);   // el título no queda solo al pie: pasa a la página siguiente con su contenido
        Espacio(16);
        Texto(titulo.ToUpperInvariant(), 9.5f, true, ColorPdf.AcentoTexto);
        Espacio(4);
        Linea(Margen, Y, Margen + AnchoUtil, Y, ColorPdf.Linea, 0.6f);
        Espacio(8);
    }

    /// <summary>Recuadro de estado: tinte claro, barra lateral de color, título en el color fuerte y detalle en gris oscuro.</summary>
    public void Recuadro(string etiqueta, string titulo, IReadOnlyList<string> detalle, TonoPdf tono)
    {
        const float rel = 12, barra = 4;
        float anchoTexto = AnchoUtil - 2 * rel - barra;
        var lineas = detalle.SelectMany(d => Partir(d, anchoTexto, 9, false)).ToList();
        var lineasTitulo = Partir(titulo, anchoTexto, 14, true);
        float alto = rel * 2 + 12 + lineasTitulo.Count * 18 + 4 + lineas.Count * 12.5f;
        Reservar(alto);
        Rectangulo(Margen, Y - alto, AnchoUtil, alto, tono.Tinte);
        Rectangulo(Margen, Y - alto, barra, alto, tono.Fuerte);
        float x = Margen + barra + rel, y = Y - rel - 7;
        EscribirTexto(x, y, etiqueta.ToUpperInvariant(), 7.5f, true, tono.Fuerte);
        y -= 5;
        foreach (var l in lineasTitulo) { y -= 18; EscribirTexto(x, y + 4, l, 14, true, tono.Fuerte); }
        y -= 4;
        foreach (var l in lineas) { y -= 12.5f; EscribirTexto(x, y + 3, l, 9, false, ColorPdf.Texto); }
        Y -= alto;
    }

    /// <summary>Indicadores destacados en fila: etiqueta pequeña arriba y cifra grande abajo, separados por filetes verticales.</summary>
    public void Cifras(IReadOnlyList<(string Etiqueta, string Valor)> cifras)
    {
        if (cifras.Count == 0) return;
        float ancho = AnchoUtil / cifras.Count;
        var etiquetas = cifras.Select(c => Partir(c.Etiqueta, ancho - 14, 7.5f, false)).ToList();
        var valores = cifras.Select(c => Partir(c.Valor, ancho - 14, 13, true)).ToList();
        int nEtiq = etiquetas.Max(e => e.Count), nVal = valores.Max(v => v.Count);
        float alto = 10 + nEtiq * 10 + 4 + nVal * 16 + 8;
        Reservar(alto);
        Rectangulo(Margen, Y - alto, AnchoUtil, alto, ColorPdf.Fondo);
        for (int i = 0; i < cifras.Count; i++)
        {
            float x = Margen + i * ancho + 10, y = Y - 10;
            if (i > 0) Linea(Margen + i * ancho, Y - 8, Margen + i * ancho, Y - alto + 8, ColorPdf.Linea, 0.6f);
            foreach (var l in etiquetas[i]) { y -= 10; EscribirTexto(x, y + 2, l, 7.5f, false, ColorPdf.Secundario); }
            y = Y - 10 - nEtiq * 10 - 4;
            foreach (var l in valores[i]) { y -= 16; EscribirTexto(x, y + 3, l, 13, true, ColorPdf.Texto); }
        }
        Y -= alto;
    }

    /// <summary>Pares «etiqueta / valor» en columnas.</summary>
    public void Pares(IReadOnlyList<(string Etiqueta, string Valor)> pares, int columnas = 2)
    {
        const float separacion = 18;
        float anchoCol = (AnchoUtil - separacion * (columnas - 1)) / columnas, anchoEtiqueta = Math.Min(130, anchoCol * 0.4f);
        for (int i = 0; i < pares.Count; i += columnas)
        {
            var fila = pares.Skip(i).Take(columnas).ToList();
            var celdas = fila.Select(p => (Etiq: Partir(p.Etiqueta, anchoEtiqueta - 6, 8.5f, false),
                                           Val: Partir(p.Valor, anchoCol - anchoEtiqueta - 4, 9.5f, false))).ToList();
            int n = celdas.Max(c => Math.Max(c.Etiq.Count, c.Val.Count));
            float alto = n * 12.5f + 7;
            Reservar(alto);
            for (int c = 0; c < celdas.Count; c++)
            {
                float x = Margen + c * (anchoCol + separacion), y = Y - 11;
                foreach (var l in celdas[c].Etiq) { EscribirTexto(x, y, l, 8.5f, false, ColorPdf.Secundario); y -= 12.5f; }
                y = Y - 11;
                foreach (var l in celdas[c].Val) { EscribirTexto(x + anchoEtiqueta, y, l, 9.5f, false, ColorPdf.Texto); y -= 12.5f; }
                Linea(x, Y - alto, x + anchoCol, Y - alto, ColorPdf.LineaSuave, 0.5f);
            }
            Y -= alto;
        }
    }

    /// <summary>
    /// Tabla con encabezado. <paramref name="proporciones"/> reparte el ancho útil; <paramref name="derecha"/> alinea columnas numéricas.
    /// Una fila de una sola celda es un subtítulo de grupo que ocupa todo el ancho. Si la tabla no cabe, continúa en la página
    /// siguiente con el encabezado repetido.
    /// </summary>
    public void Tabla(IReadOnlyList<string> encabezado, IReadOnlyList<float> proporciones, IEnumerable<IReadOnlyList<string>> filas,
                      IReadOnlyList<bool>? derecha = null, float tamano = 8.5f)
    {
        float suma = proporciones.Sum();
        var anchos = proporciones.Select(p => p / suma * AnchoUtil).ToArray();
        const float rel = 5f;
        float lineaAlto = tamano * 1.3f;

        void Encabezado()
        {
            var celdas = encabezado.Select((t, i) => Partir(t.ToUpperInvariant(), anchos[i] - 2 * rel, 7, true)).ToList();
            float alto = celdas.Max(c => c.Count) * 9 + 2 * rel;
            Reservar(alto + lineaAlto + 2 * rel);
            Linea(Margen, Y, Margen + AnchoUtil, Y, ColorPdf.Texto, 0.8f);
            float x = Margen;
            for (int i = 0; i < celdas.Count; i++)
            {
                bool alDerecha = derecha is not null && i < derecha.Count && derecha[i];
                float y = Y - rel - 6;
                foreach (var l in celdas[i])
                {
                    float xx = alDerecha ? x + anchos[i] - rel - AnchoTexto(l, 7, true) : x + rel;
                    EscribirTexto(xx, y, l, 7, true, ColorPdf.Secundario);
                    y -= 9;
                }
                x += anchos[i];
            }
            Y -= alto;
            Linea(Margen, Y, Margen + AnchoUtil, Y, ColorPdf.Linea, 0.6f);
        }

        Reservar(90);   // el encabezado nunca queda solo al pie de una página: va con al menos dos filas
        Encabezado();
        foreach (var fila in filas)
        {
            if (fila.Count == 1)
            {
                float altoGrupo = lineaAlto + 2 * rel + 2;
                if (Y - altoGrupo - lineaAlto - 2 * rel < Margen + AltoPie) { NuevaPagina(); Encabezado(); }
                Rectangulo(Margen, Y - altoGrupo, AnchoUtil, altoGrupo, ColorPdf.Fondo);
                EscribirTexto(Margen + rel, Y - rel - tamano, fila[0], tamano, true, ColorPdf.Texto);
                Y -= altoGrupo;
                continue;
            }
            var celdas = Enumerable.Range(0, anchos.Length)
                .Select(i => Partir(i < fila.Count ? fila[i] : "", anchos[i] - 2 * rel, tamano, false)).ToList();
            float alto = celdas.Max(c => Math.Max(1, c.Count)) * lineaAlto + 2 * rel - 1;
            if (Y - alto < Margen + AltoPie) { NuevaPagina(); Encabezado(); }
            float x = Margen;
            for (int i = 0; i < celdas.Count; i++)
            {
                bool alDerecha = derecha is not null && i < derecha.Count && derecha[i];
                float y = Y - rel - tamano + 1;
                foreach (var l in celdas[i])
                {
                    float xx = alDerecha ? x + anchos[i] - rel - AnchoTexto(l, tamano, false) : x + rel;
                    EscribirTexto(xx, y, l, tamano, false, ColorPdf.Texto);
                    y -= lineaAlto;
                }
                x += anchos[i];
            }
            Y -= alto;
            Linea(Margen, Y, Margen + AnchoUtil, Y, ColorPdf.LineaSuave, 0.5f);
        }
    }

    public void Linea(float x1, float y1, float x2, float y2, ColorPdf color, float grosor = 0.5f) =>
        _actual.Append(CultureInfo.InvariantCulture, $"q {color.Trazo} {grosor:0.##} w {x1:0.##} {y1:0.##} m {x2:0.##} {y2:0.##} l S Q\n");

    public void Rectangulo(float x, float y, float ancho, float alto, ColorPdf color) =>
        _actual.Append(CultureInfo.InvariantCulture, $"q {color.Relleno} {x:0.##} {y:0.##} {ancho:0.##} {alto:0.##} re f Q\n");

    public void EscribirTexto(float x, float y, string texto, float tamano, bool negrita, ColorPdf color) =>
        _actual.Append(CultureInfo.InvariantCulture,
            $"BT {color.Relleno} /{(negrita ? "F2" : "F1")} {tamano:0.##} Tf {x:0.##} {y:0.##} Td ({Cadena(texto)}) Tj ET\n");

    /// <summary>Texto alineado a la derecha en <paramref name="xDerecha"/>.</summary>
    public void EscribirTextoDerecha(float xDerecha, float y, string texto, float tamano, bool negrita, ColorPdf color) =>
        EscribirTexto(xDerecha - AnchoTexto(texto, tamano, negrita), y, texto, tamano, negrita, color);

    /// <summary>Dibuja el logo con su esquina inferior izquierda en (x, y) y el alto indicado (el ancho mantiene la proporción).</summary>
    public float DibujarLogo(float x, float y, float alto, float anchoMaximo)
    {
        if (_logo is null) return 0;
        float ancho = alto * _logo.Ancho / _logo.Alto;
        if (ancho > anchoMaximo) { alto *= anchoMaximo / ancho; ancho = anchoMaximo; }
        _actual.Append(CultureInfo.InvariantCulture, $"q {ancho:0.##} 0 0 {alto:0.##} {x:0.##} {y:0.##} cm /Logo Do Q\n");
        return ancho;
    }

    /// <summary>Genera el archivo PDF (versión 1.4).</summary>
    public byte[] Generar()
    {
        int n = _paginas.Count;
        var objetos = new List<byte[]>();   // el objeto k está en objetos[k - 1]
        static byte[] Ascii(string s) => Encoding.Latin1.GetBytes(s);
        byte[] Flujo(string diccionario, byte[] datos)
        {
            using var ms = new MemoryStream();
            ms.Write(Ascii($"<< {diccionario} /Length {datos.Length} >>\nstream\n"));
            ms.Write(datos);
            ms.Write(Ascii("\nendstream"));
            return ms.ToArray();
        }

        // 1 catálogo, 2 páginas, 3 y 4 fuentes, 5 información, 6 logo, 7 máscara del logo; luego, por página: hoja y contenido
        const int primeraPagina = 8;
        objetos.Add(Ascii("<< /Type /Catalog /Pages 2 0 R /ViewerPreferences << /DisplayDocTitle true >> >>"));
        var hijos = string.Join(" ", Enumerable.Range(0, n).Select(i => $"{primeraPagina + 2 * i} 0 R"));
        objetos.Add(Ascii($"<< /Type /Pages /Kids [{hijos}] /Count {n} >>"));
        objetos.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        objetos.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"));
        var ahora = DateTimeOffset.Now;
        string fecha = $"D:{ahora:yyyyMMddHHmmss}{(ahora.Offset < TimeSpan.Zero ? "-" : "+")}{ahora.Offset:hh}'{ahora.Offset:mm}'";
        objetos.Add(Ascii($"<< /Title ({Cadena(_titulo)}) /Author ({Cadena(_autor)}) /Subject ({Cadena(_asunto)}) " +
                          $"/Creator ({Cadena("Sistema de Evaluación de Proyectos de Fomento")}) /Producer (EscritorPdf) /CreationDate ({fecha}) >>"));
        if (_logo is not null)
        {
            string mascara = _logo.Mascara is null ? "" : " /SMask 7 0 R";
            objetos.Add(Flujo($"/Type /XObject /Subtype /Image /Width {_logo.Ancho} /Height {_logo.Alto} {_logo.Diccionario}{mascara}", _logo.Datos));
            objetos.Add(_logo.Mascara is null
                ? Ascii("null")
                : Flujo($"/Type /XObject /Subtype /Image /Width {_logo.Ancho} /Height {_logo.Alto} /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode", _logo.Mascara));
        }
        else
        {
            objetos.Add(Ascii("null"));
            objetos.Add(Ascii("null"));
        }
        string recursos = "/Font << /F1 3 0 R /F2 4 0 R >>" + (_logo is null ? "" : " /XObject << /Logo 6 0 R >>");

        for (int i = 0; i < n; i++)
        {
            var contenido = new StringBuilder(_paginas[i].ToString());
            if (_pie is not null)
            {
                var (izq, der, segunda) = _pie(i + 1, n);
                float y = Margen - 6;
                contenido.Append(CultureInfo.InvariantCulture, $"q {ColorPdf.Linea.Trazo} 0.5 w {Margen:0.##} {y + 14:0.##} m {AnchoPagina - Margen:0.##} {y + 14:0.##} l S Q\n");
                contenido.Append(CultureInfo.InvariantCulture, $"BT {ColorPdf.Secundario.Relleno} /F1 7 Tf {Margen:0.##} {y:0.##} Td ({Cadena(izq)}) Tj ET\n");
                contenido.Append(CultureInfo.InvariantCulture,
                    $"BT {ColorPdf.Secundario.Relleno} /F2 7 Tf {AnchoPagina - Margen - AnchoTexto(der, 7, true):0.##} {y:0.##} Td ({Cadena(der)}) Tj ET\n");
                if (segunda.Length > 0)
                    contenido.Append(CultureInfo.InvariantCulture, $"BT {ColorPdf.Secundario.Relleno} /F1 6.5 Tf {Margen:0.##} {y - 10:0.##} Td ({Cadena(segunda)}) Tj ET\n");
            }
            objetos.Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {AnchoPagina.ToString(CultureInfo.InvariantCulture)} {AltoPagina.ToString(CultureInfo.InvariantCulture)}] " +
                              $"/Resources << {recursos} >> /Contents {primeraPagina + 1 + 2 * i} 0 R >>"));
            objetos.Add(Flujo("/Filter /FlateDecode", Comprimir(Encoding.Latin1.GetBytes(contenido.ToString()))));
        }

        using var salida = new MemoryStream();
        salida.Write(Ascii("%PDF-1.4\n%âãÏÓ\n"));
        var posiciones = new long[objetos.Count];
        for (int i = 0; i < objetos.Count; i++)
        {
            posiciones[i] = salida.Position;
            salida.Write(Ascii($"{i + 1} 0 obj\n"));
            salida.Write(objetos[i]);
            salida.Write(Ascii("\nendobj\n"));
        }
        long xref = salida.Position;
        var tabla = new StringBuilder($"xref\n0 {objetos.Count + 1}\n0000000000 65535 f \n");
        foreach (var p in posiciones) tabla.Append(CultureInfo.InvariantCulture, $"{p:D10} 00000 n \n");
        tabla.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objetos.Count + 1} /Root 1 0 R /Info 5 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        salida.Write(Ascii(tabla.ToString()));
        return salida.ToArray();
    }

    internal static byte[] Comprimir(byte[] datos)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(datos);
        return ms.ToArray();
    }

    // ---------- texto ----------

    /// <summary>Cadena PDF en WinAnsi: escapa paréntesis y barra inversa; los caracteres fuera de WinAnsi se aproximan.</summary>
    public static string Cadena(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto)
        {
            int b = WinAnsi(c);
            if (b == '(' || b == ')' || b == '\\') sb.Append('\\').Append((char)b);
            else if (b < 32 || b > 126) sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            else sb.Append((char)b);
        }
        return sb.ToString();
    }

    private static int WinAnsi(char c) => c switch
    {
        '—' => 0x97, '–' => 0x96, '…' => 0x85, '•' => 0x95, '€' => 0x80,
        '‘' => 0x91, '’' => 0x92, '“' => 0x93, '”' => 0x94,
        '→' => '>', ' ' => ' ', '\t' => ' ',
        _ when c >= 32 && c <= 126 => c,
        _ when c >= 0xA0 && c <= 0xFF => c,
        _ => '?'
    };

    /// <summary>Ancho del texto en puntos, con las métricas de Helvetica y Helvetica-Bold.</summary>
    public static float AnchoTexto(string texto, float tamano, bool negrita)
    {
        var tabla = negrita ? AnchosNegrita : AnchosNormal;
        int total = 0;
        foreach (var c0 in texto)
        {
            char c = Base(c0);
            total += c >= 32 && c <= 126 ? tabla[c - 32] : c0 switch
            {
                '—' or '…' => 1000, '–' => 556, '«' or '»' => 556, '·' or '•' => 350,
                'º' or 'ª' => 370, '°' => 400, _ => 556
            };
        }
        return total * tamano / 1000f;
    }

    /// <summary>Letra base de una letra con tilde (á → a): tienen el mismo ancho.</summary>
    private static char Base(char c)
    {
        if (c < 0xC0) return c;
        var d = c.ToString().Normalize(NormalizationForm.FormD);
        return d.Length > 0 && d[0] < 128 ? d[0] : c;
    }

    /// <summary>Divide el texto en líneas que caben en <paramref name="ancho"/> (respeta los saltos de línea).</summary>
    public static List<string> Partir(string texto, float ancho, float tamano, bool negrita)
    {
        var lineas = new List<string>();
        foreach (var parrafo in (texto ?? "").Replace("\r", "").Split('\n'))
        {
            var actual = "";
            foreach (var palabra0 in parrafo.Split(' '))
            {
                var palabra = palabra0;
                var prueba = actual.Length == 0 ? palabra : actual + " " + palabra;
                if (AnchoTexto(prueba, tamano, negrita) <= ancho) { actual = prueba; continue; }
                if (actual.Length > 0) { lineas.Add(actual); actual = ""; }
                // palabra más larga que la línea: se corta por caracteres
                while (AnchoTexto(palabra, tamano, negrita) > ancho && palabra.Length > 1)
                {
                    int k = palabra.Length - 1;
                    while (k > 1 && AnchoTexto(palabra[..k], tamano, negrita) > ancho) k--;
                    lineas.Add(palabra[..k]);
                    palabra = palabra[k..];
                }
                actual = palabra;
            }
            lineas.Add(actual);
        }
        return lineas;
    }

    // Anchos (milésimas del cuerpo) de los caracteres 32 a 126, de las métricas AFM estándar de Adobe.
    private static readonly short[] AnchosNormal =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584
    ];

    private static readonly short[] AnchosNegrita =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584
    ];
}
