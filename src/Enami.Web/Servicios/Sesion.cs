using System.Security.Claims;
using Enami.Dominio.Servicios;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace Enami.Web.Servicios;

/// <summary>Convierte la sesión del dominio en claims de la cookie y viceversa.</summary>
public static class Sesion
{
    private const string TipoOficinaId = "enami:oficina_id", TipoOficina = "enami:oficina";

    public static ClaimsPrincipal Principal(UsuarioSesion s)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, s.Id.ToString()),
            new(ClaimTypes.Email, s.Correo),
            new(ClaimTypes.Name, s.NombreCompleto),
            new(ClaimTypes.Role, s.Rol),
        };
        if (s.IdOficina is int o) claims.Add(new(TipoOficinaId, o.ToString()));
        if (s.Oficina is not null) claims.Add(new(TipoOficina, s.Oficina));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    /// <summary>Datos del usuario conectado; null si no hay sesión.</summary>
    public static UsuarioSesion? Usuario(this ClaimsPrincipal p)
    {
        if (p.Identity?.IsAuthenticated != true || !int.TryParse(p.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        int? oficina = int.TryParse(p.FindFirstValue(TipoOficinaId), out var o) ? o : null;
        return new UsuarioSesion(id, p.FindFirstValue(ClaimTypes.Email) ?? "", p.FindFirstValue(ClaimTypes.Name) ?? "",
                                 p.FindFirstValue(ClaimTypes.Role) ?? "", oficina, p.FindFirstValue(TipoOficina));
    }

    public static async Task<UsuarioSesion> UsuarioAsync(this Task<AuthenticationState> estado) =>
        (await estado).User.Usuario() ?? throw new InvalidOperationException("No hay una sesión activa.");
}

/// <summary>
/// Revisa cada minuto que el usuario siga habilitado y con el mismo rol. Si el administrador lo deshabilita
/// o le cambia el rol, la sesión abierta en el navegador termina (HU-01).
/// </summary>
public class RevalidacionSesion(ILoggerFactory log, IServiceScopeFactory scopes) : RevalidatingServerAuthenticationStateProvider(log)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState estado, CancellationToken cancelacion)
    {
        var u = estado.User.Usuario();
        if (u is null) return Task.FromResult(false);
        using var scope = scopes.CreateScope();
        var actual = scope.ServiceProvider.GetRequiredService<ServicioUsuarios>().Sesion(u.Id);
        return Task.FromResult(actual is not null && actual.Rol == u.Rol && actual.IdOficina == u.IdOficina);
    }
}
