using Enami.Dominio.Modelo;
using Enami.Dominio.Persistencia;

namespace Enami.Dominio.Servicios;

/// <summary>Persona que participa en el flujo (para el correo).</summary>
public record PersonaFlujo(string Nombre, string Correo);

/// <summary>
/// Aviso que la aplicación envía al flujo de aprobación (BOT-03 y BOT-04 en Azure Logic Apps):
/// APROBACION_PENDIENTE = enviar el correo de aprobación al <see cref="Aprobador"/>;
/// OBSERVADA = avisar al ingeniero que debe corregir; DICTAMEN = avisar al ingeniero que quedó aprobada o rechazada.
/// </summary>
public record EventoFlujo(string Tipo, long IdEvaluacion, string NroEvaluacion, int? Nivel, string? NivelNombre, PersonaFlujo? Aprobador,
                          PersonaFlujo Ingeniero, string Productor, string Faena, string? Oficina, string Instrumento,
                          decimal MontoSolicitado, decimal? Van, string Estado, string? Comentario)
{
    /// <summary>Enlace a la evaluación en la aplicación web (lo completa el notificador).</summary>
    public string? UrlEvaluacion { get; init; }
    /// <summary>Dirección de la API donde el flujo registra la decisión (lo completa el notificador).</summary>
    public string? UrlDecision { get; init; }
}

/// <summary>Envía los avisos al flujo de aprobación. Devuelve nulo si se envió, o el motivo por el que no.</summary>
public interface INotificadorFlujos
{
    Task<string?> EnviarAsync(EventoFlujo evento, CancellationToken ct = default);
}

/// <summary>Una decisión de la cadena de aprobación, para mostrar el historial.</summary>
public record AprobacionVista(long Id, int Nivel, string NivelNombre, string Aprobador, string Correo, DateTime FechaAsignacion,
                              DateTime? FechaDecision, string? Decision, string? Comentario);

/// <summary>Resultado de enviar o decidir: el nuevo estado, un mensaje para el usuario y los avisos para el flujo.</summary>
public record ResultadoAprobacion(string Estado, string Mensaje, IReadOnlyList<EventoFlujo> Eventos);

/// <summary>
/// Aprobación en tres niveles (RF-20 a RF-22, BOT-03): el Ingeniero de Fomento envía la evaluación a revisión (nivel 1),
/// la revisa el Jefe de Oficina de la faena (nivel 2) y la resuelve el Subgerente (nivel 3). Cada aprobador puede aprobar,
/// observar (vuelve al ingeniero para corregir) o rechazar. Las decisiones quedan en APROBACION y en la auditoría; se toman
/// en la aplicación o en el correo de aprobación que envía el flujo de Logic Apps, que las registra por la API.
/// </summary>
public class ServicioAprobaciones(IAlmacen db, ServicioSolicitudes solicitudes, ServicioAuditoria auditoria)
{
    public const string Aprobar = EstadosSolicitud.Aprobada, Observar = EstadosSolicitud.Observada, Rechazar = EstadosSolicitud.Rechazada;
    public const int LargoComentario = 500;

    /// <summary>Convierte la respuesta del correo («Aprobar», «Observar», «Rechazar») o el valor guardado al valor de APROBACION.decision.</summary>
    public static string? Normalizar(string? decision) => (decision ?? "").Trim().ToUpperInvariant() switch
    {
        "APROBAR" or "APROBADA" or "APROBADO" => Aprobar,
        "OBSERVAR" or "OBSERVADA" or "OBSERVADO" => Observar,
        "RECHAZAR" or "RECHAZADA" or "RECHAZADO" => Rechazar,
        _ => null
    };

    public static string NombreNivel(int nivel) => nivel switch { 2 => "Jefe de Oficina", 3 => "Subgerente", _ => "Ingeniero de Fomento" };

