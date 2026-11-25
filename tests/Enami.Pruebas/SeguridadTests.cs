using Enami.Dominio.Modelo;
using Enami.Dominio.Servicios;

namespace Enami.Pruebas;

/// <summary>Medidas de seguridad: bloqueo por intentos fallidos y código firmado de las decisiones por correo.</summary>
public class SeguridadTests
{
    private const string Correo = "ingeniero.copiapo@enami.cl";

    [Fact]
    public void Tras_cinco_intentos_fallidos_el_ingreso_queda_bloqueado_aunque_la_clave_sea_correcta()
    {
        var e = new Entorno();
        for (int i = 0; i < ServicioUsuarios.IntentosMaximos - 1; i++)
            Assert.Null(e.Usuarios.Autenticar(Correo, "clave-incorrecta"));
        Assert.Null(e.Usuarios.Bloqueo(Correo));                 // cuatro fallos todavía no bloquean
        Assert.Null(e.Usuarios.Autenticar(Correo, "clave-incorrecta"));

        var bloqueo = e.Usuarios.Bloqueo(Correo.ToUpperInvariant());
        Assert.NotNull(bloqueo);
        Assert.True(bloqueo <= TimeSpan.FromMinutes(ServicioUsuarios.MinutosBloqueo));
        Assert.Null(e.Usuarios.Autenticar(Correo, Entorno.Clave));   // bloqueado: ni la clave correcta entra
        Assert.Contains(e.Db.Tabla<Auditoria>(), a => a.Accion == "CUENTA_BLOQUEADA");
        Assert.Contains(e.Db.Tabla<Auditoria>(), a => a.Accion == "INGRESO_BLOQUEADO");

        // pasado el tiempo de bloqueo vuelve a poder ingresar
        Assert.Null(e.Usuarios.Bloqueo(Correo, DateTime.Now.AddMinutes(ServicioUsuarios.MinutosBloqueo + 1)));
    }

    [Fact]
    public void Un_ingreso_correcto_reinicia_la_cuenta_de_intentos_fallidos()
    {
        var e = new Entorno();
        for (int i = 0; i < ServicioUsuarios.IntentosMaximos - 1; i++) e.Usuarios.Autenticar(Correo, "clave-incorrecta");
        Assert.NotNull(e.Usuarios.Autenticar(Correo, Entorno.Clave));
        e.Usuarios.Autenticar(Correo, "clave-incorrecta");
        Assert.Null(e.Usuarios.Bloqueo(Correo));
    }

    [Fact]
    public void El_codigo_firmado_solo_sirve_para_su_aprobacion_evaluacion_y_aprobador()
    {
        const string clave = "clave-de-firma-de-prueba";
        var codigo = FirmaDecision.Generar(clave, 7, 12, "Jefe.Copiapo@enami.cl ");

        Assert.True(FirmaDecision.Verificar(clave, 7, 12, "jefe.copiapo@enami.cl", codigo));
        Assert.False(FirmaDecision.Verificar(clave, 8, 12, "jefe.copiapo@enami.cl", codigo));      // otra aprobación (otro nivel)
        Assert.False(FirmaDecision.Verificar(clave, 7, 13, "jefe.copiapo@enami.cl", codigo));      // otra evaluación
        Assert.False(FirmaDecision.Verificar(clave, 7, 12, "subgerente@enami.cl", codigo));        // otro aprobador
        Assert.False(FirmaDecision.Verificar("otra-clave", 7, 12, "jefe.copiapo@enami.cl", codigo));
        Assert.False(FirmaDecision.Verificar(clave, 7, 12, "jefe.copiapo@enami.cl", null));
        Assert.False(FirmaDecision.Iguales("clave-api", "clave-ap"));
        Assert.True(FirmaDecision.Iguales("clave-api", "clave-api"));
    }
}
