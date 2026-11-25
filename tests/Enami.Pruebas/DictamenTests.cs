using System.IO.Compression;
using System.Text;
using Enami.Dominio.Exportacion;
using Enami.Dominio.Modelo;
using Enami.Dominio.Servicios;

namespace Enami.Pruebas;

/// <summary>Dictamen en PDF: contenido, alcance por rol, auditoría y validez del archivo.</summary>
public class DictamenTests
{
    private static (ServicioAprobaciones A, ServicioDictamen D) Servicios(Entorno e)
    {
        var a = new ServicioAprobaciones(e.Db, e.Solicitudes, e.Auditoria);
        return (a, new ServicioDictamen(e.Db, e.Solicitudes, a, e.Plantillas, e.Auditoria));
    }

    /// <summary>Texto de todas las páginas (los flujos de contenido van comprimidos con FlateDecode).</summary>
    public static string Contenido(byte[] pdf)
    {
        var texto = Encoding.Latin1.GetString(pdf);
        var sb = new StringBuilder();
        int i = 0;
        while ((i = texto.IndexOf("stream\n", i, StringComparison.Ordinal)) >= 0)
        {
            int inicio = i + 7, fin = texto.IndexOf("\nendstream", inicio, StringComparison.Ordinal);
            using var z = new ZLibStream(new MemoryStream(pdf, inicio, fin - inicio), CompressionMode.Decompress);
            using var r = new StreamReader(z, Encoding.Latin1);
            sb.Append(r.ReadToEnd());
            i = fin + "\nendstream".Length;
        }
        return sb.ToString();
    }