    /// <summary>El ingeniero que la registró puede enviarla si está en borrador u observada y tiene un cálculo guardado.</summary>
    public bool PuedeEnviar(SolicitudCredito s, UsuarioSesion u) =>
        s.IdIngeniero == u.Id && s.Estado is EstadosSolicitud.Borrador or EstadosSolicitud.Observada;

    /// <summary>Nivel 1: el ingeniero envía la evaluación a revisión; queda asignada al Jefe de Oficina de la faena.</summary>
    public ResultadoAprobacion Enviar(long idSolicitud, UsuarioSesion usuario)
    {
        var s = Solicitud(idSolicitud);
        if (s.IdIngeniero != usuario.Id) throw new ErrorNegocio("Solo el ingeniero que registró la evaluación puede enviarla a revisión.");
        if (s.Estado is not (EstadosSolicitud.Borrador or EstadosSolicitud.Observada))
            throw new ErrorNegocio("Solo se envían a revisión las evaluaciones en borrador u observadas.");
        if (solicitudes.UltimoResultado(s.Id) is null)
            throw new ErrorNegocio("Antes de enviarla a revisión, complete los datos y guarde la evaluación con el cálculo del VAN.");

        var faena = db.Tabla<Faena>().First(f => f.Id == s.IdFaena);
        // RF-14: la elegibilidad se vuelve a revisar al enviar, porque el padrón puede vencer o el productor caer en mora después de crear el borrador
        var productor = db.Tabla<Productor>().First(p => p.Id == faena.IdProductor);
        var motivos = ReglasElegibilidad.Evaluar(productor, faena, DateTime.Today);
        if (motivos.Count > 0)
            throw new ErrorNegocio("No se puede enviar a revisión: " + string.Join(" ", motivos.Select(m => m.Mensaje)));

        var oficina = faena.IdOficina is int io ? db.Tabla<Oficina>().Where(o => o.Id == io).Select(o => o.Nombre).FirstOrDefault() : null;
        var jefe = (faena.IdOficina is null ? null : Aprobadores(Roles.JefeOficina).FirstOrDefault(u => u.IdOficina == faena.IdOficina))
                   ?? throw new ErrorNegocio($"No hay un Jefe de Oficina activo para {oficina ?? "la oficina de esta faena"}. El administrador debe crearlo en «Usuarios».");

        db.EnTransaccion(() =>
        {
            s.Estado = EstadosSolicitud.EnRevision;
            db.Agregar(new Aprobacion { IdSolicitud = s.Id, IdUsuario = jefe.Id, FechaAsignacion = DateTime.Now });
            db.Guardar();
        });
        auditoria.Registrar("SOLICITUD_ENVIADA", usuario.Id, s.Id, $"Enviada a revisión; asignada a {jefe.NombreCompleto} (Jefe de Oficina)");
        return new ResultadoAprobacion(EstadosSolicitud.EnRevision, $"Evaluación enviada a revisión. La revisa {jefe.NombreCompleto} (Jefe de Oficina).",
            [Evento("APROBACION_PENDIENTE", s, 2, jefe, null)]);
    }

    /// <summary>Decisión tomada en la aplicación por el aprobador que tiene asignada la evaluación.</summary>
    public ResultadoAprobacion Decidir(long idSolicitud, UsuarioSesion usuario, string decision, string? comentario, short? idCausal = null) =>
        Registrar(idSolicitud, usuario.Id, decision, comentario, idCausal, robot: null);

