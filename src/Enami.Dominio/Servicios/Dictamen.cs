using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Enami.Dominio.Calculo;
using Enami.Dominio.Exportacion;
using Enami.Dominio.Modelo;
using Enami.Dominio.Persistencia;

namespace Enami.Dominio.Servicios;

/// <summary>Membrete del dictamen. El logo se carga desde la configuración (sección «Dictamen»); sin logo se usa la sigla en texto.</summary>
public class OpcionesDictamen
{
    /// <summary>Contenido del archivo del logo (PNG o JPG). Debe ser el oficial, entregado por la institución.</summary>
    public byte[]? Logo { get; set; }
    public string Sigla { get; set; } = "ENAMI";
    public string Institucion { get; set; } = "Empresa Nacional de Minería";
    /// <summary>
    /// Color de acento del membrete y de los títulos («#RRGGBB»), normalmente el color corporativo del logo. Si no tiene
    /// contraste suficiente para texto, los títulos usan el color de texto y el acento queda solo en el filete del membrete.
    /// </summary>
    public string? ColorAcento { get; set; }
    /// <summary>Leyenda opcional al pie de cada página (por ejemplo, «Prototipo académico · datos de prueba»).</summary>
    public string? Leyenda { get; set; }
}

/// <summary>
/// Dictamen de una evaluación en PDF: antecedentes del productor y la faena, resultado del cálculo guardado (reproducido con
/// los mismos valores y la misma versión de las fórmulas, no con los valores de mercado de hoy), resolución con la cadena de
/// aprobación y los valores usados. Respeta el alcance por rol de la bandeja; cada emisión queda en la auditoría con su código
/// de verificación, que se calcula a partir del contenido y permite comprobar que un dictamen impreso corresponde al sistema.
/// </summary>
public class ServicioDictamen(IAlmacen db, ServicioSolicitudes solicitudes, ServicioAprobaciones aprobaciones,
                              IRepositorioPlantillas plantillas, ServicioAuditoria auditoria, OpcionesDictamen? opciones = null)
{
    private static readonly NumberFormatInfo Chile = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = ".", NegativeSign = "-" };
    private readonly OpcionesDictamen _o = opciones ?? new OpcionesDictamen();
    private ColorPdf Acento => ColorPdf.Desde(_o.ColorAcento) ?? ColorPdf.Acento;

    /// <summary>Nombre del archivo y su contenido, o null si la evaluación no existe o el usuario no puede verla.</summary>
    public (string Archivo, byte[] Contenido, string Codigo)? Generar(long idSolicitud, UsuarioSesion usuario, DateTime? ahora = null)
    {
        var d = solicitudes.Obtener(idSolicitud, usuario);
        if (d is null) return null;
        var g = d.UltimoResultado ?? throw new ErrorNegocio("La evaluación aún no tiene un cálculo guardado: guárdela con los datos completos para emitir el dictamen.");
        var momento = ahora ?? DateTime.Now;
        var s = d.Solicitud;
        var plantilla = plantillas.Obtener(g.IdPlantilla);
        var resultado = solicitudes.Reproducir(g);   // el mismo criterio de viabilidad que aplica la aprobación
        double van = resultado?.Van ?? (double)g.Van;
        bool viable = resultado?.Viable ?? van >= 0;
        var historial = aprobaciones.Historial(s.Id);
        var causal = s.IdCausal is short ic ? db.Tabla<CausalRechazo>().FirstOrDefault(c => c.Id == ic) : null;
        var p = d.Productor.Productor;
        var f = d.Faena;
        string estado = ServicioExportacion.NombreEstado(s.Estado);
        string codigo = CodigoVerificacion(s, g);
        bool resuelta = s.Estado is EstadosSolicitud.Aprobada or EstadosSolicitud.Rechazada;

        ImagenPdf? logo = null;
        if (_o.Logo is { Length: > 0 } bytes)
        {
            try { logo = ImagenPdf.Cargar(bytes); }
            catch (FormatException) { logo = null; }   // un logo ilegible no impide emitir el dictamen: se usa la sigla
        }

        var pdf = new DocumentoPdf($"Dictamen {s.NroSolicitud}", $"{_o.Institucion} ({_o.Sigla})", $"Dictamen de evaluación de proyecto {s.NroSolicitud}",
            logo, doc => Membrete(doc, s.NroSolicitud, momento),
            (pagina, total) => (
                $"{_o.Sigla} · Documento de uso interno · Código de verificación {codigo}",
                $"Página {pagina} de {total}",
                $"Emitido el {momento:dd-MM-yyyy HH:mm} por {usuario.NombreCompleto} desde el Sistema de Evaluación de Proyectos de Fomento." +
                (string.IsNullOrWhiteSpace(_o.Leyenda) ? "" : $" {_o.Leyenda.Trim()}")));

        pdf.ColorTitulos = ColorPdf.Desde(_o.ColorAcento) is { } propio
            ? (propio.ContrasteSobreBlanco() >= 4.5 ? propio : ColorPdf.Texto)
            : ColorPdf.AcentoTexto;

        // título y datos de control
        pdf.Texto("Dictamen de evaluación de proyecto", 17, true);
        pdf.Texto($"{d.Instrumento.Nombre}", 10, false, ColorPdf.Secundario);
        pdf.Espacio(10);
        pdf.Pares(
        [
            ("Estado", estado),
            ("Fecha de ingreso", s.FechaIngreso.ToString("dd-MM-yyyy")),
            ("Fecha del dictamen", s.FechaDictamen is DateTime fd ? fd.ToString("dd-MM-yyyy HH:mm") : "Pendiente"),
            ("Código de verificación", codigo),
        ]);
        pdf.Espacio(12);

        // veredicto
        var tono = s.Estado switch
        {
            EstadosSolicitud.Aprobada => TonoPdf.Favorable,
            EstadosSolicitud.Rechazada => TonoPdf.Desfavorable,
            _ => TonoPdf.Pendiente
        };
        string etiqueta = resuelta ? "Resolución" : "Documento preliminar · sin resolución";
        string titulo = s.Estado switch
        {
            EstadosSolicitud.Aprobada => $"Evaluación aprobada · proyecto {(viable ? "viable" : "no viable")}",
            EstadosSolicitud.Rechazada => $"Evaluación rechazada · proyecto {(viable ? "viable" : "no viable")}",
            _ => $"Proyecto {(viable ? "viable" : "no viable")} según el cálculo · {estado.ToLowerInvariant()}"
        };
        var detalle = new List<string>();
        if (causal is not null) detalle.Add($"Causal de rechazo: {causal.Codigo} · {causal.Descripcion}.");
        detalle.Add(resultado?.Modo == ModoCalculo.Escenarios
            ? "Criterio técnico: es viable si la recuperación esperada cubre el financiamiento de ENAMI y el VAN no es negativo."
            : "Criterio técnico: es viable si el VAN no es negativo.");
        if (!resuelta) detalle.Add("Este documento no constituye una resolución: la evaluación aún no tiene una decisión final.");
        pdf.Recuadro(etiqueta, titulo, detalle, tono);

        // 1. resultado
        pdf.Seccion("1. Resultado del cálculo");
        var cifras = new List<(string, string)> { ("VAN", Dolares(van)) };
        if (resultado is { Modo: ModoCalculo.Escenarios } e)
        {
            cifras.Add(("Recuperación esperada", Dolares(e.Recuperacion)));
            cifras.Add(("Financiamiento", Dolares(e.Inversion)));
            cifras.Add(("Precio proyectado", $"{Numero(e.PrecioProyectado, 1)} {e.UnidadPrecio}"));
        }
        else if (resultado is not null)
        {
            cifras.Add(("IVAN · VAN por US$ invertido", Numero(resultado.Ivan, 3)));
            cifras.Add(("Inversión", Dolares(resultado.Inversion)));
            cifras.Add(("Plazo del flujo", $"{resultado.Plazo} meses"));
        }
        pdf.Cifras(cifras);
        pdf.Espacio(6);
        if (resultado is { Modo: ModoCalculo.Escenarios } esc)
        {
            pdf.Pares(
            [
                ("Precio crítico operacional", esc.PrecioCritico is double pc ? $"{Numero(pc, 1)} {esc.UnidadPrecio}" : "—"),
                ("Precio mínimo de retorno", esc.PrecioMinimoRetorno is double pm ? $"{Numero(pm, 1)} {esc.UnidadPrecio}" : "—"),
            ]);
            pdf.Espacio(4);
        }
        pdf.Texto($"Cálculo guardado el {g.Fecha:dd-MM-yyyy HH:mm} con la versión {g.VersionPlantilla} de las fórmulas del instrumento. " +
                  "Montos en dólares de los Estados Unidos.", 8, false, ColorPdf.Secundario);

        // 2. antecedentes
        pdf.Seccion("2. Antecedentes del productor y del proyecto");
        pdf.Pares(
        [
            (p.TipoProductor == TiposProductor.Empresa ? "Razón social" : "Productor", p.NombreMostrado),
            ("RUT", p.Rut),
            ("Faena", $"{f.Faena.Codigo} · {f.Faena.Nombre}"),
            ("Comuna", string.Join(", ", new[] { f.Comuna, f.Region }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } cr ? cr : "—"),
            ("Oficina", f.Oficina ?? "—"),
            ("Ingeniero de fomento", d.Ingeniero),
            ("Monto solicitado", Dolares((double)s.MontoSolicitado)),
            ("Plazo solicitado", $"{s.PlazoMeses} meses"),
        ]);

        // 3. resolución
        pdf.Seccion("3. Cadena de aprobación");
        if (historial.Count == 0)
            pdf.Texto("La evaluación aún no se ha enviado a revisión.", 9, false, ColorPdf.Secundario);
        else
        {
            pdf.Tabla(["Nivel", "Responsable", "Asignada", "Decisión", "Fecha", "Comentario"], [13, 19, 12, 11, 12, 33],
                historial.Select(a => (IReadOnlyList<string>)
                [
                    a.NivelNombre, a.Aprobador, a.FechaAsignacion.ToString("dd-MM-yyyy HH:mm"),
                    a.Decision is null ? "Pendiente" : ServicioExportacion.NombreEstado(a.Decision),
                    a.FechaDecision?.ToString("dd-MM-yyyy HH:mm") ?? "—", string.IsNullOrWhiteSpace(a.Comentario) ? "—" : a.Comentario
                ]));
            var final = historial.LastOrDefault(a => a.FechaDecision is not null && a.Decision is EstadosSolicitud.Aprobada or EstadosSolicitud.Rechazada);
            if (resuelta && final is { Nivel: 1 })
            {
                pdf.Espacio(10);
                pdf.Texto($"Cerrada como no viable por {final.Aprobador} ({final.NivelNombre}) el {final.FechaDecision:dd-MM-yyyy} a las " +
                          $"{final.FechaDecision:HH:mm}. Las evaluaciones no viables no pasan a la visación del Jefe de Oficina ni del Subgerente: " +
                          "se cierran con su causal de rechazo y la decisión queda en el registro de auditoría.", 8.5f, false, ColorPdf.Texto);
            }
            else if (resuelta && final is not null)
            {
                pdf.Espacio(10);
                pdf.Texto($"Resuelta electrónicamente por {final.Aprobador} ({final.NivelNombre}) el {final.FechaDecision:dd-MM-yyyy} a las " +
                          $"{final.FechaDecision:HH:mm}, mediante el flujo de aprobación del sistema. La identidad del responsable y cada " +
                          "decisión quedan en el registro de auditoría, por lo que el documento no requiere firma manuscrita.", 8.5f, false, ColorPdf.Texto);
            }
        }

        // 4. valores
        pdf.Seccion("4. Valores usados en el cálculo");
        var variables = plantilla?.Variables.OrderBy(v => v.Orden).ToList() ?? [];
        var conocidas = variables.Select(v => v.Codigo).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filas = new List<IReadOnlyList<string>>();
        foreach (var grupo in variables.Where(v => g.Valores.ContainsKey(v.Codigo)).GroupBy(v => string.IsNullOrWhiteSpace(v.Seccion) ? "" : v.Seccion))
        {
            if (grupo.Key.Length > 0) filas.Add([grupo.Key]);
            filas.AddRange(grupo.Select(v => (IReadOnlyList<string>)[v.Nombre, ValorVariable(v, (double)g.Valores[v.Codigo]), v.Unidad]));
        }
        filas.AddRange(g.Valores.Where(x => !conocidas.Contains(x.Key)).Select(x => (IReadOnlyList<string>)[x.Key, Numero((double)x.Value, 4, recortar: true), ""]));
        pdf.Tabla(["Variable", "Valor", "Unidad"], [60, 18, 22], filas, [false, true, false]);
        pdf.Espacio(8);
        pdf.Texto("Valores tal como se guardaron en el cálculo (datos del proyecto ingresados por el ingeniero y valores de mercado vigentes " +
                  "en ese momento). El dictamen reproduce ese cálculo, por lo que puede diferir del que muestra la aplicación con los valores de hoy.",
                  7.5f, false, ColorPdf.Secundario);

        var contenido = pdf.Generar();
        auditoria.Registrar("DICTAMEN_PDF", usuario.Id, s.Id, $"Dictamen en PDF de {s.NroSolicitud} ({estado}); código de verificación {codigo}");
        return ($"dictamen-{s.NroSolicitud}.pdf", contenido, codigo);
    }

    /// <summary>Membrete de cada página: logo (o sigla) a la izquierda; tipo de documento, número y fecha de emisión a la derecha.</summary>
    private void Membrete(DocumentoPdf doc, string nro, DateTime momento)
    {
        float arriba = doc.Y, derecha = DocumentoPdf.Margen + doc.AnchoUtil, alto = 34;
        if (doc.Logo is not null)
            doc.DibujarLogo(DocumentoPdf.Margen, arriba - alto, alto, 170);
        else
        {
            doc.EscribirTexto(DocumentoPdf.Margen, arriba - 17, _o.Sigla, 19, true, ColorPdf.Texto);
            doc.EscribirTexto(DocumentoPdf.Margen, arriba - 30, _o.Institucion, 8, false, ColorPdf.Secundario);
        }
        doc.EscribirTextoDerecha(derecha, arriba - 8, "DICTAMEN DE EVALUACIÓN DE PROYECTO", 7, true, ColorPdf.Secundario);
        doc.EscribirTextoDerecha(derecha, arriba - 21, $"N.º {nro}", 11, true, ColorPdf.Texto);
        doc.EscribirTextoDerecha(derecha, arriba - 32, $"Emitido el {momento:dd-MM-yyyy HH:mm}", 7.5f, false, ColorPdf.Secundario);
        doc.Espacio(alto + 8);
        doc.Rectangulo(DocumentoPdf.Margen, doc.Y - 1.5f, doc.AnchoUtil, 1.5f, Acento);
        doc.Espacio(20);
    }

    /// <summary>
    /// Código de 12 caracteres (SHA-256 del número, el estado, la fecha del dictamen, el cálculo y su VAN). Cambia si cambia
    /// cualquiera de esos datos, así que un dictamen impreso se puede comparar con el que emite hoy el sistema.
    /// </summary>
    public static string CodigoVerificacion(SolicitudCredito s, ResultadoGuardado g)
    {
        var texto = string.Join("|", s.Id, s.NroSolicitud, s.Estado, s.FechaDictamen?.ToString("O") ?? "", s.IdCausal?.ToString() ?? "",
            g.Id, g.Van.ToString("F2", CultureInfo.InvariantCulture));
        var hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto)))[..12];
        return $"{hex[..4]}-{hex[4..8]}-{hex[8..]}";
    }

    private static string ValorVariable(VariableCalculo v, double valor)
    {
        var opcion = v.ListaOpciones().FirstOrDefault(o => Math.Abs(o.Valor - valor) < 1e-9);
        return opcion.Texto is { Length: > 0 } t ? t : Numero(valor, 4, recortar: true);
    }

    public static string Dolares(double v) => "US$ " + Numero(v, Math.Abs(v) >= 1000 ? 0 : 2);

    public static string Numero(double v, int decimales, bool recortar = false)
    {
        var s = v.ToString(recortar ? "#,##0." + new string('#', decimales) : "N" + decimales, Chile);
        return s.EndsWith(',') ? s[..^1] : s;
    }
}
