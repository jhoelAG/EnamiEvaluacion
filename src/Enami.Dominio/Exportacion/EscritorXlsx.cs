using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Enami.Dominio.Exportacion;

/// <summary>
/// Escribe una planilla Excel (.xlsx) de una hoja, sin librerías externas (RF-29). Es la contraparte de LectorXlsx:
/// textos como textos compartidos, montos con separador de miles y fechas como fechas de Excel, para que se puedan ordenar y filtrar.
/// La primera fila (encabezados) queda en negrita, fija al desplazarse y con filtros.
/// </summary>
public static class EscritorXlsx
{
    private const string NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string NsPkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    // estilos (cellXfs): 0 normal, 1 encabezado en negrita, 2 número con miles, 3 fecha y hora
    private const int EstiloEncabezado = 1, EstiloNumero = 2, EstiloFecha = 3;

    public static byte[] Escribir(string nombreHoja, IReadOnlyList<string> encabezados, IEnumerable<IReadOnlyList<object?>> filas, IReadOnlyList<double>? anchos = null)
    {
        var compartidos = new List<string>();
        var indice = new Dictionary<string, int>();
        int Compartido(string texto)
        {
            if (!indice.TryGetValue(texto, out int i)) { i = compartidos.Count; compartidos.Add(texto); indice[texto] = i; }
            return i;
        }

        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            int nFilas = 1;
            Parte(zip, "xl/worksheets/sheet1.xml", x =>
            {
                x.WriteStartElement("worksheet", NsMain);
                x.WriteStartElement("sheetViews"); x.WriteStartElement("sheetView"); x.WriteAttributeString("workbookViewId", "0");
                x.WriteStartElement("pane"); x.WriteAttributeString("ySplit", "1"); x.WriteAttributeString("topLeftCell", "A2");
                x.WriteAttributeString("activePane", "bottomLeft"); x.WriteAttributeString("state", "frozen");
                x.WriteEndElement(); x.WriteEndElement(); x.WriteEndElement();
                if (anchos is { Count: > 0 })
                {
                    x.WriteStartElement("cols");
                    for (int i = 0; i < anchos.Count; i++)
                    {
                        x.WriteStartElement("col");
                        x.WriteAttributeString("min", (i + 1).ToString(CultureInfo.InvariantCulture));
                        x.WriteAttributeString("max", (i + 1).ToString(CultureInfo.InvariantCulture));
                        x.WriteAttributeString("width", anchos[i].ToString(CultureInfo.InvariantCulture));
                        x.WriteAttributeString("customWidth", "1");
                        x.WriteEndElement();
                    }
                    x.WriteEndElement();
                }
                x.WriteStartElement("sheetData");
                EscribirFila(x, 1, encabezados.Cast<object?>().ToList(), Compartido, encabezado: true);
                foreach (var fila in filas) EscribirFila(x, ++nFilas, fila, Compartido, encabezado: false);
                x.WriteEndElement();
                x.WriteStartElement("autoFilter");
                x.WriteAttributeString("ref", $"A1:{Columna(Math.Max(encabezados.Count, 1) - 1)}{nFilas}");
                x.WriteEndElement();
                x.WriteEndElement();
            });
            Parte(zip, "xl/sharedStrings.xml", x =>
            {
                x.WriteStartElement("sst", NsMain);
                x.WriteAttributeString("count", compartidos.Count.ToString(CultureInfo.InvariantCulture));
                x.WriteAttributeString("uniqueCount", compartidos.Count.ToString(CultureInfo.InvariantCulture));
                foreach (var t in compartidos)
                {
                    x.WriteStartElement("si"); x.WriteStartElement("t");
                    x.WriteAttributeString("xml", "space", null, "preserve");
                    x.WriteString(t);
                    x.WriteEndElement(); x.WriteEndElement();
                }
                x.WriteEndElement();
            });
            Parte(zip, "xl/workbook.xml", x =>
            {
                x.WriteStartElement("workbook", NsMain);
                x.WriteAttributeString("xmlns", "r", null, NsRel);
                x.WriteStartElement("sheets"); x.WriteStartElement("sheet");
                x.WriteAttributeString("name", nombreHoja.Length > 31 ? nombreHoja[..31] : nombreHoja);
                x.WriteAttributeString("sheetId", "1");
                x.WriteAttributeString("id", NsRel, "rId1");
                x.WriteEndElement(); x.WriteEndElement();
                x.WriteEndElement();
            });
            Parte(zip, "xl/styles.xml", x =>
            {
                x.WriteStartElement("styleSheet", NsMain);
                x.WriteStartElement("numFmts"); x.WriteAttributeString("count", "1");
                x.WriteStartElement("numFmt"); x.WriteAttributeString("numFmtId", "164"); x.WriteAttributeString("formatCode", "dd-mm-yyyy hh:mm");
                x.WriteEndElement(); x.WriteEndElement();
                x.WriteStartElement("fonts"); x.WriteAttributeString("count", "2");
                x.WriteStartElement("font"); x.WriteEndElement();
                x.WriteStartElement("font"); x.WriteStartElement("b"); x.WriteEndElement(); x.WriteEndElement();
                x.WriteEndElement();
                x.WriteStartElement("fills"); x.WriteAttributeString("count", "2");
                foreach (var patron in new[] { "none", "gray125" })
                {
                    x.WriteStartElement("fill"); x.WriteStartElement("patternFill"); x.WriteAttributeString("patternType", patron);
                    x.WriteEndElement(); x.WriteEndElement();
                }
                x.WriteEndElement();
                x.WriteStartElement("borders"); x.WriteAttributeString("count", "1"); x.WriteStartElement("border"); x.WriteEndElement(); x.WriteEndElement();
                x.WriteStartElement("cellStyleXfs"); x.WriteAttributeString("count", "1"); Xf(x, 0, 0, aplicar: false); x.WriteEndElement();
                x.WriteStartElement("cellXfs"); x.WriteAttributeString("count", "4");
                Xf(x, 0, 0); Xf(x, 0, 1); Xf(x, 3, 0); Xf(x, 164, 0);
                x.WriteEndElement();
                x.WriteEndElement();
            });
            Parte(zip, "xl/_rels/workbook.xml.rels", x =>
            {
                x.WriteStartElement("Relationships", NsPkgRel);
                Relacion(x, "rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", "worksheets/sheet1.xml");
                Relacion(x, "rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml");
                Relacion(x, "rId3", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings", "sharedStrings.xml");
                x.WriteEndElement();
            });
            Parte(zip, "_rels/.rels", x =>
            {
                x.WriteStartElement("Relationships", NsPkgRel);
                Relacion(x, "rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml");
                x.WriteEndElement();
            });
            Parte(zip, "[Content_Types].xml", x =>
            {
                x.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
                Tipo(x, "Default", "Extension", "rels", "application/vnd.openxmlformats-package.relationships+xml");
                Tipo(x, "Default", "Extension", "xml", "application/xml");
                Tipo(x, "Override", "PartName", "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
                Tipo(x, "Override", "PartName", "/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                Tipo(x, "Override", "PartName", "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
                Tipo(x, "Override", "PartName", "/xl/sharedStrings.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml");
                x.WriteEndElement();
            });
        }
        return memoria.ToArray();
    }

    private static void EscribirFila(XmlWriter x, int nro, IReadOnlyList<object?> valores, Func<string, int> compartido, bool encabezado)
    {
        x.WriteStartElement("row");
        x.WriteAttributeString("r", nro.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < valores.Count; i++)
        {
            var v = valores[i];
            if (v is null || v is string { Length: 0 }) continue;
            x.WriteStartElement("c");
            x.WriteAttributeString("r", Columna(i) + nro.ToString(CultureInfo.InvariantCulture));
            string? contenido;
            switch (v)
            {
                case DateTime f:
                    x.WriteAttributeString("s", EstiloFecha.ToString(CultureInfo.InvariantCulture));
                    contenido = f.ToOADate().ToString(CultureInfo.InvariantCulture);
                    break;
                case decimal or double or float or int or long or short or byte:
                    x.WriteAttributeString("s", EstiloNumero.ToString(CultureInfo.InvariantCulture));
                    contenido = Convert.ToDecimal(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                    break;
                default:
                    x.WriteAttributeString("t", "s");
                    if (encabezado) x.WriteAttributeString("s", EstiloEncabezado.ToString(CultureInfo.InvariantCulture));
                    contenido = compartido(Convert.ToString(v, CultureInfo.InvariantCulture) ?? "").ToString(CultureInfo.InvariantCulture);
                    break;
            }
            x.WriteElementString("v", NsMain, contenido);
            x.WriteEndElement();
        }
        x.WriteEndElement();
    }

    /// <summary>Letra de la columna: 0 → A, 25 → Z, 26 → AA.</summary>
    public static string Columna(int i)
    {
        var sb = new StringBuilder();
        for (i++; i > 0; i = (i - 1) / 26) sb.Insert(0, (char)('A' + (i - 1) % 26));
        return sb.ToString();
    }

    private static void Parte(ZipArchive zip, string ruta, Action<XmlWriter> escribir)
    {
        using var flujo = zip.CreateEntry(ruta, CompressionLevel.Optimal).Open();
        using var x = XmlWriter.Create(flujo, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });
        x.WriteStartDocument(true);
        escribir(x);
        x.WriteEndDocument();
    }

    private static void Xf(XmlWriter x, int numFmt, int font, bool aplicar = true)
    {
        x.WriteStartElement("xf");
        x.WriteAttributeString("numFmtId", numFmt.ToString(CultureInfo.InvariantCulture));
        x.WriteAttributeString("fontId", font.ToString(CultureInfo.InvariantCulture));
        x.WriteAttributeString("fillId", "0");
        x.WriteAttributeString("borderId", "0");
        if (aplicar)
        {
            x.WriteAttributeString("xfId", "0");
            if (numFmt != 0) x.WriteAttributeString("applyNumberFormat", "1");
            if (font != 0) x.WriteAttributeString("applyFont", "1");
        }
        x.WriteEndElement();
    }

    private static void Relacion(XmlWriter x, string id, string tipo, string destino)
    {
        x.WriteStartElement("Relationship");
        x.WriteAttributeString("Id", id); x.WriteAttributeString("Type", tipo); x.WriteAttributeString("Target", destino);
        x.WriteEndElement();
    }

    private static void Tipo(XmlWriter x, string elemento, string atributo, string valor, string contentType)
    {
        x.WriteStartElement(elemento);
        x.WriteAttributeString(atributo, valor); x.WriteAttributeString("ContentType", contentType);
        x.WriteEndElement();
    }
}