    /// <summary>Decisión que registra el flujo de aprobación (BOT-03) con el correo del aprobador que respondió.</summary>
    public ResultadoAprobacion DecidirPorCorreo(long idSolicitud, string correo, string decision, string? comentario)
    {
        var u = db.Tabla<Usuario>().ToList().FirstOrDefault(x => x.Correo.Equals((correo ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ErrorNegocio($"No hay un usuario con el correo «{correo}».");
        return Registrar(idSolicitud, u.Id, decision, comentario, null, robot: "BOT-03");
    }

    /// <summary>Aprobación pendiente de la evaluación (la que espera decisión), si la hay.</summary>
    public AprobacionVista? Pendiente(long idSolicitud) => Historial(idSolicitud).LastOrDefault(a => a.FechaDecision is null);

    /// <summary>Cadena de aprobación de la evaluación, en orden.</summary>
    public IReadOnlyList<AprobacionVista> Historial(long idSolicitud)
    {
        var lista = db.Tabla<Aprobacion>().Where(a => a.IdSolicitud == idSolicitud).OrderBy(a => a.Id).ToList();
        var ids = lista.Select(a => a.IdUsuario).Distinct().ToList();
        var usuarios = db.Tabla<Usuario>().Where(u => ids.Contains(u.Id)).ToDictionary(u => u.Id);
        var roles = db.Tabla<Rol>().ToDictionary(r => r.Id, r => r.Nombre);
        return lista.Select(a =>
        {
            var u = usuarios.GetValueOrDefault(a.IdUsuario);
            int nivel = Nivel(u is null ? "" : roles.GetValueOrDefault(u.IdRol, ""));
            return new AprobacionVista(a.Id, nivel, NombreNivel(nivel), u?.NombreCompleto ?? "?", u?.Correo ?? "", a.FechaAsignacion,
                a.FechaDecision, a.Decision, a.Comentario);
        }).ToList();
    }

    /// <summary>Evaluaciones que esperan la decisión de este usuario.</summary>
    public IReadOnlyList<long> PendientesDe(int idUsuario) =>
        db.Tabla<Aprobacion>().Where(a => a.IdUsuario == idUsuario && a.FechaDecision == null).Select(a => a.IdSolicitud).ToList();

    private ResultadoAprobacion Registrar(long idSolicitud, int idAprobador, string decision, string? comentario, short? idCausal, string? robot)
    {
        var d = Normalizar(decision) ?? throw new ErrorNegocio("La decisión debe ser Aprobar, Observar o Rechazar.");
        var s = Solicitud(idSolicitud);
        if (s.Estado != EstadosSolicitud.EnRevision)
            throw new ErrorNegocio($"La evaluación {s.NroSolicitud} ya no está en revisión (estado: {s.Estado}).");
        var pendiente = db.Tabla<Aprobacion>().Where(a => a.IdSolicitud == s.Id && a.FechaDecision == null).OrderByDescending(a => a.Id).FirstOrDefault()
                        ?? throw new ErrorNegocio("La evaluación no tiene una aprobación pendiente.");
        var aprobador = db.Tabla<Usuario>().First(u => u.Id == idAprobador);
        if (pendiente.IdUsuario != idAprobador)
        {
            var asignado = db.Tabla<Usuario>().Where(u => u.Id == pendiente.IdUsuario).Select(u => u.NombreCompleto).FirstOrDefault();
            throw new ErrorNegocio($"La evaluación {s.NroSolicitud} está asignada a {asignado}; {aprobador.NombreCompleto} no puede decidirla.");
        }
        comentario = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim();
        if (comentario?.Length > LargoComentario) throw new ErrorNegocio($"El comentario tiene como máximo {LargoComentario} caracteres.");
        if (robot is null && d is Observar or Rechazar && comentario is null)
            throw new ErrorNegocio(d == Observar ? "Indique qué debe corregir el ingeniero." : "Indique el motivo del rechazo.");
        if (idCausal is short c && !db.Tabla<CausalRechazo>().Any(x => x.Id == c)) throw new ErrorNegocio("La causal de rechazo no existe.");

        int nivel = Nivel(db.Tabla<Rol>().Where(r => r.Id == aprobador.IdRol).Select(r => r.Nombre).First());
        // el siguiente aprobador se busca antes de guardar, para no dejar la cadena a medias
        Usuario? siguiente = null;
        if (d == Aprobar && nivel < 3)
            siguiente = Aprobadores(Roles.Subgerente).FirstOrDefault()
                        ?? throw new ErrorNegocio("No hay un Subgerente activo. El administrador debe crearlo en «Usuarios».");

        db.EnTransaccion(() =>
        {
            pendiente.FechaDecision = DateTime.Now;
            pendiente.Decision = d;
            pendiente.Comentario = comentario;
            if (siguiente is not null)
                db.Agregar(new Aprobacion { IdSolicitud = s.Id, IdUsuario = siguiente.Id, FechaAsignacion = DateTime.Now });
            else if (d == Observar)
                s.Estado = EstadosSolicitud.Observada;
            else
            {
                s.Estado = d;                       // APROBADA (Subgerente) o RECHAZADA (cualquier nivel)
                s.FechaDictamen = DateTime.Now;
                if (d == Rechazar) s.IdCausal = idCausal;
            }
            db.Guardar();
        });

        string quien = $"{aprobador.NombreCompleto} ({NombreNivel(nivel)})";
        auditoria.Registrar("DECISION_" + d, robot is null ? aprobador.Id : null, s.Id,
            $"{quien}: {d.ToLowerInvariant()}{(comentario is null ? "" : " — " + comentario)}", robot);

        if (siguiente is not null)
            return new ResultadoAprobacion(s.Estado, $"Aprobada por {quien}. Pasa al Subgerente {siguiente.NombreCompleto}.",
                [Evento("APROBACION_PENDIENTE", s, 3, siguiente, comentario)]);
        if (d == Observar)
            return new ResultadoAprobacion(s.Estado, $"Observada por {quien}. Vuelve al ingeniero para corregirla.",
                [Evento("OBSERVADA", s, nivel, null, comentario)]);
        return new ResultadoAprobacion(s.Estado,
            d == Aprobar ? $"Aprobada por {quien}. El proceso de aprobación terminó." : $"Rechazada por {quien}.",
            [Evento("DICTAMEN", s, nivel, null, comentario)]);
    }

    private SolicitudCredito Solicitud(long id) =>
        db.Tabla<SolicitudCredito>().FirstOrDefault(x => x.Id == id) ?? throw new ErrorNegocio("La evaluación de proyecto no existe.");

    /// <summary>Usuarios activos con ese rol, en orden de creación.</summary>
    private List<Usuario> Aprobadores(string rol)
    {
        var idRol = db.Tabla<Rol>().Where(r => r.Nombre == rol).Select(r => r.Id).First();
        return db.Tabla<Usuario>().Where(u => u.IdRol == idRol && u.Activo).OrderBy(u => u.Id).ToList();
    }

    private static int Nivel(string rol) => rol switch { Roles.JefeOficina => 2, Roles.Subgerente => 3, _ => 1 };

    private EventoFlujo Evento(string tipo, SolicitudCredito s, int? nivel, Usuario? aprobador, string? comentario)
    {
        var faena = db.Tabla<Faena>().First(f => f.Id == s.IdFaena);
        var productor = db.Tabla<Productor>().First(p => p.Id == faena.IdProductor);
        var oficina = faena.IdOficina is int io ? db.Tabla<Oficina>().Where(o => o.Id == io).Select(o => o.Nombre).FirstOrDefault() : null;
        var ingeniero = db.Tabla<Usuario>().First(u => u.Id == s.IdIngeniero);
        var instrumento = db.Tabla<InstrumentoFomento>().Where(i => i.Id == s.IdInstrumento).Select(i => i.Nombre).FirstOrDefault() ?? "";
        var van = solicitudes.UltimoResultado(s.Id)?.Van;
        return new EventoFlujo(tipo, s.Id, s.NroSolicitud, nivel, nivel is int n ? NombreNivel(n) : null,
            aprobador is null ? null : new PersonaFlujo(aprobador.NombreCompleto, aprobador.Correo),
            new PersonaFlujo(ingeniero.NombreCompleto, ingeniero.Correo), productor.NombreMostrado, faena.Nombre, oficina,
            instrumento, s.MontoSolicitado, van, s.Estado, comentario);
    }
}
