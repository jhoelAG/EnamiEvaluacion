using System.Net.Mail;
using Enami.Dominio.Modelo;
using Enami.Dominio.Persistencia;

namespace Enami.Dominio.Servicios;

/// <summary>
/// Calcula y verifica el hash de las contraseñas. En la aplicación web se usa el PasswordHasher
/// de ASP.NET Core Identity (PBKDF2 con sal aleatoria); las pruebas usan una implementación propia.
/// </summary>
public interface IHasherClaves
{
    string Calcular(string clave);
    bool Verificar(string hash, string clave);
}

/// <summary>Datos de la sesión de un usuario autenticado.</summary>
public record UsuarioSesion(int Id, string Correo, string NombreCompleto, string Rol, int? IdOficina, string? Oficina);

/// <summary>Fila de la lista de usuarios (con los nombres del rol y la oficina).</summary>
public record UsuarioVista(int Id, string Correo, string NombreCompleto, string Rol, int? IdOficina, string? Oficina, bool Activo);

/// <summary>Datos que se ingresan al crear o editar un usuario.</summary>
public class DatosUsuario
{
    public string Correo { get; set; } = "";
    public string NombreCompleto { get; set; } = "";
    public string Rol { get; set; } = Roles.Ingeniero;
    public int? IdOficina { get; set; }
    /// <summary>Obligatoria al crear; al editar, vacía significa «no cambiar».</summary>
    public string? Clave { get; set; }
}

public class ErrorNegocio(string mensaje) : Exception(mensaje);

/// <summary>Gestión de usuarios y autenticación (HU-01, HU-02, RF-10, RF-11).</summary>
public class ServicioUsuarios(IAlmacen db, IHasherClaves hasher, ServicioAuditoria auditoria)
{
    public const int LargoMinimoClave = 8;

    public IReadOnlyList<UsuarioVista> Listar()
    {
        var roles = db.Tabla<Rol>().ToDictionary(r => r.Id, r => r.Nombre);
        var oficinas = db.Tabla<Oficina>().ToDictionary(o => o.Id, o => o.Nombre);
        return db.Tabla<Usuario>().ToList()
            .Select(u => new UsuarioVista(u.Id, u.Correo, u.NombreCompleto, roles.GetValueOrDefault(u.IdRol, "?"), u.IdOficina,
                                          u.IdOficina is int o ? oficinas.GetValueOrDefault(o) : null, u.Activo))
            .OrderBy(u => u.NombreCompleto).ToList();
    }

    public int Crear(DatosUsuario d, int idAdministrador)
    {
        var (correo, rol) = Validar(d, idExistente: null);
        if (string.IsNullOrEmpty(d.Clave)) throw new ErrorNegocio("La contraseña inicial es obligatoria.");
        ValidarClave(d.Clave);
        var u = new Usuario
        {
            Correo = correo, NombreCompleto = d.NombreCompleto.Trim(), IdRol = rol.Id,
            IdOficina = Roles.RequiereOficina(rol.Nombre) ? d.IdOficina : null,
            Activo = true, ClaveHash = hasher.Calcular(d.Clave)
        };
        db.Agregar(u);
        db.Guardar();
        auditoria.Registrar("USUARIO_CREADO", idAdministrador, detalle: $"{u.Correo} ({rol.Nombre})");
        return u.Id;
    }

    public void Editar(int id, DatosUsuario d, int idAdministrador)
    {
        var u = db.Tabla<Usuario>().FirstOrDefault(x => x.Id == id) ?? throw new ErrorNegocio("El usuario no existe.");
        var (correo, rol) = Validar(d, id);
        if (u.Id == idAdministrador && rol.Nombre != Roles.Administrador)
            throw new ErrorNegocio("No puede quitarse a sí mismo el rol de Administrador.");
        u.Correo = correo;
        u.NombreCompleto = d.NombreCompleto.Trim();
        u.IdRol = rol.Id;
        u.IdOficina = Roles.RequiereOficina(rol.Nombre) ? d.IdOficina : null;
        if (!string.IsNullOrEmpty(d.Clave))
        {
            ValidarClave(d.Clave);
            u.ClaveHash = hasher.Calcular(d.Clave);
        }
        db.Guardar();
        auditoria.Registrar("USUARIO_EDITADO", idAdministrador, detalle: $"{u.Correo} ({rol.Nombre})" + (string.IsNullOrEmpty(d.Clave) ? "" : "; contraseña restablecida"));
    }