    [Fact]
    public void El_dictamen_de_una_evaluacion_aprobada_muestra_el_veredicto_la_cadena_y_queda_en_la_auditoria()
    {
        var e = new Entorno();
        var (a, d) = Servicios(e);
        int idSub = e.Db.Tabla<Usuario>().Single(u => u.Correo == "subgerente@enami.cl").Id;
        long id = e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible), e.Sesion(e.IdIngeniero)).IdSolicitud;
        a.Enviar(id, e.Sesion(e.IdIngeniero));
        a.Decidir(id, e.Sesion(e.IdJefeCopiapo), ServicioAprobaciones.Aprobar, "Conforme (revisado).");
        a.Decidir(id, e.Sesion(idSub), ServicioAprobaciones.Aprobar, "Proyecto viable.");

        var (archivo, pdf, codigo) = d.Generar(id, e.Sesion(e.IdIngeniero))!.Value;

        var nro = e.Db.Tabla<SolicitudCredito>().Single(s => s.Id == id).NroSolicitud;
        Assert.Equal($"dictamen-{nro}.pdf", archivo);
        var texto = Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-1.4", texto);
        Assert.EndsWith("%%EOF\n", texto);
        var contenido = Contenido(pdf);
        Assert.Contains(DocumentoPdf.Cadena("Juan Pérez Soto"), contenido);
        Assert.Contains(DocumentoPdf.Cadena("Conforme (revisado)."), contenido);   // paréntesis escapados
        Assert.Contains("Subgerente", contenido);
        Assert.Contains(DocumentoPdf.Cadena("Evaluación aprobada"), contenido);
        Assert.Contains(codigo, contenido);
        Assert.Matches("^[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$", codigo);
        Assert.Contains(e.Db.Tabla<Auditoria>(), x => x.Accion == "DICTAMEN_PDF" && x.IdSolicitud == id && x.IdUsuario == e.IdIngeniero
                                                       && x.Detalle!.Contains(codigo));
        // el código depende del contenido: emitirlo de nuevo da el mismo código
        Assert.Equal(codigo, d.Generar(id, e.Sesion(idSub))!.Value.Codigo);
    }

    [Fact]
    public void El_rechazo_incluye_la_causal_y_un_borrador_se_marca_como_preliminar()
    {
        var e = new Entorno();
        var (a, d) = Servicios(e);
        var causal = e.Db.Tabla<CausalRechazo>().OrderBy(c => c.Id).First();
        long rechazada = e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible), e.Sesion(e.IdIngeniero)).IdSolicitud;
        a.Enviar(rechazada, e.Sesion(e.IdIngeniero));
        a.Decidir(rechazada, e.Sesion(e.IdJefeCopiapo), ServicioAprobaciones.Rechazar, "Leyes bajo el mínimo.", causal.Id);
        long borrador = e.Solicitudes.GuardarBorrador(null, e.DatosCompletos(e.IdFaenaElegible, 30_000), e.Sesion(e.IdIngeniero)).IdSolicitud;

        var c1 = Contenido(d.Generar(rechazada, e.Sesion(e.IdIngeniero))!.Value.Contenido);
        Assert.Contains(DocumentoPdf.Cadena("Evaluación rechazada"), c1);
        Assert.Contains(DocumentoPdf.Cadena($"{causal.Codigo} · {causal.Descripcion}"), c1);

        var c2 = Contenido(d.Generar(borrador, e.Sesion(e.IdIngeniero))!.Value.Contenido);
        Assert.Contains("DOCUMENTO PRELIMINAR", c2);
        Assert.Contains(DocumentoPdf.Cadena("no constituye una resolución"), c2);
    }

    [Fact]
    public void Sin_calculo_guardado_no_se_emite_y_una_evaluacion_inexistente_no_entrega_archivo()
    {
        var e = new Entorno();
        var (_, d) = Servicios(e);
        var datos = e.DatosCompletos(e.IdFaenaElegible);
        datos.DatosProyecto.Clear();   // sin datos del proyecto no se calcula el VAN
        long id = e.Solicitudes.GuardarBorrador(null, datos, e.Sesion(e.IdIngeniero)).IdSolicitud;

        Assert.Throws<ErrorNegocio>(() => d.Generar(id, e.Sesion(e.IdIngeniero)));
        Assert.Null(d.Generar(999_999, e.Sesion(e.IdIngeniero)));
    }

    [Fact]
    public void Una_tabla_larga_continua_en_otra_pagina_y_las_lineas_se_ajustan_al_ancho()
    {
        var doc = new DocumentoPdf("Prueba", "Autor", "Asunto");
        doc.Tabla(["A", "B"], [1, 1], Enumerable.Range(1, 120).Select(i => (IReadOnlyList<string>)[$"Fila {i}", "x"]));
        Assert.True(doc.Paginas >= 3);
        var lineas = DocumentoPdf.Partir("Revisar el costo de extracción por tonelada; parece alto frente a faenas similares.", 120, 9, false);
        Assert.True(lineas.Count > 1);
        Assert.All(lineas, l => Assert.True(DocumentoPdf.AnchoTexto(l, 9, false) <= 120));
        Assert.Equal(@"Pr\351stamo \(A\)", DocumentoPdf.Cadena("Préstamo (A)"));
    }

    [Fact]
    public void El_logo_en_png_con_transparencia_se_incrusta_con_su_mascara()
    {
        // PNG de 2 x 1 píxeles RGBA: uno rojo opaco y uno transparente
        byte[] crudo = [0, 255, 0, 0, 255, 0, 0, 0, 0];
        var png = Png(2, 1, 6, crudo);
        var logo = ImagenPdf.Cargar(png);
        Assert.Equal(2, logo.Ancho);
        var doc = new DocumentoPdf("Prueba", "Autor", "Asunto", logo, d => d.DibujarLogo(50, 780, 30, 100));
        var texto = Encoding.Latin1.GetString(doc.Generar());
        Assert.Contains("/Subtype /Image /Width 2 /Height 1", texto);
        Assert.Contains("/SMask 7 0 R", texto);
        Assert.Throws<FormatException>(() => ImagenPdf.Cargar([1, 2, 3, 4, 5, 6, 7, 8, 9]));
    }

    private static byte[] Png(int ancho, int alto, byte tipo, byte[] filasFiltradas)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10]);
        void Bloque(string nombre, byte[] datos)
        {
            ms.Write([(byte)(datos.Length >> 24), (byte)(datos.Length >> 16), (byte)(datos.Length >> 8), (byte)datos.Length]);
            ms.Write(Encoding.ASCII.GetBytes(nombre));
            ms.Write(datos);
            ms.Write([0, 0, 0, 0]);   // CRC: el lector no lo valida
        }
        Bloque("IHDR", [0, 0, 0, (byte)ancho, 0, 0, 0, (byte)alto, 8, tipo, 0, 0, 0]);
        using (var z = new MemoryStream())
        {
            using (var zl = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) zl.Write(filasFiltradas);
            Bloque("IDAT", z.ToArray());
        }
        Bloque("IEND", []);
        return ms.ToArray();
    }
}
