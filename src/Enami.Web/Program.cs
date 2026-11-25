using System.Security.Claims;
using Enami.Datos;
using Enami.Dominio.Servicios;
using Enami.Web.Components;
using Enami.Web.Servicios;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
// Las consultas a las API de mercado solo dejan en el registro los avisos, no el detalle de cada solicitud HTTP.
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

// Persistencia (SQL Server o memoria, según "Persistencia" en appsettings.json) y servicios de dominio.
builder.Services.AddPersistenciaEnami(builder.Configuration);

// Inicio de sesión con cookie (RF-11): expira tras 30 minutos sin actividad y se revalida contra la base cada minuto,
// de modo que un usuario deshabilitado pierde el acceso aunque tenga la página abierta (HU-01).
int minutosSesion = builder.Configuration.GetValue("Seguridad:MinutosInactividad", 30);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/cuenta/ingresar";
        o.LogoutPath = "/cuenta/salir";
        o.AccessDeniedPath = "/cuenta/sin-permiso";
        o.ExpireTimeSpan = TimeSpan.FromMinutes(minutosSesion);
        o.SlidingExpiration = true;
        o.Cookie.Name = "enami.sesion";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, RevalidacionSesion>();
builder.Services.AddScoped<AvisoPendiente>();

// Dólar y precio del cobre por API (con edición manual en «Dólar y minerales»); se consulta al iniciar y cada hora.
builder.Services.Configure<OpcionesMercado>(builder.Configuration.GetSection("Mercado"));
builder.Services.AddHttpClient<ConsultaMercado>();
builder.Services.AddHostedService<ConsultaMercadoAutomatica>();

// Límite de intentos de ingreso por dirección IP (además del bloqueo por cuenta tras 5 intentos fallidos, en ServicioUsuarios):
// frena que se prueben contraseñas en muchas cuentas desde un mismo equipo.
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy("ingreso", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.OnRejected = (contexto, _) =>
    {
        contexto.HttpContext.Response.Redirect("/cuenta/ingresar?error=3");
        return ValueTask.CompletedTask;
    };
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 64 * 1024);

var app = builder.Build();

// Fuera de desarrollo, con el flujo de aprobación configurado, la clave para firmar los correos de aprobación es obligatoria.
if (!app.Environment.IsDevelopment() && (app.Configuration.GetSection("Flujos").Get<OpcionesFlujos>()?.FaltaClaveFirma ?? false))
    throw new InvalidOperationException("Falta Flujos:ClaveFirma: sin ella los correos de aprobación no llevan el código firmado y la API los rechazaría.");

// Crea la base de datos y los catálogos; si la base está vacía y existe la carpeta de planillas, las carga.
app.Services.PrepararBaseDatos(app.Environment.ContentRootPath, cargarPlanillas: true);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Ingreso y salida (formularios de la página de inicio de sesión y del menú)
app.MapPost("/cuenta/validar", async (HttpContext http, [FromForm] string correo, [FromForm] string clave,
                                       [FromForm] string? returnUrl, ServicioUsuarios usuarios) =>
{
    var sesion = usuarios.Autenticar(correo, clave);
    if (sesion is null)
    {
        // con el ingreso bloqueado se informa cuánto falta; si no, el mensaje no dice si el correo existe
        var bloqueo = usuarios.Bloqueo(correo);
        var error = bloqueo is TimeSpan b ? $"2&minutos={Math.Max(1, (int)Math.Ceiling(b.TotalMinutes))}" : "1";
        return Results.Redirect($"/cuenta/ingresar?error={error}&correo={Uri.EscapeDataString(correo ?? "")}&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");
    }
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, Sesion.Principal(sesion));
    return Results.LocalRedirect(string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//") ? "/" : returnUrl);
}).AllowAnonymous().RequireRateLimiting("ingreso");

app.MapMethods("/cuenta/salir", ["GET", "POST"], async (HttpContext http, string? motivo) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect(motivo == "inactividad" ? "/cuenta/ingresar?expirada=1" : "/cuenta/ingresar");
}).AllowAnonymous().DisableAntiforgery();

// Exportación del historial de evaluaciones a Excel (RF-29): lo mismo que el usuario ve en la bandeja
app.MapGet("/exportar/evaluaciones", (HttpContext http, ServicioExportacion exportacion, string? estado, string? texto) =>
{
    var usuario = http.User.Usuario();
    if (usuario is null) return Results.Unauthorized();
    var archivo = exportacion.Evaluaciones(usuario, string.IsNullOrWhiteSpace(estado) ? null : estado, texto);
    return Results.File(archivo, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"evaluaciones-{DateTime.Now:yyyy-MM-dd-HHmm}.xlsx");
}).RequireAuthorization();

app.Run();

public partial class Program;