    /// <summary>Habilita o deshabilita un usuario. No se elimina, para conservar la trazabilidad de sus acciones.</summary>
    public void CambiarEstado(int id, bool activo, int idAdministrador)
    {
        var u = db.Tabla<Usuario>().FirstOrDefault(x => x.Id == id) ?? throw new ErrorNegocio("El usuario no existe.");
        if (!activo && u.Id == idAdministrador) throw new ErrorNegocio("No puede deshabilitar su propia cuenta.");
        u.Activo = activo;
        db.Guardar();
        auditoria.Registrar(activo ? "USUARIO_HABILITADO" : "USUARIO_DESHABILITADO", idAdministrador, detalle: u.Correo);
    }

    /// <summary>Verifica correo y contraseña. Devuelve null si no coinciden o si el usuario está deshabilitado.</summary>
    /// <summary>Intentos fallidos permitidos dentro de la ventana antes de bloquear el ingreso con ese correo.</summary>
    public const int IntentosMaximos = 5;
    /// <summary>Minutos de la ventana de intentos y del bloqueo.</summary>
    public const int MinutosBloqueo = 15;

    public UsuarioSesion? Autenticar(string correo, string clave)
    {
        correo = (correo ?? "").Trim().ToLowerInvariant();
        var u = db.Tabla<Usuario>().FirstOrDefault(x => x.Correo == correo);
        // con el ingreso bloqueado ni siquiera se revisa la contraseña: así no se pueden seguir probando claves
        if (Bloqueo(correo) is not null)
        {
            auditoria.Registrar("INGRESO_BLOQUEADO", u?.Id, detalle: correo);
            return null;
        }
        if (u is null || string.IsNullOrEmpty(clave) || !hasher.Verificar(u.ClaveHash, clave))
        {
            auditoria.Registrar("INGRESO_FALLIDO", u?.Id, detalle: correo);
            if (Bloqueo(correo) is not null)
                auditoria.Registrar("CUENTA_BLOQUEADA", u?.Id, detalle: $"{correo}: {IntentosMaximos} intentos fallidos; ingreso bloqueado por {MinutosBloqueo} minutos");
            return null;
        }
        if (!u.Activo)
        {
            auditoria.Registrar("INGRESO_RECHAZADO", u.Id, detalle: "Usuario deshabilitado");
            return null;
        }
        auditoria.Registrar("INGRESO", u.Id);
        return Sesion(u.Id);
    }

    /// <summary>
    /// Tiempo que falta para poder ingresar con ese correo, o null si no está bloqueado. Se bloquea tras <see cref="IntentosMaximos"/>
    /// intentos fallidos en <see cref="MinutosBloqueo"/> minutos, contados desde el último ingreso correcto; el bloqueo dura
    /// <see cref="MinutosBloqueo"/> minutos desde el último intento fallido. Se calcula con la auditoría, así que no se pierde al reiniciar.
    /// </summary>
    public TimeSpan? Bloqueo(string correo, DateTime? ahora = null)
    {
        correo = (correo ?? "").Trim().ToLowerInvariant();
        var momento = ahora ?? DateTime.Now;
        var desde = momento.AddMinutes(-MinutosBloqueo);
        var fallos = db.Tabla<Auditoria>().Where(a => a.Accion == "INGRESO_FALLIDO" && a.Fecha >= desde && a.Detalle == correo)
            .Select(a => a.Fecha).ToList();
        if (fallos.Count < IntentosMaximos) return null;
        var id = db.Tabla<Usuario>().Where(x => x.Correo == correo).Select(x => (int?)x.Id).FirstOrDefault();
        var ultimoIngreso = id is int i
            ? db.Tabla<Auditoria>().Where(a => a.Accion == "INGRESO" && a.IdUsuario == i && a.Fecha >= desde).Select(a => (DateTime?)a.Fecha).Max()
            : null;
        var vigentes = fallos.Where(f => ultimoIngreso is null || f > ultimoIngreso).ToList();
        if (vigentes.Count < IntentosMaximos) return null;
        var resta = vigentes.Max().AddMinutes(MinutosBloqueo) - momento;
        return resta > TimeSpan.Zero ? resta : null;
    }

    /// <summary>Datos actuales de la sesión; null si el usuario ya no existe o fue deshabilitado (se revisa periódicamente).</summary>
    public UsuarioSesion? Sesion(int id)
    {
        var u = db.Tabla<Usuario>().FirstOrDefault(x => x.Id == id && x.Activo);
        if (u is null) return null;
        var rol = db.Tabla<Rol>().First(r => r.Id == u.IdRol).Nombre;
        string? oficina = u.IdOficina is int o ? db.Tabla<Oficina>().Where(x => x.Id == o).Select(x => x.Nombre).FirstOrDefault() : null;
        return new UsuarioSesion(u.Id, u.Correo, u.NombreCompleto, rol, u.IdOficina, oficina);
    }

