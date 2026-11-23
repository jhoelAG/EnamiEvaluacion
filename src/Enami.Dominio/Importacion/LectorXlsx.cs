using System.IO.Compression;
using System.Xml;

namespace Enami.Dominio.Importacion;

/// <summary>
/// Lector mínimo de archivos .xlsx que no necesita paquetes externos (usa ZipArchive y XmlReader).
/// Lee la primera hoja y entrega cada fila como un diccionario «encabezado → texto».
/// Lee fila por fila, así que soporta planillas grandes (por ejemplo, los 52.690 registros de padrones).
/// </summary>
public static class LectorXlsx
{
    private const string NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static IEnumerable<(int Fila, Dictionary<string, string> Valores)> LeerFilas(string ruta)
    {
        using var zip = ZipFile.OpenRead(ruta);
        var compartidos = LeerTextosCompartidos(zip);
        string rutaHoja = RutaPrimeraHoja(zip);
        var entrada = zip.GetEntry(rutaHoja) ?? throw new InvalidDataException("La planilla no tiene hojas.");

        using var flujo = entrada.Open();
        using var xr = XmlReader.Create(flujo, new XmlReaderSettings { IgnoreWhitespace = true });
        List<string>? encabezados = null;
        while (xr.ReadToFollowing("row", NsMain))
        {
            int nroFila = int.TryParse(xr.GetAttribute("r"), out var n) ? n : 0;
            var celdas = LeerFila(xr.ReadSubtree(), compartidos);
            if (encabezados is null)
            {
                int max = celdas.Count == 0 ? 0 : celdas.Keys.Max() + 1;
                encabezados = Enumerable.Range(0, max).Select(i => celdas.TryGetValue(i, out var h) ? h.Trim() : $"Columna{i + 1}").ToList();
                continue;
            }
            if (celdas.Count == 0) continue;
            var dic = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < encabezados.Count; i++)
                dic.TryAdd(encabezados[i], celdas.TryGetValue(i, out var v) ? v : "");
            yield return (nroFila, dic);
        }
    }

    private static Dictionary<int, string> LeerFila(XmlReader fila, List<string> compartidos)
    {
        var celdas = new Dictionary<int, string>();
        while (fila.ReadToFollowing("c", NsMain))
        {
            string referencia = fila.GetAttribute("r") ?? "";
            string? tipo = fila.GetAttribute("t");
            int col = IndiceColumna(referencia);
            string valor = "";
            using (var c = fila.ReadSubtree())
            {
                while (c.Read())
                {
                    if (c.NodeType != XmlNodeType.Element) continue;
                    if (c.LocalName == "v")
                    {
                        string crudo = c.ReadElementContentAsString();
                        valor = tipo == "s" && int.TryParse(crudo, out var idx) && idx < compartidos.Count ? compartidos[idx] : crudo;
                    }
                    else if (c.LocalName == "t")   // texto en línea
                    {
                        valor += c.ReadElementContentAsString();
                    }
                }
            }
            if (col >= 0) celdas[col] = valor;
        }
        return celdas;
    }

    private static List<string> LeerTextosCompartidos(ZipArchive zip)
    {
        var lista = new List<string>();
        var entrada = zip.GetEntry("xl/sharedStrings.xml");
        if (entrada is null) return lista;
        using var flujo = entrada.Open();
        using var xr = XmlReader.Create(flujo);
        while (xr.ReadToFollowing("si", NsMain))
        {
            using var si = xr.ReadSubtree();
            var texto = new System.Text.StringBuilder();
            while (si.Read())
                if (si.NodeType == XmlNodeType.Element && si.LocalName == "t")
                    texto.Append(si.ReadElementContentAsString());
            lista.Add(texto.ToString());
        }
        return lista;
    }

    private static string RutaPrimeraHoja(ZipArchive zip)
    {
        var libro = zip.GetEntry("xl/workbook.xml") ?? throw new InvalidDataException("El archivo no es una planilla .xlsx válida.");
        string? rid = null;
        using (var xr = XmlReader.Create(libro.Open()))
            if (xr.ReadToFollowing("sheet", NsMain)) rid = xr.GetAttribute("id", NsRel);
        var rels = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (rid is not null && rels is not null)
        {
            using var xr = XmlReader.Create(rels.Open());
            while (xr.Read())
            {
                if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "Relationship" && xr.GetAttribute("Id") == rid)
                {
                    string destino = xr.GetAttribute("Target") ?? "";
                    return destino.StartsWith('/') ? destino.TrimStart('/') : "xl/" + destino;
                }
            }
        }
        return "xl/worksheets/sheet1.xml";
    }

    private static int IndiceColumna(string referencia)
    {
        int col = 0, i = 0;
        while (i < referencia.Length && char.IsLetter(referencia[i]))
        {
            col = col * 26 + (char.ToUpperInvariant(referencia[i]) - 'A' + 1);
            i++;
        }
        return col - 1;
    }
}
