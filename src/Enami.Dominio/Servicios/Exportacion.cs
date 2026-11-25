using Enami.Dominio.Exportacion;
using Enami.Dominio.Modelo;
using Enami.Dominio.Persistencia;

namespace Enami.Dominio.Servicios;

/// <summary>
/// Exportación del historial de evaluaciones a Excel (RF-29, HU-17). Exporta lo mismo que la bandeja muestra al usuario
/// (mismo alcance por rol, filtro de estado y búsqueda), más el plazo, la fecha del dictamen y la causal de rechazo.
/// Cada exportación queda en la auditoría.
/// </summary>
public class ServicioExportacion(IAlmacen db, ServicioSolicitudes solicitudes, ServicioAuditoria auditoria)
{
    public static readonly string[] Columnas =
    [
        "N.º", "Fecha de ingreso", "Estado", "Productor", "RUT", "Faena", "Código de faena", "Oficina", "Instrumento",
        "Monto solicitado (US$)", "Plazo (meses)", "VAN último cálculo", "Fecha del dictamen", "Causal de rechazo", "Ingeniero"
    ];
    private static readonly double[] Anchos = [15, 17, 12, 30, 13, 24, 10, 26, 34, 14, 9, 16, 17, 40, 24];

    public byte[] Evaluaciones(UsuarioSesion usuario, string? estado, string? texto)
    {
        var lista = solicitudes.Listar(usuario, estado, texto);
        var ids = lista.Select(s => s.Id).ToList();
        var dictamen = db.Tabla<SolicitudCredito>().Where(s => ids.Contains(s.Id)).ToList().ToDictionary(s => s.Id);
        var causales = db.Tabla<CausalRechazo>().ToDictionary(c => c.Id, c => $"{c.Codigo} · {c.Descripcion}");

        var filas = lista.Select(s =>
        {
            var d = dictamen[s.Id];
            return (IReadOnlyList<object?>)new object?[]
            {
                s.NroSolicitud, s.FechaIngreso, NombreEstado(s.Estado), s.Productor, s.RutProductor, s.Faena, s.CodigoFaena,
                s.Oficina, s.Instrumento, s.Monto, s.Plazo, s.UltimoVan, d.FechaDictamen,
                d.IdCausal is short c ? causales.GetValueOrDefault(c) : null, s.Ingeniero
            };
        });
        var archivo = EscritorXlsx.Escribir("Evaluaciones", Columnas, filas, Anchos);
        auditoria.Registrar("EXPORTACION", usuario.Id, null,
            $"Historial de evaluaciones a Excel: {lista.Count} filas (estado: {estado ?? "todos"}; búsqueda: {(string.IsNullOrWhiteSpace(texto) ? "—" : texto)})");
        return archivo;
    }

    public static string NombreEstado(string estado) => estado switch
    {
        EstadosSolicitud.Borrador => "Borrador",
        EstadosSolicitud.EnRevision => "En revisión",
        EstadosSolicitud.Observada => "Observada",
        EstadosSolicitud.Aprobada => "Aprobada",
        EstadosSolicitud.Rechazada => "Rechazada",
        _ => estado
    };
}