    public static void ValidarClave(string clave)
    {
        if (clave.Length < LargoMinimoClave || !clave.Any(char.IsLetter) || !clave.Any(char.IsDigit))
            throw new ErrorNegocio($"La contraseña debe tener al menos {LargoMinimoClave} caracteres e incluir letras y números.");
    }

    private (string Correo, Rol Rol) Validar(DatosUsuario d, int? idExistente)
    {
        var correo = (d.Correo ?? "").Trim().ToLowerInvariant();
        if (correo.Length == 0 || correo.Length > 120 || !MailAddress.TryCreate(correo, out var m) || m.Address != correo || !correo.Contains('.'))
            throw new ErrorNegocio("Ingrese un correo válido (por ejemplo, nombre.apellido@enami.cl).");
        if (string.IsNullOrWhiteSpace(d.NombreCompleto) || d.NombreCompleto.Trim().Length > 120)
            throw new ErrorNegocio("El nombre completo es obligatorio (máximo 120 caracteres).");
        var rol = db.Tabla<Rol>().FirstOrDefault(r => r.Nombre == d.Rol) ?? throw new ErrorNegocio("Seleccione un rol válido.");
        if (Roles.RequiereOficina(rol.Nombre))
        {
            if (d.IdOficina is null) throw new ErrorNegocio($"El rol {Roles.Nombre(rol.Nombre)} requiere una oficina.");
            if (!db.Tabla<Oficina>().Any(o => o.Id == d.IdOficina)) throw new ErrorNegocio("La oficina no existe.");
        }
        if (db.Tabla<Usuario>().Any(u => u.Correo == correo && u.Id != (idExistente ?? 0)))
            throw new ErrorNegocio("Ya existe un usuario con ese correo.");
        return (correo, rol);
    }
}

/// <summary>Registro inmodificable de acciones (tabla AUDITORIA, RF-27).</summary>
public class ServicioAuditoria(IAlmacen db)
{
    public void Registrar(string accion, int? idUsuario, long? idSolicitud = null, string? detalle = null, string? robot = null)
    {
        db.Agregar(new Auditoria
        {
            Fecha = DateTime.Now, Accion = accion, IdUsuario = idUsuario, IdSolicitud = idSolicitud, Robot = robot,
            Detalle = detalle is { Length: > 1000 } ? detalle[..1000] : detalle
        });
        db.Guardar();
    }

    /// <summary>
    /// Busca en la auditoría por fecha (días completos), acción, usuario o robot, y número de evaluación. Devuelve las más recientes
    /// primero, como máximo <paramref name="maximo"/>.
    /// </summary>
    public IReadOnlyList<Auditoria> Buscar(DateTime? desde, DateTime? hasta, string? accion, string? autor, string? nroEvaluacion, int maximo = 500)
    {
        var q = db.Tabla<Auditoria>();
        if (desde is DateTime d) { var inicio = d.Date; q = q.Where(a => a.Fecha >= inicio); }
        if (hasta is DateTime h) { var fin = h.Date.AddDays(1); q = q.Where(a => a.Fecha < fin); }
        if (!string.IsNullOrWhiteSpace(accion)) q = q.Where(a => a.Accion == accion);
        if (!string.IsNullOrWhiteSpace(autor))
            q = int.TryParse(autor, out var idUsuario) ? q.Where(a => a.IdUsuario == idUsuario) : q.Where(a => a.Robot == autor);
        if (!string.IsNullOrWhiteSpace(nroEvaluacion))
        {
            var nro = nroEvaluacion.Trim();
            var ids = db.Tabla<SolicitudCredito>().Where(s => s.NroSolicitud.Contains(nro)).Select(s => s.Id).ToList();
            q = q.Where(a => a.IdSolicitud != null && ids.Contains(a.IdSolicitud.Value));
        }
        return q.OrderByDescending(a => a.Id).Take(maximo).ToList();
    }

    /// <summary>Acciones y robots distintos que hay en la auditoría, para los filtros.</summary>
    public (IReadOnlyList<string> Acciones, IReadOnlyList<string> Robots) Catalogo() =>
        (db.Tabla<Auditoria>().Select(a => a.Accion).Distinct().OrderBy(a => a).ToList(),
         db.Tabla<Auditoria>().Where(a => a.Robot != null).Select(a => a.Robot!).Distinct().OrderBy(r => r).ToList());

    public IReadOnlyList<Auditoria> Ultimos(int cantidad = 50) =>
        db.Tabla<Auditoria>().OrderByDescending(a => a.Id).Take(cantidad).ToList();
}
